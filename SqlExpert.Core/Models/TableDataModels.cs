namespace SqlExpert.Core.Models;

/// <summary>Column information needed to read/write table data (Data tab).</summary>
public sealed class DataColumnInfo
{
    public int Ordinal { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Declared type name (TYPE_NAME(user_type_id)), e.g. nvarchar, int, geography.</summary>
    public string TypeName { get; set; } = "";
    /// <summary>Base system type name (for alias types), e.g. nvarchar.</summary>
    public string BaseTypeName { get; set; } = "";
    public bool IsNullable { get; set; }
    public bool IsIdentity { get; set; }
    public bool IsComputed { get; set; }
    public bool IsRowVersion { get; set; }
    /// <summary>CLR user-defined type (geography, geometry, hierarchyid, …): read as text, read-only.</summary>
    public bool IsClrType { get; set; }
    public bool IsPrimaryKey { get; set; }
    /// <summary>1-based position inside the primary key (0 when not part of the PK).</summary>
    public int KeyOrdinal { get; set; }
    /// <summary>sys.columns.max_length in bytes (-1 = MAX, 0 = unknown).</summary>
    public int MaxLength { get; set; }
    public int Precision { get; set; }
    public int Scale { get; set; }

    /// <summary>False for identity, computed, rowversion and CLR UDT columns (never sent in INSERT/UPDATE).</summary>
    public bool IsWritable => !IsIdentity && !IsComputed && !IsRowVersion && !IsClrType;
}

/// <summary>Schema of a table for the data grid.</summary>
public sealed class TableDataSchema
{
    public string Schema { get; set; } = "dbo";
    public string Table { get; set; } = "";
    public IReadOnlyList<DataColumnInfo> Columns { get; set; } = Array.Empty<DataColumnInfo>();

    public IReadOnlyList<DataColumnInfo> KeyColumns =>
        Columns.Where(c => c.IsPrimaryKey).OrderBy(c => c.KeyOrdinal).ToList();

    public bool HasPrimaryKey => Columns.Any(c => c.IsPrimaryKey);

    public DataColumnInfo? IdentityColumn => Columns.FirstOrDefault(c => c.IsIdentity);

    /// <summary>Foreign keys of the table (Data tab lookups). Empty when unknown or none.</summary>
    public IReadOnlyList<ForeignKeyInfo> ForeignKeys { get; set; } = Array.Empty<ForeignKeyInfo>();
}

/// <summary>Rows read with SELECT TOP (n); values are in <see cref="TableDataSchema.Columns"/> order (DBNull for NULL).</summary>
public sealed class TableDataPage
{
    public TableDataSchema Schema { get; set; } = new();
    public IReadOnlyList<Type> FieldTypes { get; set; } = Array.Empty<Type>();
    public IReadOnlyList<object[]> Rows { get; set; } = Array.Empty<object[]>();
    public int Top { get; set; }
}

public enum RowChangeKind
{
    Insert,
    Update,
    Delete
}

/// <summary>One pending grid change to persist.</summary>
public sealed class RowChange
{
    public RowChangeKind Kind { get; init; }
    /// <summary>Insert: all values; Update: changed values only (column name → value, null/DBNull = NULL).</summary>
    public IReadOnlyDictionary<string, object?> Values { get; init; } = new Dictionary<string, object?>();
    /// <summary>Original primary-key values (Update/Delete).</summary>
    public IReadOnlyDictionary<string, object?> Key { get; init; } = new Dictionary<string, object?>();
}

public sealed record SqlParam(string Name, object? Value, DataColumnInfo Column);

/// <summary>Parameterized statement. <see cref="ReturnsRow"/>: the batch ends with a SELECT of the affected row.</summary>
public sealed record SqlStatement(string Sql, IReadOnlyList<SqlParam> Parameters, bool ReturnsRow = false);
