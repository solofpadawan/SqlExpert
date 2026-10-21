using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

/// <summary>
/// SQL fragments used by <see cref="MetadataService"/>. Exposed for unit tests (no live server required).
/// </summary>
public static class MetadataSql
{
    public const string ListDatabases = """
        SELECT name
        FROM sys.databases
        WHERE state = 0
          AND HAS_DBACCESS(name) = 1
        ORDER BY name;
        """;

    /// <summary>master, model, msdb, tempdb (hidden when a registration turns "Show system databases" off).</summary>
    public static readonly IReadOnlySet<string> SystemDatabases =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "master", "model", "msdb", "tempdb" };

    public static bool IsSystemDatabase(string? name) => name is not null && SystemDatabases.Contains(name);

    /// <summary>MS_Description of schema-scoped objects (tables, views, procedures, functions, triggers, sequences).</summary>
    public const string ObjectDescriptions = """
        SELECT SCHEMA_NAME(o.schema_id) AS [Schema], o.name AS [Name], CAST(ep.value AS nvarchar(4000)) AS [Description]
        FROM sys.extended_properties ep
        INNER JOIN sys.objects o ON o.object_id = ep.major_id
        WHERE ep.class = 1 AND ep.minor_id = 0 AND ep.name = N'MS_Description';
        """;

    /// <summary>Kinds whose objects live in sys.objects (descriptions come from <see cref="ObjectDescriptions"/>).</summary>
    public static bool HasObjectDescriptions(ObjectKind kind) => kind is ObjectKind.Table or ObjectKind.View
        or ObjectKind.Procedure or ObjectKind.Function or ObjectKind.Trigger or ObjectKind.Sequence;

    public static string CountByKind(ObjectKind kind) => kind switch
    {
        ObjectKind.UserDefinedType => """
            SELECT COUNT(*) FROM sys.types t
            WHERE t.is_user_defined = 1;
            """,
        ObjectKind.Table => """
            SELECT COUNT(*) FROM sys.tables t
            WHERE t.is_ms_shipped = 0;
            """,
        ObjectKind.View => """
            SELECT COUNT(*) FROM sys.views v
            WHERE v.is_ms_shipped = 0;
            """,
        ObjectKind.Procedure => """
            SELECT COUNT(*) FROM sys.procedures p
            WHERE p.is_ms_shipped = 0;
            """,
        ObjectKind.Function => """
            SELECT COUNT(*) FROM sys.objects o
            WHERE o.is_ms_shipped = 0
              AND o.type IN ('FN','IF','TF','FS','FT');
            """,
        ObjectKind.Trigger => """
            SELECT COUNT(*) FROM sys.triggers tr
            INNER JOIN sys.objects o ON o.object_id = tr.parent_id
            WHERE tr.parent_class = 1 AND o.is_ms_shipped = 0;
            """,
        ObjectKind.Sequence => """
            SELECT COUNT(*) FROM sys.sequences;
            """,
        ObjectKind.Role => """
            SELECT COUNT(*) FROM sys.database_principals
            WHERE type = 'R' AND name NOT IN ('public');
            """,
        _ => "SELECT 0;"
    };

    public static string ListByKind(ObjectKind kind) => kind switch
    {
        ObjectKind.UserDefinedType => """
            SELECT SCHEMA_NAME(t.schema_id) AS [Schema], t.name AS [Name]
            FROM sys.types t
            WHERE t.is_user_defined = 1
            ORDER BY 1, 2;
            """,
        ObjectKind.Table => """
            SELECT SCHEMA_NAME(t.schema_id) AS [Schema], t.name AS [Name]
            FROM sys.tables t
            WHERE t.is_ms_shipped = 0
            ORDER BY 1, 2;
            """,
        ObjectKind.View => """
            SELECT SCHEMA_NAME(v.schema_id) AS [Schema], v.name AS [Name]
            FROM sys.views v
            WHERE v.is_ms_shipped = 0
            ORDER BY 1, 2;
            """,
        ObjectKind.Procedure => """
            SELECT SCHEMA_NAME(p.schema_id) AS [Schema], p.name AS [Name]
            FROM sys.procedures p
            WHERE p.is_ms_shipped = 0
            ORDER BY 1, 2;
            """,
        ObjectKind.Function => """
            SELECT SCHEMA_NAME(o.schema_id) AS [Schema], o.name AS [Name]
            FROM sys.objects o
            WHERE o.is_ms_shipped = 0
              AND o.type IN ('FN','IF','TF','FS','FT')
            ORDER BY 1, 2;
            """,
        ObjectKind.Trigger => """
            SELECT SCHEMA_NAME(o.schema_id) AS [Schema], tr.name AS [Name]
            FROM sys.triggers tr
            INNER JOIN sys.objects o ON o.object_id = tr.parent_id
            WHERE tr.parent_class = 1 AND o.is_ms_shipped = 0
            ORDER BY 1, 2;
            """,
        ObjectKind.Sequence => """
            SELECT SCHEMA_NAME(s.schema_id) AS [Schema], s.name AS [Name]
            FROM sys.sequences s
            ORDER BY 1, 2;
            """,
        ObjectKind.Role => """
            SELECT N'dbo' AS [Schema], p.name AS [Name]
            FROM sys.database_principals p
            WHERE p.type = 'R' AND p.name NOT IN ('public')
            ORDER BY 2;
            """,
        _ => "SELECT CAST(NULL AS sysname) AS [Schema], CAST(NULL AS sysname) AS [Name] WHERE 1=0;"
    };

    public const string TableColumns = """
        SELECT
            c.column_id AS Ordinal,
            c.name AS ColumnName,
            CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
            CASE WHEN fk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsForeignKey,
            CASE WHEN uq.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsUnique,
            ty.name AS DataType,
            CASE
                WHEN ty.name IN (N'nvarchar', N'nchar') AND c.max_length >= 0 THEN c.max_length / 2
                WHEN ty.name IN (N'varchar', N'char', N'varbinary', N'binary') AND c.max_length >= 0 THEN c.max_length
                WHEN c.max_length = -1 THEN -1
                ELSE NULL
            END AS [Size],
            c.scale AS Scale,
            c.is_nullable AS IsNullable,
            dc.definition AS DefaultValue,
            c.collation_name AS Collation,
            CAST(ep.value AS nvarchar(4000)) AS Description,
            CAST(c.precision AS int) AS [Precision]
        FROM sys.columns c
        INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        INNER JOIN sys.tables t ON t.object_id = c.object_id
        INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
        LEFT JOIN sys.extended_properties ep
            ON ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = N'MS_Description' AND ep.class = 1
        LEFT JOIN (
            SELECT ic.object_id, ic.column_id
            FROM sys.index_columns ic
            INNER JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            WHERE i.is_primary_key = 1
        ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
        LEFT JOIN (
            SELECT fkc.parent_object_id AS object_id, fkc.parent_column_id AS column_id
            FROM sys.foreign_key_columns fkc
        ) fk ON fk.object_id = c.object_id AND fk.column_id = c.column_id
        LEFT JOIN (
            SELECT ic.object_id, ic.column_id
            FROM sys.index_columns ic
            INNER JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            WHERE i.is_unique_constraint = 1 OR (i.is_unique = 1 AND i.is_primary_key = 0)
        ) uq ON uq.object_id = c.object_id AND uq.column_id = c.column_id
        WHERE s.name = @schema AND t.name = @table
        ORDER BY c.column_id;
        """;

    public const string TableConstraints = """
        SELECT kc.name AS ConstraintName, kc.type_desc AS ConstraintType,
               (
                   SELECT STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal)
                   FROM sys.index_columns ic
                   INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                   WHERE ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
               ) AS Definition
        FROM sys.key_constraints kc
        INNER JOIN sys.tables t ON t.object_id = kc.parent_object_id
        INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE s.name = @schema AND t.name = @table
        UNION ALL
        SELECT fk.name, N'FOREIGN_KEY_CONSTRAINT',
               OBJECT_SCHEMA_NAME(fk.referenced_object_id) + N'.' + OBJECT_NAME(fk.referenced_object_id)
        FROM sys.foreign_keys fk
        INNER JOIN sys.tables t ON t.object_id = fk.parent_object_id
        INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE s.name = @schema AND t.name = @table
        UNION ALL
        SELECT cc.name, N'CHECK_CONSTRAINT', cc.definition
        FROM sys.check_constraints cc
        INNER JOIN sys.tables t ON t.object_id = cc.parent_object_id
        INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE s.name = @schema AND t.name = @table;
        """;

    public const string TableIndexes = """
        SELECT i.name AS IndexName, i.is_unique AS IsUnique,
               CASE WHEN i.type_desc = N'CLUSTERED' THEN 1 ELSE 0 END AS IsClustered,
               (
                   SELECT STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal)
                   FROM sys.index_columns ic
                   INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                   WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
               ) AS Columns
        FROM sys.indexes i
        INNER JOIN sys.tables t ON t.object_id = i.object_id
        INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE s.name = @schema AND t.name = @table
          AND i.name IS NOT NULL
          AND i.is_hypothetical = 0
        ORDER BY i.index_id;
        """;

    public const string TableDescription = """
        SELECT CAST(ep.value AS nvarchar(4000))
        FROM sys.tables t
        INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        LEFT JOIN sys.extended_properties ep
            ON ep.major_id = t.object_id AND ep.minor_id = 0 AND ep.name = N'MS_Description' AND ep.class = 1
        WHERE s.name = @schema AND t.name = @table;
        """;

    public static string CategoryDisplayName(ObjectKind kind) => kind switch
    {
        ObjectKind.UserDefinedType => "User-Defined Types",
        ObjectKind.Table => "Tables",
        ObjectKind.View => "Views",
        ObjectKind.Procedure => "Procedures",
        ObjectKind.Function => "Functions",
        ObjectKind.Trigger => "Triggers",
        ObjectKind.Sequence => "Sequences",
        ObjectKind.Role => "Roles",
        _ => kind.ToString()
    };

    public static ObjectKind[] ExplorerKinds { get; } =
    {
        ObjectKind.UserDefinedType,
        ObjectKind.Table,
        ObjectKind.View,
        ObjectKind.Procedure,
        ObjectKind.Function,
        ObjectKind.Trigger,
        ObjectKind.Sequence,
        ObjectKind.Role
    };
}
