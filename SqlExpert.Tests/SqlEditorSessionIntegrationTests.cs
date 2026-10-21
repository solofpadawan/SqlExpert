using Microsoft.Data.SqlClient;
using SqlExpert.Core.SqlEditor;
using Xunit.Abstractions;

namespace SqlExpert.Tests;

/// <summary>
/// Live tests of <see cref="SqlEditorSession"/> against a real SQL Server. They only run SELECTs and use
/// temp tables (#…), so no database object is changed. Enabled by the environment variable
/// SQLEXPERT_IT_CONNSTR (e.g. "Server=localhost\SQLEXPRESS;Database=SqlExpertTeste;Integrated Security=true;TrustServerCertificate=true");
/// without it every test returns immediately.
/// </summary>
public class SqlEditorSessionIntegrationTests
{
    private readonly ITestOutputHelper _out;
    private static readonly string? ConnStr = Environment.GetEnvironmentVariable("SQLEXPERT_IT_CONNSTR");

    public SqlEditorSessionIntegrationTests(ITestOutputHelper output) => _out = output;

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

    [Fact]
    public async Task ResultSets_Print_RowsAffected()
    {
        using var s = NewSession();
        if (s is null) return;
        var r = await s.ExecuteAsync(new SqlExecutionRequest
        {
            Sql = "SET NOCOUNT OFF;\nPRINT 'olá do PRINT';\nCREATE TABLE #t (id int);\nINSERT INTO #t VALUES (1),(2),(3);\n" +
                  "SELECT id, CAST(NULL AS nvarchar(10)) AS vazio FROM #t ORDER BY id;\nSELECT COUNT(*) AS n FROM sys.objects;\nUPDATE #t SET id = id + 10;"
        });
        Assert.False(r.HasErrors, string.Join(" | ", r.Messages.Select(m => m.Text)));
        Assert.Equal(2, r.ResultSets.Count);
        Assert.Equal(new[] { "id", "vazio" }, r.ResultSets[0].Columns.Select(c => c.Name));
        Assert.Equal(3, r.ResultSets[0].Rows.Count);
        Assert.Null(r.ResultSets[0].Rows[0][1]);
        Assert.Equal(1, r.ResultSets[0].Rows[0][0]);
        Assert.Equal(6, r.RowsAffected);
        Assert.Contains(r.Messages, m => m.Kind == SqlMessageKind.Info && m.Text == "olá do PRINT");
        Assert.Contains(r.Messages, m => m.Kind == SqlMessageKind.RowsAffected && m.Text == "(3 rows affected)");
        Assert.Equal(0, r.TranCount);
    }

    [Fact]
    public async Task FetchCap_CountsAllRows()
    {
        using var s = NewSession();
        if (s is null) return;
        const string sql = "SELECT TOP (2500) a.object_id FROM sys.all_columns a CROSS JOIN (VALUES (1), (2), (3)) v(x)";
        var r = await s.ExecuteAsync(new SqlExecutionRequest { Sql = sql, MaxRows = 1000 });
        Assert.Equal(1000, r.ResultSets.Single().Rows.Count);
        Assert.Equal(2500, r.ResultSets.Single().TotalRowCount);
        Assert.True(r.HasMoreRows);
        var all = await s.ExecuteAsync(new SqlExecutionRequest { Sql = sql, MaxRows = 0 });
        Assert.Equal(2500, all.ResultSets.Single().Rows.Count);
        Assert.False(all.HasMoreRows);
    }

    [Fact]
    public async Task ErrorLine_IsMappedToEditor_AndStopsOnError()
    {
        using var s = NewSession();
        if (s is null) return;
        // Executed text starts on editor line 10; batch 2 starts on its line 3; the bad table is on the batch's line 2.
        var r = await s.ExecuteAsync(new SqlExecutionRequest
        {
            Sql = "SELECT 1 AS a\nGO\nSELECT 2 AS b\nSELECT * FROM dbo.TabelaQueNaoExiste_SqlExpertIT\nGO\nSELECT 3 AS c",
            StartLine = 10
        });
        var err = Assert.Single(r.Messages, m => m.Kind == SqlMessageKind.Error);
        Assert.Equal(208, err.Number);
        Assert.Equal(13, err.EditorLine);
        Assert.StartsWith("Msg 208, Level 16, State 1, Line 13", err.Text);
        Assert.Equal(13, r.FirstErrorLine);
        // Batch 2 returns "b" before failing on the missing table (deferred name resolution); batch 3 never runs.
        Assert.Equal(new[] { "a", "b" }, r.ResultSets.Select(x => x.Columns[0].Name));
        Assert.Equal(3, r.BatchCount);
    }

