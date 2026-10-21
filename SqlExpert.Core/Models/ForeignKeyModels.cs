namespace SqlExpert.Core.Models;

/// <summary>One column pair of a foreign key: local (referencing) column → referenced column.</summary>
public sealed record ForeignKeyColumnPair(string Column, string ReferencedColumn);

/// <summary>Raw row of <see cref="Services.ForeignKeySql.TableForeignKeys"/> (one per FK column).</summary>
public sealed record ForeignKeyColumnRow(
    string Name, string ReferencedSchema, string ReferencedTable, int ColumnId, string Column, string ReferencedColumn, bool IsDisabled);

/// <summary>Foreign key of the table shown in the Data tab (sys.foreign_keys + sys.foreign_key_columns).</summary>
public sealed class ForeignKeyInfo
{
    public string Name { get; init; } = "";
    public string ReferencedSchema { get; init; } = "dbo";
    public string ReferencedTable { get; init; } = "";
    /// <summary>Column pairs in constraint_column_id order.</summary>
    public IReadOnlyList<ForeignKeyColumnPair> Columns { get; init; } = Array.Empty<ForeignKeyColumnPair>();
    public bool IsDisabled { get; init; }

    public bool IsMultiColumn => Columns.Count > 1;

    /// <summary>"schema.table" (popup title: "Data of [schema.table]").</summary>
    public string ReferencedTitle => $"{ReferencedSchema}.{ReferencedTable}";

    public IReadOnlyList<string> LocalColumns => Columns.Select(c => c.Column).ToList();
    public IReadOnlyList<string> ReferencedColumns => Columns.Select(c => c.ReferencedColumn).ToList();
}

/// <summary>Local column as the lookup sees it (grid column of the edited table).</summary>
public sealed record LookupLocalColumn(string Name, bool IsEditable, bool IsNullable);

/// <summary>What the dropdown of one grid column does: which FK, which local columns it sets, whether it is usable.</summary>
public sealed class ForeignKeyLookupTarget
{
    public required ForeignKeyInfo ForeignKey { get; init; }
    /// <summary>Local columns set together by a pick, in FK column order.</summary>
    public IReadOnlyList<string> LocalColumns { get; init; } = Array.Empty<string>();
    public bool IsEnabled { get; init; }
    /// <summary>Why the lookup is off (column missing from the grid / read-only), null when enabled.</summary>
    public string? DisabledReason { get; init; }
    /// <summary>True when every local column accepts NULL: the popup offers "(null)".</summary>
    public bool AllowsNull { get; init; }

    public string ToolTip => IsEnabled
        ? $"Chave estrangeira {ForeignKey.Name} → {ForeignKey.ReferencedTitle} ({string.Join(", ", ForeignKey.ReferencedColumns)}). Alt+↓ ou F4 abre a lista."
        : $"Chave estrangeira {ForeignKey.Name} → {ForeignKey.ReferencedTitle}: lookup indisponível ({DisabledReason}).";
}

/// <summary>Rows of the referenced table for the lookup popup. Columns: key columns first (<see cref="KeyCount"/>), then display columns.</summary>
public sealed class LookupData
{
    public required ForeignKeyInfo ForeignKey { get; init; }
    public IReadOnlyList<DataColumnInfo> Columns { get; init; } = Array.Empty<DataColumnInfo>();
    public int KeyCount { get; init; }
    public IReadOnlyList<object[]> Rows { get; init; } = Array.Empty<object[]>();
    /// <summary>Rows requested (TOP n); 0 = all rows.</summary>
    public int Top { get; init; }
    /// <summary>False when TOP n cut the list (more rows may exist).</summary>
    public bool IsComplete { get; init; }
    /// <summary>The current value's row was outside TOP n and was read separately (appended at the end).</summary>
    public bool CurrentRowAppended { get; init; }
}
