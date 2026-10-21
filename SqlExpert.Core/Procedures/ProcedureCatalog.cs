using System.Diagnostics;
using System.Text;
using Microsoft.Data.SqlClient;

namespace SqlExpert.Core.Procedures;

/// <summary>
/// Catalog reads of the procedure editor (definition, descriptions, dependencies, grants, DDL) and the
/// "Recompile all …" / "Recompute selectivity" maintenance runs. Every method takes an open connection
/// already in the right database, so the editor can use its own session connection (which sees its own
/// uncommitted DDL instead of blocking on it).
/// </summary>
public static class ProcedureCatalog
{
    public const string ServerVersionSql = "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128));";

    public const string ProcedureInfoSql = """
        SELECT o.object_id, o.type, o.create_date, o.modify_date, OBJECT_DEFINITION(o.object_id),
               CAST(ep.value AS nvarchar(max))
        FROM sys.objects o
        LEFT JOIN sys.extended_properties ep ON ep.class = 1 AND ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = N'MS_Description'
        WHERE o.object_id = OBJECT_ID(@name) AND o.type IN ('P', 'PC', 'RF', 'X');
        """;

    public const string ParameterDescriptionsSql = """
        SELECT p.name, CAST(ep.value AS nvarchar(max))
        FROM sys.parameters p
        JOIN sys.extended_properties ep ON ep.class = 2 AND ep.major_id = p.object_id AND ep.minor_id = p.parameter_id AND ep.name = N'MS_Description'
        WHERE p.object_id = OBJECT_ID(@name);
        """;

    /// <summary>Both directions: objects this one uses, and objects that use it.</summary>
    public const string DependenciesSql = """
        SELECT N'Usa' AS direction, COALESCE(OBJECT_SCHEMA_NAME(d.referenced_id), d.referenced_schema_name) AS sch,
               COALESCE(OBJECT_NAME(d.referenced_id), d.referenced_entity_name) AS obj, o.type_desc
        FROM sys.sql_expression_dependencies d
        LEFT JOIN sys.objects o ON o.object_id = d.referenced_id
        WHERE d.referencing_id = OBJECT_ID(@name)
        UNION ALL
        SELECT N'Usado por', OBJECT_SCHEMA_NAME(d.referencing_id), OBJECT_NAME(d.referencing_id), o.type_desc
        FROM sys.sql_expression_dependencies d
        JOIN sys.objects o ON o.object_id = d.referencing_id
        WHERE d.referenced_id = OBJECT_ID(@name)
        ORDER BY 1 DESC, 2, 3;
        """;

    public const string GrantsSql = """
        SELECT p.state_desc, p.permission_name, gr.name, gr.type_desc, gt.name, c.name
        FROM sys.database_permissions p
        JOIN sys.database_principals gr ON gr.principal_id = p.grantee_principal_id
        LEFT JOIN sys.database_principals gt ON gt.principal_id = p.grantor_principal_id
        LEFT JOIN sys.columns c ON c.object_id = p.major_id AND c.column_id = p.minor_id AND p.minor_id > 0
        WHERE p.class = 1 AND p.major_id = OBJECT_ID(@name)
        ORDER BY gr.name, p.permission_name;
        """;

    public const string ListProceduresSql = """
        SELECT s.name, p.name FROM sys.procedures p JOIN sys.schemas s ON s.schema_id = p.schema_id
        WHERE p.is_ms_shipped = 0 AND p.type = 'P' ORDER BY s.name, p.name;
        """;

    /// <summary>Object (DML) triggers with the schema of their table, then database DDL triggers (schema null).</summary>
    public const string ListTriggersSql = """
        SELECT OBJECT_SCHEMA_NAME(t.parent_id), t.name, t.parent_class FROM sys.triggers t
        WHERE t.is_ms_shipped = 0 AND t.type = 'TR'
        ORDER BY t.parent_class DESC, 1, 2;
        """;

    public const string ListUserTablesSql = """
        SELECT s.name, t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE t.is_ms_shipped = 0 ORDER BY s.name, t.name;
        """;

