namespace SqlExpert.Core.Models;

public enum ObjectKind
{
    Database,
    Schema,
    UserDefinedType,   // Domain → UDT / CREATE TYPE
    Table,
    View,
    Procedure,
    Function,          // UDF → scalar/table-valued function
    Trigger,
    Sequence,          // Generator → SEQUENCE
    Role,
    Index,
    Constraint,
    Synonym,
    CustomMessage      // Exception → sp_addmessage / THROW
}

/// <param name="Description">MS_Description (null when none or not loaded).</param>
public sealed record ObjectRef(string Database, string Schema, string Name, ObjectKind Kind, string? Description = null);

public sealed class SchemaNode
{
    public string Name { get; set; } = "";
    public ObjectKind Kind { get; set; }
    public int? Count { get; set; }
    public List<SchemaNode> Children { get; set; } = new();
    public ObjectRef? Object { get; set; }
}

public sealed class TableMetadata
{
    public ObjectRef Ref { get; set; } = null!;
    public IReadOnlyList<ColumnInfo> Columns { get; set; } = Array.Empty<ColumnInfo>();
    public IReadOnlyList<ConstraintInfo> Constraints { get; set; } = Array.Empty<ConstraintInfo>();
    public IReadOnlyList<IndexInfo> Indices { get; set; } = Array.Empty<IndexInfo>();
    public string? Description { get; set; }
}

public sealed class ColumnInfo
{
    public int Ordinal { get; set; }
    public string Name { get; set; } = "";
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
    public bool IsUnique { get; set; }
    public string DataType { get; set; } = "";
    public int? Size { get; set; }
    public int? Scale { get; set; }
    /// <summary>Numeric precision (sys.columns.precision); meaningful for decimal/numeric.</summary>
    public int? Precision { get; set; }
    public bool IsNullable { get; set; }
    public string? DefaultValue { get; set; }
    public string? Collation { get; set; }
    public string? Description { get; set; }
}

public sealed class ConstraintInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Definition { get; set; } = "";
}

public sealed class IndexInfo
{
    public string Name { get; set; } = "";
    public bool IsUnique { get; set; }
    public bool IsClustered { get; set; }
    public string Columns { get; set; } = "";
}
