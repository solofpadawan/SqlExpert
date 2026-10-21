namespace SqlExpert.Core.Services;

/// <summary>Common SQL Server data types offered by the Field editor and the rules for their modifiers.</summary>
public static class SqlTypeCatalog
{
    public static IReadOnlyList<string> CommonTypes { get; } = new[]
    {
        "int", "bigint", "smallint", "tinyint", "bit",
        "decimal", "numeric", "money", "smallmoney", "float", "real",
        "date", "time", "datetime", "datetime2", "smalldatetime", "datetimeoffset",
        "char", "varchar", "nchar", "nvarchar", "text", "ntext",
        "binary", "varbinary", "image",
        "uniqueidentifier", "xml", "sql_variant", "rowversion"
    };

    private static string N(string t) => (t ?? "").Trim().ToLowerInvariant();

    /// <summary>char/varchar/nchar/nvarchar/binary/varbinary take a length.</summary>
    public static bool HasLength(string type) => N(type) is "char" or "varchar" or "nchar" or "nvarchar" or "binary" or "varbinary";

    /// <summary>varchar/nvarchar/varbinary accept (max).</summary>
    public static bool SupportsMax(string type) => N(type) is "varchar" or "nvarchar" or "varbinary";

    /// <summary>Maximum explicit length (8000 bytes, 4000 for Unicode).</summary>
    public static int MaxLength(string type) => N(type) is "nchar" or "nvarchar" ? 4000 : 8000;

    public static bool HasPrecisionAndScale(string type) => N(type) is "decimal" or "numeric";

    /// <summary>time/datetime2/datetimeoffset take a fractional-seconds scale (0..7).</summary>
    public static bool HasFractionalScale(string type) => N(type) is "time" or "datetime2" or "datetimeoffset";

    /// <summary>Character types where COLLATE applies.</summary>
    public static bool IsCharacterType(string type) => N(type) is "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext";

    /// <summary>Types allowed for IDENTITY.</summary>
    public static bool SupportsIdentity(string type, int? scale) =>
        N(type) is "int" or "bigint" or "smallint" or "tinyint"
        || (HasPrecisionAndScale(type) && (scale ?? 0) == 0);

    public static bool IsRowVersion(string type) => N(type) is "rowversion" or "timestamp";

    public static bool IsKnownSystemType(string type) => CommonTypes.Contains(N(type)) || N(type) == "timestamp";

    /// <summary>Suggested length when switching to a length type.</summary>
    public static int DefaultLength(string type) => N(type) is "char" or "nchar" or "binary" ? 10 : 50;
}
