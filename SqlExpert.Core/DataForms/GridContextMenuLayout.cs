namespace SqlExpert.Core.DataForms;

/// <summary>One row of the Data grid context menu (null <see cref="Caption"/> = separator).</summary>
/// <param name="Id">Stable id used by the view to wire the action.</param>
/// <param name="Caption">Menu text (classic-style captions, like the main menu).</param>
/// <param name="Shortcut">Input gesture text shown on the right ("" = none).</param>
/// <param name="IconKey">Icons.xaml key without the "Icon." prefix ("" = no icon).</param>
/// <param name="IsImplemented">False: the item is shown disabled ("ainda não implementado").</param>
public sealed record GridMenuEntry(string Id, string? Caption, string Shortcut = "", string IconKey = "", bool IsImplemented = false)
{
    public bool IsSeparator => Caption is null;
}

/// <summary>
/// Items of the right-click menu of the Data grid (table editor Data tab and SQL Editor results), in display order.
/// Items not implemented yet stay visible but disabled.
/// </summary>
public static class GridContextMenuLayout
{
    public const string CutCell = "CutCell";
    public const string CopyCell = "CopyCell";
    public const string PasteCell = "PasteCell";
    public const string IncrementalSearch = "IncrementalSearch";
    public const string AdjustWidths = "AdjustWidths";
    public const string SetNull = "SetNull";
    public const string SetEmpty = "SetEmpty";
    public const string SetNow = "SetNow";
    public const string CopyAll = "CopyAll";
    public const string CopySelected = "CopySelected";
    public const string CopyInsert = "CopyInsert";
    public const string CopyUpdate = "CopyUpdate";
    public const string Duplicate = "Duplicate";
    public const string ResetOrder = "ResetOrder";
    public const string ReorderColumns = "ReorderColumns";
    public const string GroupFields = "GroupFields";
    public const string ApplyFilter = "ApplyFilter";
    public const string FilterPanel = "FilterPanel";
    public const string QuickFilter = "QuickFilter";

    private static GridMenuEntry Sep(int n) => new("sep" + n, null);

    public static IReadOnlyList<GridMenuEntry> Entries { get; } = new[]
    {
        new GridMenuEntry(CutCell, "Cut cell value", "", "Cut", true),
        new GridMenuEntry(CopyCell, "Copy cell value", "", "Copy", true),
        new GridMenuEntry(PasteCell, "Paste cell value", "", "Paste", true),
        Sep(1),
        new GridMenuEntry(IncrementalSearch, "Incremental Search", "Ctrl+F", "Find"),
        Sep(2),
        new GridMenuEntry(AdjustWidths, "Adjust column widths", "Ctrl+NUMMULT", "AutoWidth", true),
        Sep(3),
        new GridMenuEntry(SetNull, "Set NULL", "Ctrl+0", "SetNull", true),
        new GridMenuEntry(SetEmpty, "Set empty string", "", "SetEmptyString", true),
        new GridMenuEntry(SetNow, "Set as NOW", "", "SetNow", true),
        Sep(4),
        new GridMenuEntry(CopyAll, "Copy records to clipboard", "", "CopyRecords", true),
        new GridMenuEntry(CopySelected, "Copy selected record(s) to clipboard", "", "Copy", true),
        new GridMenuEntry(CopyInsert, "Copy selected record(s) as INSERT", "", "CopyAsInsert", true),
        new GridMenuEntry(CopyUpdate, "Copy selected record(s) as UPDATE", "", "CopyAsUpdate", true),
        Sep(5),
        new GridMenuEntry(Duplicate, "Duplicate record", "", "DuplicateRecord"),
        Sep(6),
        new GridMenuEntry(ResetOrder, "Reset fields order", "", "Restore", true),
        new GridMenuEntry(ReorderColumns, "Reorder grid columns", "", "Reorder"),
        Sep(7),
        new GridMenuEntry(GroupFields, "Group fields", "", "GroupFields"),
        Sep(8),
        new GridMenuEntry(ApplyFilter, "Apply Filter", "", "Filter"),
        new GridMenuEntry(FilterPanel, "Show Filter Panel", "Ctrl+Alt+F", "FilterPanel"),
        new GridMenuEntry(QuickFilter, "Quick Add Filter Criteria", "", "FilterAdd"),
    };
}
