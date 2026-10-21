using Microsoft.Data.SqlClient;
using SqlExpert.Core.Models;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlExpert.Core.SqlEditor;

/// <summary>One column of sp_describe_first_result_set (@browse_information_mode = 1).</summary>
public sealed record DescribedColumn(
    int Ordinal,
    string? Name,
    bool IsHidden,
    bool IsNullable,
    string? TypeName,
    string? SourceDatabase,
    string? SourceSchema,
    string? SourceTable,
    string? SourceColumn,
    bool IsPartOfUniqueKey,
    bool IsIdentity = false,
    bool IsComputed = false)
{
    public string? SourceText => SourceTable is null
        ? null
        : string.Join(".", new[] { SourceSchema, SourceTable, SourceColumn }.Where(p => !string.IsNullOrEmpty(p)));
}

/// <summary>
/// Which statement of a batch produced each result set, so its source can be described afterwards
/// (sp_describe_first_result_set). Conservative: anything that may return a variable number of result sets
/// (EXEC, IF/WHILE blocks, USE…) makes the batch "unknown" and its results stay read-only.
/// </summary>
public static class ResultSourceAnalyzer
{
    /// <summary>
    /// Text to describe for each result-producing statement of <paramref name="batch"/>, in order: the DECLAREs
    /// before it plus the statement itself (null entry: a statement that returns rows but is not a plain SELECT,
    /// e.g. INSERT … OUTPUT). Null when the mapping is not reliable.
    /// </summary>
    public static IReadOnlyList<string?>? ResultStatements(string batch)
    {
        if (string.IsNullOrWhiteSpace(batch)) return Array.Empty<string?>();
        var parser = new Dom.TSql160Parser(initialQuotedIdentifiers: true);
        Dom.TSqlFragment fragment;
        IList<Dom.ParseError> errors;
        using (var reader = new StringReader(batch))
            fragment = parser.Parse(reader, out errors);
        if (errors.Count > 0 || fragment is not Dom.TSqlScript script) return null;
        var list = new List<string?>();
        var declarations = new List<string>();
        foreach (var b in script.Batches)
        {
            foreach (var st in b.Statements)
            {
                var text = batch.Substring(st.StartOffset, st.FragmentLength);
                switch (st)
                {
                    case Dom.SelectStatement sel:
                        if (sel.Into is not null) break;
                        if (sel.QueryExpression is Dom.QuerySpecification qs && qs.SelectElements.Any(e => e is Dom.SelectSetVariable)) break;
                        list.Add(declarations.Count == 0 ? text : string.Join(Environment.NewLine, declarations) + Environment.NewLine + text);
                        break;
                    case Dom.DeclareVariableStatement or Dom.DeclareTableVariableStatement:
                        declarations.Add(text);
                        break;
                    case Dom.InsertStatement ins:
                        if (ins.InsertSpecification.OutputClause is not null) list.Add(null);
                        break;
                    case Dom.UpdateStatement upd:
                        if (upd.UpdateSpecification.OutputClause is not null) list.Add(null);
                        break;
                    case Dom.DeleteStatement del:
                        if (del.DeleteSpecification.OutputClause is not null) list.Add(null);
                        break;
                    case Dom.MergeStatement merge:
                        if (merge.MergeSpecification.OutputClause is not null) list.Add(null);
                        break;
                    case Dom.SetVariableStatement or Dom.PredicateSetStatement or Dom.SetRowCountStatement or Dom.SetTransactionIsolationLevelStatement
                        or Dom.PrintStatement or Dom.TransactionStatement or Dom.SetIdentityInsertStatement
                        or Dom.TruncateTableStatement or Dom.DropObjectsStatement or Dom.DropIndexStatement:
                        break;
                    default:
                        var name = st.GetType().Name;
                        if (name.StartsWith("Create", StringComparison.Ordinal) || name.StartsWith("Alter", StringComparison.Ordinal)
                            || name.StartsWith("Drop", StringComparison.Ordinal))
                            break;
                        return null; // EXEC, IF, WHILE, BEGIN…END, USE, DBCC, …
                }
            }
        }
        return list;
    }

