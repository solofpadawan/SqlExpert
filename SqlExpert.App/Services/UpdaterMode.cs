using System.Diagnostics;
using System.IO;
using System.Windows;
using SqlExpert.App.Views;
using SqlExpert.Core.Updates;

namespace SqlExpert.App.Services;

/// <summary>
/// "SqlExpert.exe --apply-update ...": started by the previous instance (from a copy of its exe outside the program
/// folder). Waits for it to exit, replaces the program files and starts SqlExpert again.
/// </summary>
public static class UpdaterMode
{
    public static void Run(Application app, UpdaterCommandLine cmd)
    {
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var win = new UpdaterProgressWindow(cmd.Version);
        win.Show();
        Task.Run(() => UpdaterRunner.Run(cmd, WaitForExit, new UpdateApplier(log: s => UpdateLog.Write("[updater] " + s)), win.SetStatus))
            .ContinueWith(t => app.Dispatcher.Invoke(() =>
            {
                var outcome = t.IsFaulted
                    ? new UpdaterOutcome(false, "Erro inesperado na atualização: " + t.Exception?.GetBaseException().Message, null)
                    : t.Result;
                if (t.IsFaulted) UpdateLog.Write("[updater] " + t.Exception);
                win.Close();
                if (!outcome.Success)
                    MessageBox.Show(outcome.Message + $"\n\nDetalhes: {UpdateLog.FilePath}", "SqlExpert - Atualização",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                if (outcome.RelaunchPath is { } exe)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
                    }
                    catch (Exception ex)
                    {
                        UpdateLog.Write("[updater] não foi possível abrir o SqlExpert: " + ex.Message);
                    }
                }
                app.Shutdown(outcome.Success ? 0 : 1);
            }));
    }

    private static bool WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.WaitForExit(timeout);
        }
        catch (ArgumentException)
        {
            return true; // already gone
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
