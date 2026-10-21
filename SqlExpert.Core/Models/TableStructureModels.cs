namespace SqlExpert.Core.Models;

/// <summary>
/// Full physical description of one table, used by the Fields tab actions
/// (edit/drop checks) and by the reorder (table rebuild) script generator.
/// </summary>
public sealed class TableStructure
{
    public string Schema { get; set; } = "dbo";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public long RowCount { get; set; }
    /// <summary>IDENT_CURRENT of the table (null when it has no identity column).</summary>
    public decimal? IdentityCurrent { get; set; }

    public List<ColumnStructure> Columns { get; set; } = new();
    public List<KeyConstraintStructure> KeyConstraints { get; set; } = new();
    public List<CheckConstraintStructure> CheckConstraints { get; set; } = new();
    /// <summary>FKs declared on this table (outgoing, includes self references) and on other tables that reference it.</summary>
    public List<ForeignKeyStructure> ForeignKeys { get; set; } = new();
    /// <summary>Indexes that are not PK/UNIQUE constraints.</summary>
    public List<IndexStructure> Indexes { get; set; } = new();
    public List<TriggerStructure> Triggers { get; set; } = new();
    /// <summary>Names starting with "PK_" already used by objects of the schema (to pick a free PK name).</summary>
    public List<string> SchemaPrimaryKeyNames { get; set; } = new();

    public ColumnStructure? FindColumn(string name) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class ColumnStructure
{
    public int Ordinal { get; set; }
    public string Name { get; set; } = "";
    /// <summary>sys.types name (system type name, or alias/UDT name when <see cref="IsUserDefinedType"/>).</summary>
    public string TypeName { get; set; } = "";
    public string TypeSchema { get; set; } = "sys";
    public bool IsUserDefinedType { get; set; }
    /// <summary>Length in characters/bytes as written in T-SQL (nvarchar(50) → 50); -1 = max; null when not applicable.</summary>
    public int? Length { get; set; }
    public int Precision { get; set; }
    public int Scale { get; set; }
    public bool IsNullable { get; set; }
    public string? Collation { get; set; }

    public bool IsIdentity { get; set; }
    public decimal? IdentitySeed { get; set; }
    public decimal? IdentityIncrement { get; set; }

    public bool IsComputed { get; set; }
    public string? ComputedDefinition { get; set; }
    public bool IsPersisted { get; set; }

    public string? DefaultName { get; set; }
    public string? DefaultDefinition { get; set; }
    public bool DefaultIsSystemNamed { get; set; }

    public string? Description { get; set; }

    public bool IsRowVersion =>
        !IsUserDefinedType && (TypeName.Equals("timestamp", StringComparison.OrdinalIgnoreCase)
                               || TypeName.Equals("rowversion", StringComparison.OrdinalIgnoreCase));
}

public sealed record IndexColumnRef(string Name, bool IsDescending = false);

public sealed class KeyConstraintStructure
{
    public string Name { get; set; } = "";
    public bool IsPrimaryKey { get; set; }
    public bool IsClustered { get; set; }
    public List<IndexColumnRef> Columns { get; set; } = new();
}

public sealed class CheckConstraintStructure
{
    public string Name { get; set; } = "";
    public string Definition { get; set; } = "";
    /// <summary>Column of a column-level CHECK (null for table-level checks).</summary>
    public string? ColumnName { get; set; }
    public bool IsNotTrusted { get; set; }
    public bool IsDisabled { get; set; }
}

public sealed class ForeignKeyStructure
{
    public string Name { get; set; } = "";
    public string ParentSchema { get; set; } = "";
    public string ParentTable { get; set; } = "";
    public List<string> Columns { get; set; } = new();
    public string ReferencedSchema { get; set; } = "";
    public string ReferencedTable { get; set; } = "";
    public List<string> ReferencedColumns { get; set; } = new();
    /// <summary>sys.foreign_keys *_referential_action_desc (NO_ACTION, CASCADE, SET_NULL, SET_DEFAULT).</summary>
    public string DeleteAction { get; set; } = "NO_ACTION";
    public string UpdateAction { get; set; } = "NO_ACTION";
    public bool IsNotTrusted { get; set; }
    public bool IsDisabled { get; set; }
    /// <summary>True when the FK is declared on the table being inspected (false = another table references it).</summary>
    public bool IsOutgoing { get; set; }
}

public sealed class IndexStructure
{
    public string Name { get; set; } = "";
    public bool IsUnique { get; set; }
    /// <summary>sys.indexes.type: 1 = clustered, 2 = nonclustered, others (xml, spatial, columnstore…) are not scripted.</summary>
    public int Type { get; set; }
    public string TypeDesc { get; set; } = "";
    public string? FilterDefinition { get; set; }
    public List<IndexColumnRef> KeyColumns { get; set; } = new();
    public List<string> IncludedColumns { get; set; } = new();
    public bool IsClustered => Type == 1;
}

public sealed class TriggerStructure
{
    public string Name { get; set; } = "";
    public string? Definition { get; set; }
    public bool IsDisabled { get; set; }
}
