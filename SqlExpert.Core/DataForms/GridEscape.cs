namespace SqlExpert.Core.DataForms;

/// <summary>What Esc does in a data grid / form view.</summary>
public enum GridEscapeAction
{
    /// <summary>Not handled here: the cell editor (or the open popup) handles Esc itself (cancel the cell edit / close).</summary>
    PassThrough,
    /// <summary>Same as the Cancel button: discard the pending changes (an unposted new record is removed).</summary>
    CancelChanges,
    /// <summary>Nothing to discard: Esc is consumed so it does not close the window or the tab.</summary>
    Swallow
}

/// <summary>
/// Esc in the data grids and the form view, classic-style: the first Esc cancels the cell being edited; an Esc with no
/// cell in edit mode discards the pending changes like the Cancel button; with nothing pending it does nothing.
/// </summary>
public static class GridEscape
{
    public static GridEscapeAction Decide(bool cellEditing, bool popupOpen, bool hasPending) =>
        cellEditing || popupOpen ? GridEscapeAction.PassThrough
        : hasPending ? GridEscapeAction.CancelChanges
        : GridEscapeAction.Swallow;

    /// <summary>Plain Esc (no modifier).</summary>
    public static bool IsPlainEscape(bool isEscapeKey, bool hasModifiers) => isEscapeKey && !hasModifiers;
}