    [Fact]
    public async Task GoRepeat_AndTempTableSurvivesBetweenRuns()
    {
        using var s = NewSession();
        if (s is null) return;
        var r1 = await s.ExecuteAsync(new SqlExecutionRequest { Sql = "CREATE TABLE #g (i int)\nGO\nINSERT INTO #g VALUES (1)\nGO 3" });
        Assert.False(r1.HasErrors, string.Join(" | ", r1.Messages.Select(m => m.Text)));
        var r2 = await s.ExecuteAsync(new SqlExecutionRequest { Sql = "SELECT COUNT(*) FROM #g" });
        Assert.Equal(3, r2.ResultSets.Single().Rows[0][0]);
        Assert.Equal("(No column name)", r2.ResultSets.Single().Columns[0].Name);
    }

    [Fact]
    public async Task ExplicitTransaction_RollbackAndCommit()
    {
        using var s = NewSession();
        if (s is null) return;
        await s.ExecuteAsync(new SqlExecutionRequest { Sql = "CREATE TABLE #x (i int)" });
        s.Autocommit = false;

        var ins = await s.ExecuteAsync(new SqlExecutionRequest { Sql = "INSERT INTO #x VALUES (1)" });
        Assert.Equal(1, ins.TranCount);
        Assert.True(s.InTransaction);
        var rb = await s.RollbackAsync();
        Assert.False(rb.HasErrors);
        Assert.False(s.InTransaction);
        var count0 = await s.ExecuteAsync(new SqlExecutionRequest { Sql = "SELECT COUNT(*) FROM #x" });
        Assert.Equal(0, count0.ResultSets.Single().Rows[0][0]);
        Assert.True(s.InTransaction); // explicit mode: the SELECT opened a new transaction

        await s.ExecuteAsync(new SqlExecutionRequest { Sql = "INSERT INTO #x VALUES (2)" });
        await s.CommitAsync();
        Assert.False(s.InTransaction);
        s.Autocommit = true;
        var count1 = await s.ExecuteAsync(new SqlExecutionRequest { Sql = "SELECT COUNT(*) FROM #x" });
        Assert.Equal(1, count1.ResultSets.Single().Rows[0][0]);
        Assert.Equal(0, count1.TranCount);
    }

    [Fact]
    public async Task Cancel_StopsLongQuery_AndSessionStaysUsable()
    {
        using var s = NewSession();
        if (s is null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        var r = await s.ExecuteAsync(new SqlExecutionRequest { Sql = "WAITFOR DELAY '00:00:20'; SELECT 1" }, cts.Token);
        Assert.True(r.Cancelled);
        Assert.True(r.Elapsed < TimeSpan.FromSeconds(10), r.Elapsed.ToString());
        var after = await s.ExecuteAsync(new SqlExecutionRequest { Sql = "SELECT 42 AS x" });
        Assert.False(after.HasErrors);
        Assert.Equal(42, after.ResultSets.Single().Rows[0][0]);
    }

    [Fact]
    public async Task Use_ChangesCurrentDatabase_AndRaiserrorInfo()
    {
        using var s = NewSession();
        if (s is null) return;
        var r = await s.ExecuteAsync(new SqlExecutionRequest { Sql = "USE master;\nRAISERROR('aviso', 10, 1);" });
        Assert.False(r.HasErrors);
        Assert.Equal("master", s.CurrentDatabase);
        Assert.Contains(r.Messages, m => m.Kind == SqlMessageKind.Info && m.Text == "aviso");
    }
}
