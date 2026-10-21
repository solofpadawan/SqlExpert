using System.Diagnostics;
using System.IO;
using System.Windows;
using SqlExpert.App.Views;
using SqlExpert.Core.About;
using SqlExpert.Core.Updates;

namespace SqlExpert.App.Services;

/// <summary>
/// Update check against the public GitHub releases of the project (startup and Help &gt; Check for updates...),
/// download + verification + staging, and hand-over to the updater mode of SqlExpert.exe when the app closes.
/// </summary>
public static class UpdateService
{
    private static int _checking;

    /// <summary>Version of the running build (ProductVersionInfo); 0.0.0 when it cannot be parsed.</summary>
    public static SemVersion CurrentVersion { get; } =
        SemVersion.TryParse(ProductVersionInfo.FromAssembly(typeof(App).Assembly).Version, out var v) ? v! : new SemVersion(0, 0, 0);

    /// <summary>Effective "Receber versões de teste (pre-release)" (saved choice, or the default of this version).</summary>
    public static bool IncludePreReleases => UiSettings.Instance.IncludePreReleases;

    /// <summary>Updater prepared by "Atualizar agora"; started when the main window has closed.</summary>
    public static PendingUpdate? Pending { get; private set; }

    public sealed record PendingUpdate(string UpdaterExe, UpdaterCommandLine Arguments);

