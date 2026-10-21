namespace SqlExpert.Core.Models;

/// <summary>Per-user UI preferences persisted in %AppData%\SqlExpert\settings.json.</summary>
public sealed class UserSettings
{
    public const int MinDataGridFontSize = 6;
    public const int MaxDataGridFontSize = 32;
    public const int DefaultDataGridFontSize = 9;

    /// <summary>Data tab grid font size in points (classic-style "Font size").</summary>
    public int DataGridFontSize { get; set; } = DefaultDataGridFontSize;

    /// <summary>Last main-window bounds/state (null = never saved → first-run default).</summary>
    public WindowPlacement? MainWindow { get; set; }

    public const int DefaultSqlEditorFontSize = 10;
    public const int DefaultSqlFetchRows = 1000;
    public const int MaxSqlFetchRows = 1_000_000;

    /// <summary>SQL Editor text font size in points (6–32).</summary>
    public int SqlEditorFontSize { get; set; } = DefaultSqlEditorFontSize;

    /// <summary>SQL Editor: rows kept per result set ("first N"; Fetch all ignores it).</summary>
    public int SqlFetchRows { get; set; } = DefaultSqlFetchRows;

    /// <summary>DB Explorer "Sort nodes": true = by alias, false = as registered.</summary>
    public bool ExplorerSortByAlias { get; set; }

    /// <summary>DB Explorer "Hide Disconnected Databases".</summary>
    public bool HideDisconnectedDatabases { get; set; }

    /// <summary>DB Explorer "Show objects description" (MS_Description next to object names).</summary>
    public bool ShowObjectsDescription { get; set; }

    public static int ClampFetchRows(int value) => Math.Clamp(value, 1, MaxSqlFetchRows);

    public static int ClampFontSize(int value) => Math.Clamp(value, MinDataGridFontSize, MaxDataGridFontSize);
}