    public const string DescribeSql = "EXEC sys.sp_describe_first_result_set @tsql = @tsql, @params = NULL, @browse_information_mode = 1;";

    /// <summary>Runs sp_describe_first_result_set for <paramref name="statement"/> (browse mode: base tables and key columns).</summary>
    public static async Task<IReadOnlyList<DescribedColumn>> DescribeAsync(SqlConnection conn, string statement, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand(DescribeSql, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@tsql", System.Data.SqlDbType.NVarChar, -1) { Value = statement });
        var list = new List<DescribedColumn>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        int Ord(string n) { try { return r.GetOrdinal(n); } catch (IndexOutOfRangeException) { return -1; } }
        var iHidden = Ord("is_hidden"); var iOrd = Ord("column_ordinal"); var iName = Ord("name"); var iNull = Ord("is_nullable");
        var iType = Ord("system_type_name"); var iDb = Ord("source_database"); var iSchema = Ord("source_schema");
        var iTable = Ord("source_table"); var iCol = Ord("source_column"); var iKey = Ord("is_part_of_unique_key");
        var iIdent = Ord("is_identity_column"); var iComp = Ord("is_computed_column");
        string? S(int i) => i < 0 || r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));
        bool B(int i) => i >= 0 && !r.IsDBNull(i) && Convert.ToBoolean(r.GetValue(i));
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new DescribedColumn(
                iOrd >= 0 && !r.IsDBNull(iOrd) ? Convert.ToInt32(r.GetValue(iOrd)) : list.Count + 1,
                S(iName), B(iHidden), B(iNull), S(iType), S(iDb), S(iSchema), S(iTable), S(iCol), B(iKey), B(iIdent), B(iComp)));
        }
        return list;
    }
}

/// <summary>The base table a result set can be edited in, and the table column behind each result column.</summary>
/// <param name="BaseColumns">Per visible result column: the table column, or null (expression / repeated column: read-only).</param>
public sealed record ResultEditTarget(string? Database, string Schema, string Table, IReadOnlyList<string?> BaseColumns, IReadOnlyList<string> KeyColumns);

public sealed record ResultEditDecision(ResultEditTarget? Target, string? Reason)
{
    public bool IsEditable => Target is not null;
    public static ResultEditDecision ReadOnly(string reason) => new(null, reason);
}

