namespace SqlExpert.Core.Updates;

/// <summary>File operations used by <see cref="UpdateApplier"/> (replaceable in tests to simulate locked files).</summary>
public interface IUpdateFileSystem
{
    void Copy(string source, string destination);
    void Delete(string path);
}

public sealed class PhysicalUpdateFileSystem : IUpdateFileSystem
{
    public static PhysicalUpdateFileSystem Instance { get; } = new();

    public void Copy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    public void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

/// <summary>Outcome of <see cref="UpdateApplier.Apply"/>.</summary>
public sealed record UpdateApplyResult(bool Success, bool RolledBack, int FilesCopied, string? Error);

/// <summary>
/// Replaces the program files: every file of the new version is copied over the program folder; files that already
/// exist are first saved to a backup folder. Locked files are retried for a while (the old process may still be
/// exiting). On any failure the files written so far are undone (backups restored, new files deleted), so the
/// folder ends as it was. Files of the program folder that the new version does not ship are left untouched.
/// </summary>
public sealed class UpdateApplier
{
    private readonly IUpdateFileSystem _fs;
    private readonly Action<string> _log;
    private readonly int _attempts;
    private readonly TimeSpan _retryDelay;
    private readonly Action<TimeSpan> _sleep;

    public UpdateApplier(IUpdateFileSystem? fileSystem = null, Action<string>? log = null,
        int attempts = 40, TimeSpan? retryDelay = null, Action<TimeSpan>? sleep = null)
    {
        _fs = fileSystem ?? PhysicalUpdateFileSystem.Instance;
        _log = log ?? (_ => { });
        _attempts = Math.Max(1, attempts);
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(500);
        _sleep = sleep ?? Thread.Sleep;
    }

    public UpdateApplyResult Apply(string sourceDir, string targetDir, string backupDir)
    {
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(sourceDir, f))
            // SqlExpert.exe last: a failure on any other file never leaves a new exe with old companions.
            .OrderBy(r => string.Equals(r, UpdatePackage.ExecutableName, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0) return new UpdateApplyResult(false, false, 0, "O pacote está vazio.");

        var replaced = new List<string>();
        var added = new List<string>();
        var copied = 0;
        try
        {
            Directory.CreateDirectory(backupDir);
            foreach (var rel in files)
            {
                var target = Path.Combine(targetDir, rel);
                if (File.Exists(target))
                {
                    Retry(() => _fs.Copy(target, Path.Combine(backupDir, rel)), "backup " + rel);
                    replaced.Add(rel);
                }
                else
                {
                    added.Add(rel);
                }
                Retry(() => _fs.Copy(Path.Combine(sourceDir, rel), target), "copiar " + rel);
                copied++;
                _log($"ok: {rel}");
            }
            return new UpdateApplyResult(true, false, copied, null);
        }
        catch (Exception ex)
        {
            _log("falhou: " + ex.Message + " — desfazendo");
            var rolledBack = Rollback(targetDir, backupDir, replaced, added);
            return new UpdateApplyResult(false, rolledBack, copied, ex.Message);
        }
    }

    private bool Rollback(string targetDir, string backupDir, List<string> replaced, List<string> added)
    {
        var ok = true;
        foreach (var rel in added)
        {
            try { Retry(() => _fs.Delete(Path.Combine(targetDir, rel)), "remover " + rel); }
            catch (Exception ex) { ok = false; _log("rollback: " + ex.Message); }
        }
        for (var i = replaced.Count - 1; i >= 0; i--)
        {
            var rel = replaced[i];
            try { Retry(() => _fs.Copy(Path.Combine(backupDir, rel), Path.Combine(targetDir, rel)), "restaurar " + rel); }
            catch (Exception ex) { ok = false; _log("rollback: " + ex.Message); }
        }
        _log(ok ? "rollback concluído" : "rollback incompleto");
        return ok;
    }

    private void Retry(Action action, string what)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < _attempts)
            {
                if (attempt == 1) _log($"aguardando ({what}): {ex.Message}");
                _sleep(_retryDelay);
            }
        }
    }
}

/// <summary>
/// Arguments of the updater mode of SqlExpert.exe. The running program copies its own exe to the staging area and
/// starts that copy with these arguments, so the code that replaces the files is the same version that downloaded them.
/// </summary>
public sealed record UpdaterCommandLine(int ProcessId, string SourceDir, string TargetDir, string BackupDir, string Version)
{
    public const string Switch = "--apply-update";

    public IReadOnlyList<string> ToArguments() => new[]
    {
        Switch,
        "--pid", ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--source", SourceDir,
        "--target", TargetDir,
        "--backup", BackupDir,
        "--version", Version
    };

    public static bool IsUpdaterInvocation(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], Switch, StringComparison.OrdinalIgnoreCase);

    public static UpdaterCommandLine? TryParse(IReadOnlyList<string> args)
    {
        if (!IsUpdaterInvocation(args)) return null;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i + 1 < args.Count; i += 2)
            map[args[i]] = args[i + 1];
        if (!map.TryGetValue("--pid", out var pidText) || !int.TryParse(pidText, out var pid) || pid <= 0) return null;
        if (!map.TryGetValue("--source", out var src) || string.IsNullOrWhiteSpace(src)) return null;
        if (!map.TryGetValue("--target", out var dst) || string.IsNullOrWhiteSpace(dst)) return null;
        if (!map.TryGetValue("--backup", out var bak) || string.IsNullOrWhiteSpace(bak)) return null;
        map.TryGetValue("--version", out var ver);
        return new UpdaterCommandLine(pid, src, dst, bak, ver ?? "");
    }
}

/// <summary>Paths used while updating: %LocalAppData%\SqlExpert\updates\&lt;version&gt;\{download,staging,updater,backup}.</summary>
public sealed record UpdateWorkspace(string Root)
{
    public static string DefaultBase()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local)) local = Path.GetTempPath();
        return Path.Combine(local, "SqlExpert", "updates");
    }

    public static UpdateWorkspace For(string version, string? baseDir = null) =>
        new(Path.Combine(baseDir ?? DefaultBase(), SafeName(version)));

    public string DownloadDir => Path.Combine(Root, "download");
    public string StagingDir => Path.Combine(Root, "staging");
    public string UpdaterDir => Path.Combine(Root, "updater");
    public string BackupDir => Path.Combine(Root, "backup");

    private static string SafeName(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "unknown" : clean;
    }

    /// <summary>Removes leftovers of earlier updates (best effort; folders still in use are skipped).</summary>
    public static int CleanupOld(string? baseDir = null, string? keepVersion = null)
    {
        var dir = baseDir ?? DefaultBase();
        var removed = 0;
        try
        {
            if (!Directory.Exists(dir)) return 0;
            foreach (var d in Directory.GetDirectories(dir))
            {
                if (keepVersion is not null && string.Equals(Path.GetFileName(d), SafeName(keepVersion), StringComparison.OrdinalIgnoreCase)) continue;
                try { Directory.Delete(d, recursive: true); removed++; } catch { }
            }
        }
        catch { }
        return removed;
    }
}
