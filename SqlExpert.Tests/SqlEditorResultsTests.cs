using System.Globalization;
using System.Xml.Linq;
using SqlExpert.Core.Commands;
using SqlExpert.Core.Models;
using SqlExpert.Core.SqlEditor;

namespace SqlExpert.Tests;

/// <summary>
/// SQL Editor results: which statement produced each result set, when the result grid may be edited,
/// the plan / performance summary of Messages and the page layout (Edit | Results | History | …).
/// </summary>
public class SqlEditorResultsTests
{
    // ===== Statements behind the result sets =====

    [Fact]
    public void ResultStatements_SingleSelect()
    {
        var list = ResultSourceAnalyzer.ResultStatements("SELECT * FROM dbo.Clientes WHERE ClienteId > 1");
        Assert.NotNull(list);
        Assert.Equal("SELECT * FROM dbo.Clientes WHERE ClienteId > 1", Assert.Single(list!));
    }

    [Fact]
    public void ResultStatements_DeclaresArePrefixed_SetAndPrintIgnored()
    {
        var list = ResultSourceAnalyzer.ResultStatements("SET NOCOUNT ON;\nDECLARE @id int = 2;\nPRINT 'x';\nSELECT * FROM Clientes WHERE ClienteId = @id;\nSELECT Nome FROM Clientes;");
        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);
        Assert.Contains("DECLARE @id int = 2;", list[0]);
        Assert.EndsWith("SELECT * FROM Clientes WHERE ClienteId = @id;", list[0]!.TrimEnd());
        Assert.Contains("SELECT Nome FROM Clientes", list[1]);
    }

    [Fact]
    public void ResultStatements_AssignmentAndSelectIntoProduceNoResult()
    {
        var list = ResultSourceAnalyzer.ResultStatements("DECLARE @n int; SELECT @n = COUNT(*) FROM Clientes; SELECT * INTO #c FROM Clientes; SELECT * FROM #c;");
        Assert.NotNull(list);
        Assert.Single(list!);
    }

    [Fact]
    public void ResultStatements_OutputClauseIsAnUnknownSource()
    {
        var list = ResultSourceAnalyzer.ResultStatements("UPDATE Clientes SET Nome = Nome OUTPUT inserted.ClienteId WHERE 1 = 0; SELECT 1 AS x;");
        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);
        Assert.Null(list[0]);
        Assert.NotNull(list[1]);
        Assert.Empty(ResultSourceAnalyzer.ResultStatements("UPDATE Clientes SET Nome = Nome WHERE 1 = 0")!);
    }

    [Theory]
    [InlineData("EXEC dbo.spPedidosPorCliente 1")]
    [InlineData("IF 1 = 1 SELECT 1 ELSE SELECT 2")]
    [InlineData("SELECT FROM WHERE (")]
    public void ResultStatements_UnknownBatches(string sql) => Assert.Null(ResultSourceAnalyzer.ResultStatements(sql));

    // ===== Editability =====

    private static DescribedColumn Col(int ord, string name, string? table = "Clientes", string? column = null, bool key = false, bool hidden = false, string schema = "dbo") =>
        new(ord, name, hidden, true, "int", "SqlExpertTeste", table is null ? null : schema, table, table is null ? null : column ?? name, key);

    [Fact]
    public void Decide_SingleTableWithKey_IsEditable()
    {
        var d = ResultEditability.Decide(new[] { Col(1, "ClienteId", key: true), Col(2, "Nome"), Col(3, "Total", table: null) }, 3, false);
        Assert.True(d.IsEditable, d.Reason);
        Assert.Equal("Clientes", d.Target!.Table);
        Assert.Equal("dbo", d.Target.Schema);
        Assert.Equal(new[] { "ClienteId" }, d.Target.KeyColumns);
        Assert.Equal(new string?[] { "ClienteId", "Nome", null }, d.Target.BaseColumns);
    }

    [Fact]
    public void Decide_RepeatedSourceColumn_OnlyFirstIsBase()
    {
        var d = ResultEditability.Decide(new[] { Col(1, "ClienteId", key: true), Col(2, "Nome"), Col(3, "Nome2", column: "Nome") }, 3, false);
        Assert.True(d.IsEditable);
        Assert.Equal(new string?[] { "ClienteId", "Nome", null }, d.Target!.BaseColumns);
    }

    [Fact]
    public void Decide_ReadOnlyCases()
    {
        Assert.Equal(ResultEditability.NoSourceReason, ResultEditability.Decide(null, 2, false).Reason);
        Assert.Equal(ResultEditability.TransactionReason, ResultEditability.Decide(new[] { Col(1, "ClienteId", key: true) }, 1, true).Reason);
        Assert.Equal(ResultEditability.MultipleTablesReason,
            ResultEditability.Decide(new[] { Col(1, "ClienteId", key: true), Col(2, "PedidoId", table: "Pedidos", key: true) }, 2, false).Reason);
        Assert.Equal(ResultEditability.NoTableReason, ResultEditability.Decide(new[] { Col(1, "x", table: null) }, 1, false).Reason);
        Assert.Equal(ResultEditability.NoKeyReason, ResultEditability.Decide(new[] { Col(1, "Nome") }, 1, false).Reason);
        var hidden = ResultEditability.Decide(new[] { Col(1, "Nome"), Col(2, "ClienteId", key: true, hidden: true) }, 1, false);
        Assert.False(hidden.IsEditable);
        Assert.Contains("ClienteId", hidden.Reason);
        Assert.False(ResultEditability.Decide(new[] { Col(1, "ClienteId", key: true) }, 2, false).IsEditable); // column count mismatch
    }

    private static TableDataSchema Schema(bool pk, bool emailNullable = false) => new()
    {
        Schema = "dbo", Table = "Clientes",
        Columns = new[]
        {
            new DataColumnInfo { Ordinal = 1, Name = "ClienteId", TypeName = "int", IsPrimaryKey = pk, KeyOrdinal = pk ? 1 : 0 },
            new DataColumnInfo { Ordinal = 2, Name = "Nome", TypeName = "nvarchar" },
            new DataColumnInfo { Ordinal = 3, Name = "Email", TypeName = "nvarchar", IsNullable = emailNullable },
        }
    };

    [Fact]
    public void ApplyKey_PrimaryKeyMustBeInTheResult()
    {
        var ok = new ResultEditTarget("db", "dbo", "Clientes", new string?[] { "ClienteId", "Nome" }, new[] { "ClienteId" });
        Assert.NotNull(ResultEditability.ApplyKey(Schema(true), ok, out var r1));
        Assert.Null(r1);
        var missing = new ResultEditTarget("db", "dbo", "Clientes", new string?[] { "Email", "Nome" }, new[] { "Email" });
        Assert.Null(ResultEditability.ApplyKey(Schema(true), missing, out var r2));
        Assert.Contains("ClienteId", r2);
    }

    [Fact]
    public void ApplyKey_UniqueKeyWithoutPrimaryKey()
    {
        var target = new ResultEditTarget("db", "dbo", "Clientes", new string?[] { "Email", "Nome" }, new[] { "Email" });
        var keyed = ResultEditability.ApplyKey(Schema(false), target, out var reason);
        Assert.NotNull(keyed);
        Assert.Null(reason);
        Assert.Equal("Email", Assert.Single(keyed!.KeyColumns).Name);
        Assert.False(Schema(false).HasPrimaryKey); // original untouched

        Assert.Null(ResultEditability.ApplyKey(Schema(false, emailNullable: true), target, out var nullable));
        Assert.Contains("NULL", nullable);
    }

    // ===== Plan / Performance info =====

    private const string Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    private static string SamplePlan(int elapsed = 3, int cpu = 2, int reads = 1234) => $"""
        <ShowPlanXML xmlns="{Ns}" Version="1.6">
          <BatchSequence><Batch><Statements>
            <StmtSimple StatementText="SELECT *&#xD;&#xA;  FROM dbo.Clientes c JOIN dbo.Pedidos p ON p.ClienteId = c.ClienteId" StatementType="SELECT">
              <QueryPlan CompileTime="5" CompileCPU="4">
                <QueryTimeStats ElapsedTime="{elapsed}" CpuTime="{cpu}"/>
                <RelOp PhysicalOp="Nested Loops" LogicalOp="Inner Join">
                  <NestedLoops>
                    <RelOp PhysicalOp="Clustered Index Scan" LogicalOp="Clustered Index Scan">
                      <RunTimeInformation><RunTimeCountersPerThread Thread="0" ActualLogicalReads="{reads}" ActualPhysicalReads="1" ActualReadAheads="2" ActualScans="1"/></RunTimeInformation>
                      <IndexScan><Object Database="[SqlExpertTeste]" Schema="[dbo]" Table="[Pedidos]" Index="[PK_Pedidos]" Alias="[p]"/></IndexScan>
                    </RelOp>
                    <RelOp PhysicalOp="Clustered Index Seek" LogicalOp="Clustered Index Seek">
                      <RunTimeInformation><RunTimeCountersPerThread Thread="0" ActualLogicalReads="10" ActualScans="0"/></RunTimeInformation>
                      <IndexScan><Object Schema="[dbo]" Table="[Clientes]" Index="[PK_Clientes]" Alias="[c]"/></IndexScan>
                    </RelOp>
                  </NestedLoops>
                </RelOp>
              </QueryPlan>
            </StmtSimple>
          </Statements></Batch></BatchSequence>
        </ShowPlanXML>
        """;

    [Fact]
    public void Showplan_SummarizesTimesReadsAndOperators()
    {
        var s = ShowplanSummary.Summarize(new[] { SamplePlan(), SamplePlan(elapsed: 7, cpu: 1, reads: 6) });
        Assert.Equal(2, s.Statements);
        Assert.Equal(10, s.PrepareMs);
        Assert.Equal(10, s.ExecuteMs);
        Assert.Equal(3, s.CpuMs);
        Assert.Equal(1234 + 10 + 6 + 10, s.LogicalReads);
        Assert.Equal(2, s.PhysicalReads);
        Assert.Equal(4, s.ReadAheads);
        Assert.Equal(2, s.Scans);
        Assert.Contains("SELECT * FROM dbo.Clientes c JOIN dbo.Pedidos p ON p.ClienteId = c.ClienteId", s.PlanLines);
        Assert.Contains("  Nested Loops (Inner Join)", s.PlanLines);
        Assert.Contains("    Clustered Index Scan  [dbo].[Pedidos].[PK_Pedidos] [p]", s.PlanLines);
        Assert.Contains("    Clustered Index Seek  [dbo].[Clientes].[PK_Clientes] [c]", s.PlanLines);
    }

    [Fact]
    public void Showplan_InvalidXmlIsIgnored()
    {
        var s = ShowplanSummary.Summarize(new[] { "<not xml", SamplePlan() });
        Assert.Equal(1, s.Statements);
    }

    [Fact]
    public void Showplan_FormatPtBr()
    {
        var s = ShowplanSummary.Summarize(new[] { SamplePlan(elapsed: 1500) });
        var (plan, perf) = ShowplanSummary.Format(s, 12345, TimeSpan.FromMilliseconds(1600.4));
        Assert.StartsWith("Plan" + Environment.NewLine, plan);
        var lines = perf.Split(Environment.NewLine);
        Assert.Equal("------ Performance info ------", lines[0]);
        Assert.Contains("Prepare time = 5ms", lines);
        Assert.Contains("Execute time = 1.500ms", lines);
        Assert.Contains("CPU time = 2ms", lines);
        Assert.Contains("Client time = 1.600ms", lines);
        Assert.Contains("Rows fetched = 12.345", lines);
        Assert.Contains("Logical reads (cache) = 1.244", lines);
        Assert.Contains("Statements = 1", lines);
    }

    [Fact]
    public void Showplan_FormatWithoutPlan()
    {
        var (plan, perf) = ShowplanSummary.Format(null, 0, TimeSpan.Zero);
        Assert.Contains("sem plano", plan);
        Assert.StartsWith("------ Performance info ------", perf);
        Assert.DoesNotContain("Logical reads", perf);
        Assert.Contains("Rows fetched = 0", perf);
    }

    [Fact]
    public void Showplan_ResultSetIsRecognizedByItsColumn()
    {
        Assert.True(ShowplanSummary.IsShowplanResult(new[] { "Microsoft SQL Server 2005 XML Showplan" }));
        Assert.False(ShowplanSummary.IsShowplanResult(new[] { "Microsoft SQL Server 2005 XML Showplan", "x" }));
        Assert.False(ShowplanSummary.IsShowplanResult(new[] { "Plan" }));
    }

    // ===== Catalog refresh after DDL =====

    [Theory]
    [InlineData("CREATE TABLE dbo.X (id int)", true)]
    [InlineData("create or alter procedure p as select 1", true)]
    [InlineData("DROP VIEW v", true)]
    [InlineData("EXEC sp_rename 'a', 'b'", true)]
    [InlineData("ALTER TABLE t ADD c int", true)]
    [InlineData("SELECT 'CREATE TABLE x' AS s -- DROP TABLE y", false)]
    [InlineData("INSERT INTO t VALUES (1)", false)]
    [InlineData("CREATE TABLE #tmp (id int)", false)]
    public void ChangesSchema(string sql, bool expected) => Assert.Equal(expected, SqlEditorStatus.ChangesSchema(sql));

    // ===== Layout and availability =====

    [Fact]
    public void Pages_ClassicOrder_UnimplementedGreyed()
    {
        Assert.Equal(new[] { "Edit", "Results", "History", "Plan Analyzer", "Performance Analysis", "Logs" }, CommandAvailability.SqlEditorPages.Select(p => p.Caption));
        foreach (var id in new[] { "sqleditor.page.edit", "sqleditor.page.results", "sqleditor.page.history", "sqleditor.panel.messages", "sqleditor.panel.query_columns", "sqleditor.code_completion", "sqleditor.object_links" })
            Assert.True(CommandAvailability.IsImplemented(id), id);
        foreach (var id in new[] { "sqleditor.page.plan_analyzer", "sqleditor.page.performance_analysis", "sqleditor.page.logs" })
            Assert.False(CommandAvailability.IsImplemented(id), id);
    }

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "SqlExpert.App");
    }

    private static XNamespace Wpf => "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void SqlEditorView_PagesAndBottomPanel_MatchAvailability()
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), "Views", "SqlEditorView.xaml"));
        var tabControls = doc.Descendants(Wpf + "TabControl").ToList();
        var pages = tabControls.Single(t => (string?)t.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "PageTabs")
            .Elements(Wpf + "TabItem").ToList();
        Assert.Equal(CommandAvailability.SqlEditorPages.Select(p => p.Caption), pages.Select(p => (string?)p.Attribute("Header")));
        Assert.Equal(CommandAvailability.SqlEditorPages.Select(p => p.Id), pages.Select(p => (string?)p.Attribute("Tag")));
        foreach (var page in pages.Where(p => !CommandAvailability.IsImplemented((string)p.Attribute("Tag")!)))
            Assert.Equal("False", (string?)page.Attribute("IsEnabled"));

        var bottom = tabControls.Single(t => (string?)t.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "BottomTabs")
            .Elements(Wpf + "TabItem").Select(t => (string?)t.Attribute("Header")).ToList();
        Assert.Equal(new[] { "Messages", "Query columns" }, bottom);

        var code = File.ReadAllText(Path.Combine(AppDir(), "Views", "SqlEditorView.xaml.cs"));
        Assert.Contains("new TableDataGrid", code);     // results use the table Data grid
        Assert.Contains("new SqlEditorAssist(", code);  // completion + object links
        Assert.Contains("new SqlEditorAssist(", File.ReadAllText(Path.Combine(AppDir(), "Views", "ProcedureEditorView.xaml.cs")));
    }

    [Fact]
    public void ObjectLinks_AreLightBlue_NotGreen()
    {
        var code = File.ReadAllText(Path.Combine(AppDir(), "Views", "SqlEditorAssist.cs"));
        var m = System.Text.RegularExpressions.Regex.Match(code, "LinkColor\\s*=\\s*Color\\.FromRgb\\(0x(?<r>[0-9A-F]{2}), 0x(?<g>[0-9A-F]{2}), 0x(?<b>[0-9A-F]{2})\\)");
        Assert.True(m.Success, "LinkColor not found");
        int C(string g) => int.Parse(m.Groups[g].Value, NumberStyles.HexNumber);
        Assert.True(C("b") > C("g") && C("b") > C("r"), "underline must be blue");
    }

    [Fact]
    public void TableDataGrid_HidesTopInResultMode()
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "TableDataGrid.xaml"));
        Assert.Contains("IsTableMode", xaml);
    }
}
