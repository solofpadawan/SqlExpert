using System.IO;

namespace SqlExpert.App.Services;

/// <summary>
/// Temporary diagnostic log for Windows-bar tab drag-and-drop
/// (%TEMP%\sqlexpert-tabdrag.log). Written only during a tab drag.
/// </summary>
internal static class TabDragLog
{
    public static string FilePath { get; } = Path.Combine(Path.GetTempPath(), "sqlexpert-tabdrag.log");

    public static void Write(string message)
    {
        try
        {
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine;
            File.AppendAllText(FilePath, line);
        }
        catch
        {
            // Logging must never break the app.
        }
    }
}
