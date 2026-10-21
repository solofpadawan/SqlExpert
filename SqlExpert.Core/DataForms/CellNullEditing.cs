using System.Data;

namespace SqlExpert.Core.DataForms;

/// <summary>
/// Explicit "set NULL" of a Data grid / Form View cell (shortcut <see cref="SetNullShortcut"/>; Ctrl+Del already
/// deletes the record). The editor itself never turns a blank text into NULL on a non-null value of a text column,
/// so this is the way to clear such a value to NULL.
/// </summary>
public static class CellNullEditing
{
    /// <summary>Shortcut shown in tool tips and status messages.</summary>
    public const string SetNullShortcut = "Ctrl+0";

    /// <summary>
    /// True for Ctrl+0 (top-row 0 or numeric keypad 0) without Shift/Alt. <paramref name="keyName"/> is the WPF
    /// <c>Key</c> name ("D0", "NumPad0").
    /// </summary>
    public static bool IsSetNullGesture(string keyName, bool control, bool shift, bool alt) =>
        control && !shift && !alt && (keyName == "D0" || keyName == "NumPad0");

    public enum Result
    {
        /// <summary>The cell was set to NULL (pending until Post/Cancel).</summary>
        SetToNull,
        /// <summary>The cell was already NULL: nothing changed.</summary>
        AlreadyNull,
        /// <summary>NOT NULL column: nothing changed.</summary>
        NotNullable,
        /// <summary>Read-only column or table: nothing changed.</summary>
        ReadOnly
    }

    /// <summary>
    /// Sets <paramref name="column"/> of <paramref name="row"/> to NULL when allowed. Only writes when the value is
    /// not NULL yet, so an already-NULL cell never marks the row as modified.
    /// </summary>
    public static Result TrySetNull(DataRow row, string column, bool isNullable, bool isReadOnly)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(column);
        if (isReadOnly) return Result.ReadOnly;
        if (!isNullable) return Result.NotNullable;
        if (DataValueText.IsNull(row[column])) return Result.AlreadyNull;
        row[column] = DBNull.Value;
        return Result.SetToNull;
    }
}