    public static async Task<string?> GetServerVersionAsync(SqlConnection conn, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand(ServerVersionSql, conn);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    public static async Task<bool> ExistsAsync(SqlConnection conn, string schema, string name, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@name, 'P') IS NULL THEN 0 ELSE 1 END;", conn);
        cmd.Parameters.AddWithValue("@name", ProcedureScriptBuilder.QualifiedName(schema, name));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)) == 1;
    }

    /// <summary>Definition + descriptions, or null when the procedure does not exist.</summary>
    public static async Task<ProcedureInfo?> LoadAsync(SqlConnection conn, string schema, string name, CancellationToken ct = default)
    {
        var qualified = ProcedureScriptBuilder.QualifiedName(schema, name);
        string? definition = null, description = null;
        bool isClr;
        DateTime? created, modified;
        await using (var cmd = new SqlCommand(ProcedureInfoSql, conn))
        {
            cmd.Parameters.AddWithValue("@name", qualified);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
            isClr = r.GetString(1).Trim() == "PC";
            created = r.IsDBNull(2) ? null : r.GetDateTime(2);
            modified = r.IsDBNull(3) ? null : r.GetDateTime(3);
            definition = r.IsDBNull(4) ? null : r.GetString(4);
            description = r.IsDBNull(5) ? null : r.GetString(5);
        }

        var paramDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new SqlCommand(ParameterDescriptionsSql, conn))
        {
            cmd.Parameters.AddWithValue("@name", qualified);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                if (!r.IsDBNull(1)) paramDescriptions[r.GetString(0)] = r.GetString(1);
        }

        return new ProcedureInfo
        {
            Schema = schema,
            Name = name,
            Definition = definition,
            Description = description,
            ParameterDescriptions = paramDescriptions,
            IsClr = isClr,
            Created = created,
            Modified = modified
        };
    }

    public static async Task<IReadOnlyList<ObjectDependency>> GetDependenciesAsync(SqlConnection conn, string schema, string name, CancellationToken ct = default)
    {
        var list = new List<ObjectDependency>();
        await using var cmd = new SqlCommand(DependenciesSql, conn);
        cmd.Parameters.AddWithValue("@name", ProcedureScriptBuilder.QualifiedName(schema, name));
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new ObjectDependency(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? "?" : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)));
        return list;
    }

    public static async Task<IReadOnlyList<ObjectPermission>> GetPermissionsAsync(SqlConnection conn, string schema, string name, CancellationToken ct = default)
    {
        var list = new List<ObjectPermission>();
        await using var cmd = new SqlCommand(GrantsSql, conn);
        cmd.Parameters.AddWithValue("@name", ProcedureScriptBuilder.QualifiedName(schema, name));
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new ObjectPermission(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)));
        return list;
    }

    /// <summary>DDL tab: definition, GO, MS_Description of the procedure and parameters, GRANT/DENY statements.</summary>
    public static string BuildDdl(ProcedureInfo info, IReadOnlyList<ObjectPermission> permissions)
    {
        const string nl = ProcedureScriptBuilder.NewLine;
        var sb = new StringBuilder();
        var def = (info.Definition ?? $"-- definição de {ProcedureScriptBuilder.QualifiedName(info.Schema, info.Name)} não disponível (criptografada?)").TrimEnd();
        sb.Append(def).Append(nl).Append("GO").Append(nl);
        if (!string.IsNullOrWhiteSpace(info.Description) || info.ParameterDescriptions.Count > 0)
        {
            sb.Append(nl);
            if (!string.IsNullOrWhiteSpace(info.Description))
                sb.Append(AddProperty(info.Schema, info.Name, null, info.Description)).Append(nl);
            foreach (var (p, d) in info.ParameterDescriptions.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                sb.Append(AddProperty(info.Schema, info.Name, p, d)).Append(nl);
            sb.Append("GO").Append(nl);
        }
        if (permissions.Count > 0)
        {
            sb.Append(nl);
            foreach (var p in permissions)
            {
                var verb = p.State switch { "DENY" => "DENY", "REVOKE" => "REVOKE", _ => "GRANT" };
                sb.Append(verb).Append(' ').Append(p.Permission).Append(" ON ").Append(ProcedureScriptBuilder.QualifiedName(info.Schema, info.Name))
                  .Append(" TO ").Append(ProcedureScriptBuilder.QuoteName(p.Grantee))
                  .Append(p.State == "GRANT_WITH_GRANT_OPTION" ? " WITH GRANT OPTION" : "").Append(';').Append(nl);
            }
            sb.Append("GO").Append(nl);
        }
        return sb.ToString();
    }

    private static string AddProperty(string schema, string name, string? parameter, string description)
    {
        var s = $"EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = {ProcedureScriptBuilder.Literal(description)}, " +
                $"@level0type = N'SCHEMA', @level0name = {ProcedureScriptBuilder.Literal(schema)}, @level1type = N'PROCEDURE', @level1name = {ProcedureScriptBuilder.Literal(name)}";
        if (parameter is not null) s += $", @level2type = N'PARAMETER', @level2name = {ProcedureScriptBuilder.Literal(parameter)}";
        return s + ";";
    }

    // ===== Maintenance runs =====

    /// <summary>What a batch run applies to.</summary>
    public enum MaintenanceKind
    {
        /// <summary>sp_refreshsqlmodule on every user stored procedure.</summary>
        RecompileProcedures,
        /// <summary>sp_refreshsqlmodule on every user trigger (DML and database DDL).</summary>
        RecompileTriggers,
        /// <summary>UPDATE STATISTICS on every user table.</summary>
        UpdateStatistics
    }

    public static string Describe(MaintenanceKind kind) => kind switch
    {
        MaintenanceKind.RecompileProcedures => "EXEC sys.sp_refreshsqlmodule @name = N'[schema].[procedure]';  -- para cada stored procedure",
        MaintenanceKind.RecompileTriggers => "EXEC sys.sp_refreshsqlmodule @name = N'[schema].[trigger]', @namespace = N'OBJECT' | N'DATABASE_DDL_TRIGGER';  -- para cada trigger",
        _ => "UPDATE STATISTICS [schema].[tabela];  -- para cada tabela de usuário"
    };

    /// <summary>Objects a run will touch: (display name, SQL to run).</summary>
    public static async Task<IReadOnlyList<(string Name, string Sql)>> ListTargetsAsync(SqlConnection conn, MaintenanceKind kind, CancellationToken ct = default)
    {
        var list = new List<(string, string)>();
        var sql = kind switch
        {
            MaintenanceKind.RecompileProcedures => ListProceduresSql,
            MaintenanceKind.RecompileTriggers => ListTriggersSql,
            _ => ListUserTablesSql
        };
        await using var cmd = new SqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var schema = r.IsDBNull(0) ? null : r.GetString(0);
            var name = r.GetString(1);
            list.Add(BuildTarget(kind, schema, name, kind == MaintenanceKind.RecompileTriggers && r.GetByte(2) == 0));
        }
        return list;
    }

    public static (string Name, string Sql) BuildTarget(MaintenanceKind kind, string? schema, string name, bool databaseDdlTrigger = false)
    {
        switch (kind)
        {
            case MaintenanceKind.RecompileProcedures:
            {
                var q = ProcedureScriptBuilder.QualifiedName(schema ?? "dbo", name);
                return (q, $"EXEC sys.sp_refreshsqlmodule @name = {ProcedureScriptBuilder.Literal(q)};");
            }
            case MaintenanceKind.RecompileTriggers when databaseDdlTrigger:
            {
                var q = ProcedureScriptBuilder.QuoteName(name);
                return (q + " (DATABASE)", $"EXEC sys.sp_refreshsqlmodule @name = {ProcedureScriptBuilder.Literal(q)}, @namespace = N'DATABASE_DDL_TRIGGER';");
            }
            case MaintenanceKind.RecompileTriggers:
            {
                var q = ProcedureScriptBuilder.QualifiedName(schema ?? "dbo", name);
                return (q, $"EXEC sys.sp_refreshsqlmodule @name = {ProcedureScriptBuilder.Literal(q)}, @namespace = N'OBJECT';");
            }
            default:
            {
                var q = ProcedureScriptBuilder.QualifiedName(schema ?? "dbo", name);
                return (q, $"UPDATE STATISTICS {q};");
            }
        }
    }

    /// <summary>
    /// Runs each target in turn (autocommit, no timeout), reporting progress; failures are collected, never thrown.
    /// Cancellation stops before the next object.
    /// </summary>
    public static async Task<BatchOperationResult> RunAsync(SqlConnection conn, IReadOnlyList<(string Name, string Sql)> targets,
        IProgress<(int Done, int Total, string Current)>? progress = null, CancellationToken ct = default)
    {
        var result = new BatchOperationResult { Total = targets.Count };
        var sw = Stopwatch.StartNew();
        foreach (var (name, sql) in targets)
        {
            if (ct.IsCancellationRequested) { result.Cancelled = true; break; }
            progress?.Report((result.Done, targets.Count, name));
            try
            {
                await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 0 };
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                result.Cancelled = true;
                break;
            }
            catch (SqlException ex)
            {
                if (ct.IsCancellationRequested) { result.Cancelled = true; break; }
                result.Failures.Add(new BatchOperationFailure(name, string.Join(" | ", ex.Errors.Cast<SqlError>().Where(e => e.Class > 10).Select(e => e.Message).DefaultIfEmpty(ex.Message))));
            }
            result.Done++;
        }
        progress?.Report((result.Done, targets.Count, ""));
        sw.Stop();
        result.Elapsed = sw.Elapsed;
        return result;
    }

    /// <summary>Report text of a run: totals, then one line per failure.</summary>
    public static string FormatReport(string title, BatchOperationResult r)
    {
        const string nl = ProcedureScriptBuilder.NewLine;
        var sb = new StringBuilder();
        sb.Append(title).Append(nl);
        sb.Append($"Objetos: {r.Total} · processados: {r.Done} · com erro: {r.Failures.Count} · tempo: {r.Elapsed.TotalSeconds:0.0} s");
        if (r.Cancelled) sb.Append(" · CANCELADO");
        sb.Append(nl);
        if (r.Failures.Count == 0)
        {
            sb.Append(nl).Append(r.Cancelled ? "Nenhuma falha até o cancelamento." : "Nenhuma falha.").Append(nl);
            return sb.ToString();
        }
        sb.Append(nl).Append("Falhas:").Append(nl);
        foreach (var f in r.Failures)
            sb.Append("• ").Append(f.Object).Append(": ").Append(f.Message).Append(nl);
        return sb.ToString();
    }
}
