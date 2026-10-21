namespace SqlExpert.Core.DataForms;

/// <summary>
/// Classic data-grid rule for new records: ↓ on the last row appends an empty record (row header "*", every field
/// NULL). Left untouched it is discarded as soon as another row becomes current; with any value it stays as a
/// pending INSERT until Post / Cancel. Identity, computed and rowversion columns are never sent, and NULL fields are
/// left out of the INSERT so column DEFAULTs apply (TableDataSql.BuildInsert).
/// </summary>
public static class NewRecordRules
{
    /// <summary>Row header text of a record that is not in the database yet.</summary>
    public const string NewRowMarker = "*";

    /// <summary>True when every value of the record is NULL (nothing was typed).</summary>
    public static bool IsBlank(IEnumerable<object?> values) => values.All(DataValueText.IsNull);

    /// <summary>
    /// ↓ appends a record when the grid is editable and the current row is the last one (or the grid is empty),
    /// unless that last row already is an untouched new record.
    /// </summary>
    public static bool ShouldAppendOnDown(int currentIndex, int rowCount, bool canModify, bool lastRowIsBlankNew)
    {
        if (!canModify || rowCount < 0) return false;
        if (rowCount == 0) return true;
        return currentIndex == rowCount - 1 && !lastRowIsBlankNew;
    }

    /// <summary>A blank appended record is dropped once it is no longer the current row (or on Post).</summary>
    public static bool ShouldDiscard(bool isAppended, bool isStillNew, bool isBlank, bool isCurrent) =>
        isAppended && isStillNew && isBlank && !isCurrent;
}
