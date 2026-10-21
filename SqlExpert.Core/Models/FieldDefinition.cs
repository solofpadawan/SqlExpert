namespace SqlExpert.Core.Models;

/// <summary>Column definition edited by the Field editor (New field / Edit field).</summary>
public sealed class FieldDefinition
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "int";
    /// <summary>Schema of a user-defined/alias type (null for system types).</summary>
    public string? TypeSchema { get; set; }
    /// <summary>Length for char/binary types; -1 = max.</summary>
    public int? Length { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public bool IsNullable { get; set; } = true;
    /// <summary>T-SQL default expression, e.g. <c>0</c>, <c>'ABERTO'</c>, <c>sysdatetime()</c>. Empty = no default.</summary>
    public string? DefaultExpression { get; set; }
    public string? Collation { get; set; }
    public bool IsIdentity { get; set; }
    public decimal IdentitySeed { get; set; } = 1;
    public decimal IdentityIncrement { get; set; } = 1;
    public string? Description { get; set; }

    public FieldDefinition Clone() => (FieldDefinition)MemberwiseClone();

    /// <summary>Builds the editable definition of an existing column.</summary>
    public static FieldDefinition FromColumn(ColumnStructure c) => new()
    {
        Name = c.Name,
        DataType = c.TypeName,
        TypeSchema = c.IsUserDefinedType ? c.TypeSchema : null,
        Length = c.IsUserDefinedType ? null : c.Length,
        Precision = c.Precision,
        Scale = c.Scale,
        IsNullable = c.IsNullable,
        DefaultExpression = c.DefaultDefinition,
        Collation = c.Collation,
        IsIdentity = c.IsIdentity,
        IdentitySeed = c.IdentitySeed ?? 1,
        IdentityIncrement = c.IdentityIncrement ?? 1,
        Description = c.Description
    };
}