    /// <summary>Folder of the running SqlExpert.exe.</summary>
    public static string InstallDirectory =>
        Path.GetDirectoryName(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, UpdatePackage.ExecutableName)) ?? AppContext.BaseDirectory;

    /// <summary>A "dotnet build" output (SqlExpert.dll beside the exe) is updated with git + build, not with the release zip.</summary>
    public static bool IsDevelopmentBuild => File.Exists(Path.Combine(InstallDirectory, "SqlExpert.dll"));

    private static UpdateClient NewClient() => new(userAgentVersion: CurrentVersion.ToString());

    /// <summary>Background check after the main window is shown. Silent on errors and when up to date (only update.log).</summary>
    public static async Task CheckOnStartupAsync(Window main)
    {
        try
        {
            _ = Task.Run(() => UpdateWorkspace.CleanupOld());
            if (!UiSettings.Instance.CheckForUpdatesOnStartup) return;
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
            if (!main.IsVisible || Interlocked.Exchange(ref _checking, 1) == 1) return;
            UpdateCheckResult result;
            try
            {
                using var client = NewClient();
                result = await Task.Run(() => UpdateChecker.CheckAsync(client, CurrentVersion, IncludePreReleases,
                    UiSettings.Instance.SkippedUpdateVersion, UpdateChecker.StartupTimeout)).ConfigureAwait(true);
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
            }
            UpdateLog.Write(result.Error is not null
                ? $"verificação ao iniciar falhou: {result.Error}"
                : result.Candidate is { } c ? $"nova versão disponível: {c.Version} (atual {CurrentVersion})" : $"sem atualização (atual {CurrentVersion}, {result.ReleaseCount} release(s))");
            if (result.Candidate is null || !main.IsVisible || Pending is not null) return;
            ShowUpdate(main, result.Candidate);
        }
        catch (Exception ex)
        {
            UpdateLog.Write("verificação ao iniciar: " + ex);
        }
    }

    /// <summary>Help &gt; Check for updates...: always reports the outcome (up to date, error or the new version).</summary>
    public static async Task CheckManuallyAsync(Window? owner, Action<string> setStatus)
    {
        if (Pending is not null)
        {
            Info(owner, $"A versão {Pending.Arguments.Version} já foi baixada e será instalada quando você fechar o SqlExpert.");
            return;
        }
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        UpdateCheckResult result;
        try
        {
            setStatus("Verificando atualizações no GitHub...");
            using var client = NewClient();
            result = await Task.Run(() => UpdateChecker.CheckAsync(client, CurrentVersion, IncludePreReleases,
                skippedVersion: null, UpdateChecker.ManualTimeout)).ConfigureAwait(true);
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
        UpdateLog.Write(result.Error is not null ? $"verificação manual falhou: {result.Error}" : $"verificação manual: {(result.Candidate?.Version.ToString() ?? "sem atualização")}");
        if (result.Error is not null)
        {
            setStatus("Não foi possível verificar atualizações.");
            if (Ask(owner, $"Não foi possível verificar se há uma versão nova:\n\n{result.Error}\n\nAbrir a página de releases no navegador?", MessageBoxImage.Warning))
                OpenUrl(ReleaseSelector.ReleasesPageUrl);
            return;
        }
        if (result.Candidate is null)
        {
            setStatus($"SqlExpert {CurrentVersion} é a versão mais recente.");
            Info(owner, $"Você já está usando a versão mais recente do SqlExpert ({CurrentVersion}).\n\n" +
                        (IncludePreReleases ? "Versões de teste (pre-release) também foram consideradas." : "Versões de teste (pre-release) não foram consideradas."));
            return;
        }
        setStatus($"Nova versão disponível: {result.Candidate.Version}.");
        if (owner is not null) ShowUpdate(owner, result.Candidate);
    }

    private static void ShowUpdate(Window owner, UpdateCandidate candidate)
    {
        var dlg = new UpdateWindow(candidate, CurrentVersion, IncludePreReleases, (progress, ct) => PrepareAsync(owner, candidate, progress, ct))
        {
            Owner = owner
        };
        dlg.ShowDialog();
        if (dlg.PreReleaseChanged) UiSettings.Instance.IncludePreReleasesSetting = dlg.IncludePreReleases;
        switch (dlg.Choice)
        {
            case UpdateChoice.Skip:
                UiSettings.Instance.SkippedUpdateVersion = candidate.Version.ToString();
                UpdateLog.Write($"versão {candidate.Version} pulada pelo usuário");
                break;
            case UpdateChoice.Install when Pending is not null:
                UpdateLog.Write($"versão {candidate.Version} pronta; fechando para instalar");
                if (owner is MainWindow main)
                {
                    if (main.DataContext is ViewModels.MainViewModel vm)
                        vm.StatusText = $"Atualização {candidate.Version} pronta: será instalada quando o SqlExpert fechar.";
                    main.Close(); // asks about unsaved documents / open transactions like a normal close
                }
                break;
        }
    }

    /// <summary>
    /// "Atualizar agora": checks the program folder, downloads and verifies the package, extracts it, copies the
    /// running exe as updater and asks to close. Returns false (with a message already shown) when it cannot go on.
    /// </summary>
    private static async Task<bool> PrepareAsync(Window owner, UpdateCandidate candidate, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        var dialogOwner = Application.Current?.Windows.OfType<UpdateWindow>().FirstOrDefault() as Window ?? owner;
        var installDir = InstallDirectory;
        if (IsDevelopmentBuild)
        {
            if (Ask(dialogOwner, $"Esta cópia do SqlExpert foi compilada a partir do código ({installDir}).\n\n" +
                                 "Atualize com git pull + dotnet build. Abrir a página da versão no navegador?", MessageBoxImage.Information))
                OpenUrl(candidate.Release.HtmlUrl);
            return false;
        }
        if (!UpdatePackage.IsDirectoryWritable(installDir))
        {
            if (Ask(dialogOwner, $"A pasta do programa não permite gravação:\n{installDir}\n\n" +
                                 "Para atualizar, baixe o .zip manualmente (ou mova o SqlExpert para uma pasta sua, como C:\\Ferramentas\\SqlExpert).\n\n" +
                                 "Abrir a página da versão no navegador?", MessageBoxImage.Warning))
                OpenUrl(candidate.Release.HtmlUrl);
            return false;
        }

        var ws = UpdateWorkspace.For(candidate.Version.ToString());
        var zip = Path.Combine(ws.DownloadDir, candidate.Package.Name);
        UpdateLog.Write($"baixando {candidate.Package.Name} ({candidate.Package.Size} bytes) de {candidate.Package.DownloadUrl}");
        using (var client = NewClient())
        {
            var expected = await UpdateChecker.ResolveExpectedHashAsync(client, candidate, ct).ConfigureAwait(true);
            var hash = await client.DownloadAsync(candidate.Package, zip, expected, progress, ct).ConfigureAwait(true);
            UpdateLog.Write($"download ok, SHA-256 {hash}" + (expected is null ? " (release sem SHA-256 publicado; só o tamanho foi conferido)" : " (confere)"));
        }

        var content = await Task.Run(() =>
        {
            var c = UpdatePackage.ExtractAndValidate(zip, ws.StagingDir);
            // The updater is a copy of the running exe, outside the program folder (so that folder can be replaced).
            Directory.CreateDirectory(ws.UpdaterDir);
            var updaterExe = Path.Combine(ws.UpdaterDir, UpdatePackage.ExecutableName);
            File.Copy(Environment.ProcessPath!, updaterExe, overwrite: true);
            if (Directory.Exists(ws.BackupDir)) Directory.Delete(ws.BackupDir, recursive: true);
            return c;
        }, ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();

        if (!Ask(dialogOwner, $"A versão {candidate.Version} foi baixada e conferida.\n\n" +
                              "O SqlExpert vai fechar, instalar a atualização e abrir de novo. Suas conexões e preferências são mantidas.\n\nContinuar agora?",
                MessageBoxImage.Question))
        {
            UpdateLog.Write("instalação adiada pelo usuário");
            return false;
        }

        Pending = new PendingUpdate(
            Path.Combine(ws.UpdaterDir, UpdatePackage.ExecutableName),
            new UpdaterCommandLine(Environment.ProcessId, content, installDir, ws.BackupDir, candidate.Version.ToString()));
        return true;
    }

    /// <summary>Called once the main window has closed: starts the updater, which waits for this process to exit.</summary>
    public static void LaunchPendingUpdater()
    {
        var p = Pending;
        if (p is null) return;
        Pending = null;
        try
        {
            var psi = new ProcessStartInfo(p.UpdaterExe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(p.UpdaterExe)!
            };
            foreach (var a in p.Arguments.ToArguments()) psi.ArgumentList.Add(a);
            Process.Start(psi);
            UpdateLog.Write($"atualizador iniciado para {p.Arguments.Version}");
        }
        catch (Exception ex)
        {
            UpdateLog.Write("não foi possível iniciar o atualizador: " + ex);
            MessageBox.Show("Não foi possível iniciar a instalação da atualização:\n\n" + ex.Message, "SqlExpert - Atualização",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { UpdateLog.Write("abrir navegador: " + ex.Message); }
    }

    private static void Info(Window? owner, string text)
    {
        if (owner is not null && owner.IsVisible) MessageBox.Show(owner, text, "SqlExpert - Atualização", MessageBoxButton.OK, MessageBoxImage.Information);
        else MessageBox.Show(text, "SqlExpert - Atualização", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static bool Ask(Window? owner, string text, MessageBoxImage image)
    {
        var r = owner is not null && owner.IsVisible
            ? MessageBox.Show(owner, text, "SqlExpert - Atualização", MessageBoxButton.YesNo, image)
            : MessageBox.Show(text, "SqlExpert - Atualização", MessageBoxButton.YesNo, image);
        return r == MessageBoxResult.Yes;
    }
}
