namespace SqlExpert.Core.Updates;

/// <summary>Outcome of the updater mode: what to tell the user and which exe to start afterwards (null = none).</summary>
public sealed record UpdaterOutcome(bool Success, string Message, string? RelaunchPath);

/// <summary>
/// Steps of "SqlExpert.exe --apply-update": wait until the old process has exited, check the folders, replace the
/// files (with backup and rollback) and pick the exe to start again — the new one, or the old one after a rollback.
/// </summary>
public static class UpdaterRunner
{
    public static readonly TimeSpan ExitWait = TimeSpan.FromMinutes(2);

    /// <param name="waitForExit">Returns true once the process id has exited (or does not exist), false on timeout.</param>
    public static UpdaterOutcome Run(UpdaterCommandLine cmd, Func<int, TimeSpan, bool> waitForExit, UpdateApplier applier, Action<string>? status = null)
    {
        status ??= _ => { };
        void Log(string s) { UpdateLog.Write("[updater] " + s); status(s); }

        var targetExe = Path.Combine(cmd.TargetDir, UpdatePackage.ExecutableName);
        Log($"aguardando o SqlExpert (pid {cmd.ProcessId}) fechar...");
        if (!waitForExit(cmd.ProcessId, ExitWait))
            return new UpdaterOutcome(false, "O SqlExpert anterior não fechou a tempo. Nada foi alterado; feche-o e tente atualizar de novo.", null);

        if (!File.Exists(Path.Combine(cmd.SourceDir, UpdatePackage.ExecutableName)))
            return new UpdaterOutcome(false, $"Pacote extraído não encontrado em {cmd.SourceDir}. Nada foi alterado.", File.Exists(targetExe) ? targetExe : null);
        if (!Directory.Exists(cmd.TargetDir))
            return new UpdaterOutcome(false, $"Pasta do programa não encontrada: {cmd.TargetDir}.", null);

        Log($"copiando arquivos para {cmd.TargetDir}...");
        var r = applier.Apply(cmd.SourceDir, cmd.TargetDir, cmd.BackupDir);
        if (r.Success)
        {
            Log($"versão {cmd.Version} instalada ({r.FilesCopied} arquivo(s)).");
            return new UpdaterOutcome(true, $"SqlExpert {cmd.Version} instalado.", targetExe);
        }
        if (r.RolledBack)
            return new UpdaterOutcome(false,
                $"A atualização para {cmd.Version} falhou e a versão anterior foi mantida.\n\n{r.Error}",
                File.Exists(targetExe) ? targetExe : null);
        return new UpdaterOutcome(false,
            $"A atualização para {cmd.Version} falhou e não foi possível restaurar todos os arquivos.\n\n{r.Error}\n\n" +
            $"Cópia dos arquivos anteriores: {cmd.BackupDir}\nCopie-os de volta para {cmd.TargetDir} ou baixe o .zip da versão novamente.",
            null);
    }
}