/// <summary>Decides whether a SQL Editor result can be edited like a table's Data tab.</summary>
public static class ResultEditability
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    public const string NoSourceReason = "Somente leitura: o resultado não vem de um SELECT simples (EXEC, IF, lote com vários comandos).";
    public const string TransactionReason = "Somente leitura: há uma transação aberta neste editor (Commit/Rollback para editar o resultado).";
    public const string MultipleTablesReason = "Somente leitura: o resultado combina mais de uma tabela (JOIN / UNION / subconsulta).";
    public const string NoTableReason = "Somente leitura: nenhuma coluna vem diretamente de uma tabela.";
    public const string NoKeyReason = "Somente leitura: a tabela não tem chave primária nem chave única.";

    /// <summary>
    /// Editable only when every column that comes from a table comes from the same base table, that table has a
    /// primary (or unique) key and all key columns are in the result, and the editor has no open transaction (Post
    /// runs on its own connection and would wait for it).
    /// </summary>
    public static ResultEditDecision Decide(IReadOnlyList<DescribedColumn>? described, int resultColumnCount, bool inTransaction)
    {
        if (described is null || described.Count == 0) return ResultEditDecision.ReadOnly(NoSourceReason);
        if (inTransaction) return ResultEditDecision.ReadOnly(TransactionReason);
        var visible = described.Where(c => !c.IsHidden).OrderBy(c => c.Ordinal).ToList();
        if (visible.Count != resultColumnCount) return ResultEditDecision.ReadOnly(NoSourceReason);

        var tables = described.Where(c => c.SourceTable is not null)
            .Select(c => (Db: c.SourceDatabase ?? "", Schema: c.SourceSchema ?? "dbo", Table: c.SourceTable!))
            .Distinct(TableComparer.Instance).ToList();
        if (tables.Count == 0) return ResultEditDecision.ReadOnly(NoTableReason);
        if (tables.Count > 1) return ResultEditDecision.ReadOnly(MultipleTablesReason);
        var t = tables[0];

        var hiddenKeys = described.Where(c => c.IsHidden && c.IsPartOfUniqueKey).Select(c => c.SourceColumn ?? c.Name ?? "?").ToList();
        if (hiddenKeys.Count > 0)
            return ResultEditDecision.ReadOnly($"Somente leitura: a chave ({string.Join(", ", hiddenKeys)}) não está no resultado.");

        var keys = visible.Where(c => c.IsPartOfUniqueKey && c.SourceColumn is not null).Select(c => c.SourceColumn!).Distinct(Ci).ToList();
        if (keys.Count == 0) return ResultEditDecision.ReadOnly(NoKeyReason);

        var seen = new HashSet<string>(Ci);
        var baseColumns = visible.Select(c =>
            c.SourceTable is not null && c.SourceColumn is not null && seen.Add(c.SourceColumn) ? c.SourceColumn : null).ToList();
        return new ResultEditDecision(new ResultEditTarget(t.Db.Length == 0 ? null : t.Db, t.Schema, t.Table, baseColumns, keys), null);
    }

    /// <summary>
    /// Checks the key against the table schema read for the Data tab. With a primary key, all its columns must be in
    /// the result; without one, the unique key reported by the server is used (NOT NULL columns only) and the returned
    /// copy of the schema marks those columns as the key. Null + reason when it cannot be edited.
    /// </summary>
    public static TableDataSchema? ApplyKey(TableDataSchema schema, ResultEditTarget target, out string? reason)
    {
        reason = null;
        var inResult = new HashSet<string>(target.BaseColumns.Where(c => c is not null)!, Ci);
        if (schema.HasPrimaryKey)
        {
            var missing = schema.KeyColumns.Where(k => !inResult.Contains(k.Name)).Select(k => k.Name).ToList();
            if (missing.Count == 0) return schema;
            reason = $"Somente leitura: a chave primária ({string.Join(", ", missing)}) não está no resultado.";
            return null;
        }
        var cols = target.KeyColumns.Select(k => schema.Columns.FirstOrDefault(c => Ci.Equals(c.Name, k))).ToList();
        if (cols.Count == 0 || cols.Any(c => c is null) || !target.KeyColumns.All(inResult.Contains))
        {
            reason = NoKeyReason;
            return null;
        }
        if (cols.Any(c => c!.IsNullable))
        {
            reason = "Somente leitura: a chave única da tabela aceita NULL.";
            return null;
        }
        var keyOrdinal = 0;
        var keyNames = new HashSet<string>(target.KeyColumns, Ci);
        var copy = schema.Columns.Select(c => new DataColumnInfo
        {
            Ordinal = c.Ordinal, Name = c.Name, TypeName = c.TypeName, BaseTypeName = c.BaseTypeName, IsNullable = c.IsNullable,
            IsIdentity = c.IsIdentity, IsComputed = c.IsComputed, IsRowVersion = c.IsRowVersion, IsClrType = c.IsClrType,
            IsPrimaryKey = keyNames.Contains(c.Name), KeyOrdinal = keyNames.Contains(c.Name) ? ++keyOrdinal : 0,
            MaxLength = c.MaxLength, Precision = c.Precision, Scale = c.Scale
        }).ToList();
        return new TableDataSchema { Schema = schema.Schema, Table = schema.Table, Columns = copy, ForeignKeys = schema.ForeignKeys };
    }

    private sealed class TableComparer : IEqualityComparer<(string Db, string Schema, string Table)>
    {
        public static readonly TableComparer Instance = new();
        public bool Equals((string Db, string Schema, string Table) a, (string Db, string Schema, string Table) b) =>
            Ci.Equals(a.Db, b.Db) && Ci.Equals(a.Schema, b.Schema) && Ci.Equals(a.Table, b.Table);
        public int GetHashCode((string Db, string Schema, string Table) v) =>
            HashCode.Combine(Ci.GetHashCode(v.Db), Ci.GetHashCode(v.Schema), Ci.GetHashCode(v.Table));
    }
}
