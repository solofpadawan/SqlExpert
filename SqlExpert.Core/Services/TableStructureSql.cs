namespace SqlExpert.Core.Services;

/// <summary>
/// One batch, several result sets, that reads everything <see cref="Models.TableStructure"/> needs.
/// Parameters: @schema, @table. Exposed for unit tests.
/// </summary>
public static class TableStructureSql
{
    public const string Load = """
        DECLARE @id int = OBJECT_ID(QUOTENAME(@schema) + N'.' + QUOTENAME(@table), N'U');

        -- 0: header
        SELECT @id AS ObjectId,
               CAST((SELECT ep.value FROM sys.extended_properties ep
                     WHERE ep.class = 1 AND ep.major_id = @id AND ep.minor_id = 0 AND ep.name = N'MS_Description') AS nvarchar(4000)) AS Description,
               CAST(ISNULL((SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = @id AND p.index_id IN (0, 1)), 0) AS bigint) AS [RowCount],
               CAST(IDENT_CURRENT(QUOTENAME(@schema) + N'.' + QUOTENAME(@table)) AS decimal(38, 0)) AS IdentCurrent;

        -- 1: columns
        SELECT c.column_id, c.name, ty.name AS type_name, SCHEMA_NAME(ty.schema_id) AS type_schema, ty.is_user_defined,
               CAST(CASE
                   WHEN c.max_length = -1 THEN -1
                   WHEN ty.name IN (N'nchar', N'nvarchar') THEN c.max_length / 2
                   ELSE c.max_length END AS int) AS length,
               CAST(c.precision AS int) AS precision, CAST(c.scale AS int) AS scale, c.is_nullable, c.collation_name,
               c.is_identity, CAST(ic.seed_value AS decimal(38, 0)) AS seed, CAST(ic.increment_value AS decimal(38, 0)) AS increment,
               c.is_computed, cc.definition AS computed_definition, ISNULL(cc.is_persisted, 0) AS is_persisted,
               dc.name AS default_name, dc.definition AS default_definition, ISNULL(dc.is_system_named, 0) AS default_system_named,
               CAST(ep.value AS nvarchar(4000)) AS description
        FROM sys.columns c
        INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
        LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
        LEFT JOIN sys.extended_properties ep
            ON ep.class = 1 AND ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = N'MS_Description'
        WHERE c.object_id = @id
        ORDER BY c.column_id;

        -- 2: PK / UNIQUE constraints
        SELECT kc.name, CASE WHEN kc.type = 'PK' THEN 1 ELSE 0 END AS is_pk,
               CASE WHEN i.type = 1 THEN 1 ELSE 0 END AS is_clustered,
               col.name AS column_name, ic.is_descending_key
        FROM sys.key_constraints kc
        INNER JOIN sys.indexes i ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
        INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        INNER JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
        WHERE kc.parent_object_id = @id
        ORDER BY CASE WHEN kc.type = 'PK' THEN 0 ELSE 1 END, kc.name, ic.key_ordinal;

        -- 3: CHECK constraints
        SELECT cc.name, cc.definition, COL_NAME(cc.parent_object_id, cc.parent_column_id) AS column_name,
               cc.is_not_trusted, cc.is_disabled
        FROM sys.check_constraints cc
        WHERE cc.parent_object_id = @id
        ORDER BY cc.name;

        -- 4: foreign keys declared here or referencing this table
        SELECT fk.name,
               OBJECT_SCHEMA_NAME(fk.parent_object_id) AS parent_schema, OBJECT_NAME(fk.parent_object_id) AS parent_table,
               COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS column_name,
               OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS ref_schema, OBJECT_NAME(fk.referenced_object_id) AS ref_table,
               COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS ref_column,
               fk.delete_referential_action_desc, fk.update_referential_action_desc,
               fk.is_not_trusted, fk.is_disabled,
               CASE WHEN fk.parent_object_id = @id THEN 1 ELSE 0 END AS is_outgoing
        FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        WHERE fk.parent_object_id = @id OR fk.referenced_object_id = @id
        ORDER BY is_outgoing DESC, fk.name, fkc.constraint_column_id;

        -- 5: indexes that are not PK/UNIQUE constraints
        SELECT i.name, i.is_unique, CAST(i.type AS int) AS type, i.type_desc, i.filter_definition,
               col.name AS column_name, ic.is_included_column, ic.is_descending_key
        FROM sys.indexes i
        INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        INNER JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
        WHERE i.object_id = @id AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
          AND i.is_hypothetical = 0 AND i.name IS NOT NULL
        ORDER BY i.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id;

        -- 6: triggers
        SELECT tr.name, OBJECT_DEFINITION(tr.object_id) AS definition, tr.is_disabled
        FROM sys.triggers tr
        WHERE tr.parent_id = @id
        ORDER BY tr.name;
        """;

    /// <summary>Database default collation first, then all collations.</summary>
    public const string Collations = """
        SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128)) AS name, 0 AS ord
        UNION ALL
        SELECT name, 1 FROM sys.fn_helpcollations()
        ORDER BY ord, name;
        """;
}
