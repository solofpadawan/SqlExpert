using Microsoft.Data.SqlClient;
using SqlExpert.Core.Services;
using SqlExpert.Core.SqlEditor;
using Xunit.Abstractions;

namespace SqlExpert.Tests;

/// <summary>
/// Live tests of the plan capture, result description and completion catalog against the test database
/// (testdb/SqlExpertTeste.sql). Read-only on real tables; writes only to temp tables (#…).
/// Enabled by SQLEXPERT_IT_CONNSTR; without it every test returns immediately.
/// </summary>
public class SqlEditorResultsIntegrationTests
{
    private readonly ITestOutputHelper _out;
    private static readonly string? ConnStr = Environment.GetEnvironmentVariable("SQLEXPERT_IT_CONNSTR");

    public SqlEditorResultsIntegrationTests(ITestOutputHelper output) => _out = output;

    private SqlEditorSession? NewSession()
    {
        if (string.IsNullOrWhiteSpace(ConnStr))
        {
            _out.WriteLine("SQLEXPERT_IT_CONNSTR não definido: teste de integração ignorado.");
            return null;
        }
        var database = new SqlConnectionStringBuilder(ConnStr).InitialCatalog;
        return new SqlEditorSession(async ct =>
        {
            var c = new SqlConnection(ConnStr);
            await c.OpenAsync(ct);
            return c;
        }, database);
    }

    private void Dump(SqlExecutionResult r)
    {
        foreach (var m in r.Messages) _out.WriteLine($"[{m.Kind}] {m.Text}");
        _out.WriteLine($"PlanCaptured={r.PlanCaptured} Showplans={r.Showplans.Count} Note={r.PlanNote}");
    }

    [Fact]
    public async Task CapturePlan_SelectKeepsOneResultSet_AndYieldsPlan()
    {
        using var s = NewSession();
        if (s is null) return;
        var r = await s.ExecuteAsync(new SqlExecutionRequest { Sql = "SELECT TOP (5) * FROM dbo.Clientes ORDER BY ClienteId", CapturePlan = true });
        Dump(r);
        Assert.False(r.HasErrors);
        var set = Assert.Single(r.ResultSets);
        Assert.DoesNotContain(set.Columns, c => c.Name == ShowplanSummary.ShowplanColumnName);
        if (!r.PlanCaptured) { _out.WriteLine("Sem permissão SHOWPLAN: só a nota é verificada."); Assert.NotNull(r.PlanNote); return; }
        Assert.NotEmpty(r.Showplans);
        var stats = ShowplanSummary.Summarize(r.Showplans);
        Assert.True(stats.Statements >= 1);
        Assert.NotNull(stats.ExecuteMs);
        Assert.Contains(stats.PlanLines, l => l.Contains("[Clientes]"));
        var (plan, perf) = ShowplanSummary.Format(stats, r.RowsFetched, r.Elapsed);
        _out.WriteLine(plan);
        _out.WriteLine(perf);

        // The capture is switched off afterwards: a plain run returns no extra result set.
        var plain = await s.ExecuteAsync(new SqlExecutionRequest { Sql = "SELECT 1 AS x" });
        Assert.Single(plain.ResultSets);
        Assert.Empty(plain.Showplans);
    }

    [Fact]
    public async Task CapturePlan_DmlAndProcedure_SameResultsAndMessages()
    {
        using var s = NewSession();
        if (s is null) return;
        const string sql = "SET NOCOUNT OFF;\nCREATE TABLE #t (id int PRIMARY KEY, n nvarchar(20));\nINSERT INTO #t VALUES (1, N'a'), (2, N'b');\n" +
                           "UPDATE #t SET n = N'c' WHERE id = 2;\nSELECT * FROM #t ORDER BY id;\nEXEC dbo.spPedidosPorCliente 1;\nDROP TABLE #t;";
        var withPlan = await s.ExecuteAsync(new SqlExecutionRequest { Sql = sql, CapturePlan = true, DescribeResults = true });
        Dump(withPlan);
        var without = await s.ExecuteAsync(new SqlExecutionRequest { Sql = sql });
        Assert.False(withPlan.HasErrors);
        Assert.False(without.HasErrors);
        Assert.Equal(without.ResultSets.Count, withPlan.ResultSets.Count);
        Assert.Equal(without.ResultSets.Select(r => string.Join(",", r.Columns.Select(c => c.Name))), withPlan.ResultSets.Select(r => string.Join(",", r.Columns.Select(c => c.Name))));
        Assert.Equal(without.RowsAffected, withPlan.RowsAffected);
        Assert.Equal(without.Messages.Where(m => m.Kind == SqlMessageKind.RowsAffected).Select(m => m.Text),
                     withPlan.Messages.Where(m => m.Kind == SqlMessageKind.RowsAffected).Select(m => m.Text));
        Assert.Equal(0, withPlan.TranCount);
        // EXEC in the batch: the source of the results is unknown, so nothing is described.
        Assert.All(withPlan.ResultSets, r => Assert.Null(r.Described));
    }

    [Fact]
    public async Task CapturePlan_NoCountOn_DoesNotHideLaterCounts()
    {
        using var s = NewSession();
        if (s is null) return;
        const string sql = "CREATE TABLE #n (id int);\nINSERT INTO #n VALUES (1), (2), (3);\nSET NOCOUNT ON;\nSELECT COUNT(*) AS c FROM #n;\nSET NOCOUNT OFF;\n" +
                           "UPDATE #n SET id = id + 1;\nSELECT * FROM #n;\nDROP TABLE #n;";
        var withPlan = await s.ExecuteAsync(new SqlExecutionRequest { Sql = sql, CapturePlan = true });
        Dump(withPlan);
        var without = await s.ExecuteAsync(new SqlExecutionRequest { Sql = sql });
        Assert.False(withPlan.HasErrors);
        Assert.Equal(2, withPlan.ResultSets.Count);
        Assert.Equal(without.Messages.Where(m => m.Kind == SqlMessageKind.RowsAffected).Select(m => m.Text),
                     withPlan.Messages.Where(m => m.Kind == SqlMessageKind.RowsAffected).Select(m => m.Text));
    }

    [Fact]
    public async Task Describe_SingleTableWithKey_IsEditable()
    {
        using var s = NewSession();
        if (s is null) return;
        var r = await s.ExecuteAsync(new SqlExecutionRequest { Sql = "SELECT ClienteId, Nome, Email, UPPER(Nome) AS NomeMaiusculo FROM dbo.Clientes", DescribeResults = true });
        Assert.False(r.HasErrors);
        var set = Assert.Single(r.ResultSets);
        Assert.NotNull(set.Described);
        var d = ResultEditability.Decide(set.Described, set.Columns.Count, false);
        Assert.True(d.IsEditable, d.Reason);
        Assert.Equal("Clientes", d.Target!.Table);
        Assert.Contains("ClienteId", d.Target.KeyColumns);
        Assert.Null(d.Target.BaseColumns[3]);
        Assert.Equal("dbo.Clientes.Email", set.Described!.First(c => c.Name == "Email").SourceText);
    }

    [Fact]
    public async Task Describe_JoinHiddenKeyAndMultipleResults()
    {
        using var s = NewSession();
        if (s is null) return;
        var r = await s.ExecuteAsync(new SqlExecutionRequest
        {
            Sql = "DECLARE @id int = 1;\nSELECT c.Nome, p.PedidoId FROM dbo.Clientes c JOIN dbo.Pedidos p ON p.ClienteId = c.ClienteId WHERE c.ClienteId = @id;\n" +
                  "SELECT Nome FROM dbo.Clientes WHERE ClienteId = @id;\nSELECT * FROM dbo.Categorias;",
            DescribeResults = true
        });
        Assert.False(r.HasErrors, string.Join(" | ", r.Messages.Select(m => m.Text)));
        Assert.Equal(3, r.ResultSets.Count);
        Assert.All(r.ResultSets, x => Assert.NotNull(x.Described));
        Assert.Equal(ResultEditability.MultipleTablesReason, ResultEditability.Decide(r.ResultSets[0].Described, r.ResultSets[0].Columns.Count, false).Reason);
        var hidden = ResultEditability.Decide(r.ResultSets[1].Described, r.ResultSets[1].Columns.Count, false);
        Assert.False(hidden.IsEditable);
        _out.WriteLine(hidden.Reason);
        Assert.True(ResultEditability.Decide(r.ResultSets[2].Described, r.ResultSets[2].Columns.Count, false).IsEditable);
        Assert.False(ResultEditability.Decide(r.ResultSets[2].Described, r.ResultSets[2].Columns.Count, inTransaction: true).IsEditable);
    }

    [Fact]
    public async Task Describe_ViewResolvesToBaseTables()
    {
        using var s = NewSession();
        if (s is null) return;
        var r = await s.ExecuteAsync(new SqlExecutionRequest { Sql = "SELECT * FROM dbo.vwPedidosResumo", DescribeResults = true });
        Assert.False(r.HasErrors);
        var set = Assert.Single(r.ResultSets);
        Assert.NotNull(set.Described);
        foreach (var c in set.Described!) _out.WriteLine($"{c.Name} <- {c.SourceText}");
        Assert.False(ResultEditability.Decide(set.Described, set.Columns.Count, false).IsEditable);
    }

    [Fact]
    public async Task CatalogLoader_ReadsObjectsColumnsAndResetsLockTimeout()
    {
        if (string.IsNullOrWhiteSpace(ConnStr)) { _out.WriteLine("SQLEXPERT_IT_CONNSTR não definido: teste de integração ignorado."); return; }
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        var cat = await CompletionCatalogLoader.LoadAsync(conn);
        Assert.Equal(CompletionKind.Table, cat.Resolve("Clientes", null)!.Kind);
        Assert.Equal(CompletionKind.View, cat.Resolve("vwPedidosResumo", null)!.Kind);
        Assert.Equal(CompletionKind.Procedure, cat.Resolve("spPedidosPorCliente", "dbo")!.Kind);
        Assert.Contains(cat.ColumnsOf("dbo", "Clientes"), c => c.Name == "Email" && c.TypeText == "nvarchar(150)" && c.IsNullable);
        Assert.Contains(cat.ColumnsOf("dbo", "vwPedidosResumo"), c => c.Ordinal == 1);
        Assert.True(cat.IsSchema("dbo"));
        Assert.DoesNotContain(cat.Objects, o => o.Schema == "sys");
        await using var cmd = new SqlCommand("SELECT @@LOCK_TIMEOUT", conn);
        Assert.Equal(-1, Convert.ToInt32(await cmd.ExecuteScalarAsync()));

        var (_, items) = SqlCompletion.Complete("SELECT c. FROM dbo.Clientes c", "SELECT c.".Length, cat);
        Assert.Contains(items, i => i.DisplayName == "Email");
        var links = SqlObjectLinks.Find("SELECT * FROM Clientes c JOIN dbo.Pedidos p ON 1=1", cat);
        Assert.Equal(2, links.Count);
    }
}
