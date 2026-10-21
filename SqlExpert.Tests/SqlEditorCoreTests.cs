using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;
using SqlExpert.Core.SqlEditor;

namespace SqlExpert.Tests;

public class SqlBatchParserTests
{
    [Fact]
    public void Split_KeepsStartLines()
    {
        var script = "SELECT 1;\r\nGO\r\n\r\nSELECT 2\r\nFROM x;\r\ngo\r\nSELECT 3;";
        var b = SqlBatchParser.Split(script);
        Assert.Equal(3, b.Count);
        Assert.Equal(("SELECT 1;", 1), (b[0].Text, b[0].StartLine));
        Assert.Equal(4, b[1].StartLine); // blank line after GO is skipped
        Assert.Equal("SELECT 2\r\nFROM x;", b[1].Text);
        Assert.Equal(7, b[2].StartLine);
    }

    [Theory]
    [InlineData("GO 3", 3)]
    [InlineData("  go   12  ", 12)]
    [InlineData("GO -- fim do lote", 1)]
    public void Split_GoVariants(string goLine, int repeat)
    {
        var b = SqlBatchParser.Split($"PRINT 'x'\n{goLine}\nSELECT 1");
        Assert.Equal(2, b.Count);
        Assert.Equal(repeat, b[0].RepeatCount);
        Assert.Equal(1, b[1].RepeatCount);
    }

    [Fact]
    public void Split_IgnoresGoInsideBlockCommentAndString()
    {
        var script = "SELECT 1 /* comentário\nGO\nainda comentário */\nSELECT 'texto\nGO\nfim'\nGO\nSELECT 2";
        var b = SqlBatchParser.Split(script);
        Assert.Equal(2, b.Count);
        Assert.Contains("ainda comentário", b[0].Text);
        Assert.Contains("fim'", b[0].Text);
        Assert.Equal(8, b[1].StartLine);
    }

    [Fact]
    public void Split_NestedCommentsAndEscapedQuotes()
    {
        var script = "SELECT 'it''s' /* a /* b */ c\nGO\n*/\nGO\nSELECT [col]]x]";
        var b = SqlBatchParser.Split(script);
        Assert.Equal(2, b.Count);
        Assert.Equal("SELECT [col]]x]", b[1].Text);
    }

    [Fact]
    public void Split_GoPrefixWordsAreNotSeparators()
    {
        var b = SqlBatchParser.Split("SELECT 1\nGOTO fim\nGO_x\nfim:");
        Assert.Single(b);
    }

    [Theory]
    [InlineData("")]
    [InlineData("GO\n\n  \nGO")]
    [InlineData(null)]
    public void Split_EmptyScripts(string? script) => Assert.Empty(SqlBatchParser.Split(script));

    [Fact]
    public void ScriptExecutor_UsesSameSplitter()
    {
        Assert.Equal(new[] { "SELECT 1 /*\r\nGO\r\n*/", "SELECT 2" }, ScriptExecutor.SplitBatches("SELECT 1 /*\nGO\n*/\nGO\nSELECT 2"));
    }
}

public class SqlErrorLineMapperTests
{
    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(1, 1, 3, 3)]
    [InlineData(1, 5, 2, 6)]      // 2nd line of the batch that starts on line 5
    [InlineData(10, 5, 2, 15)]    // selection starting on editor line 10
    [InlineData(10, 1, 0, 10)]    // unknown line → batch start
    public void ToEditorLine_Maps(int textStart, int batchStart, int serverLine, int expected)
        => Assert.Equal(expected, SqlErrorLineMapper.ToEditorLine(textStart, batchStart, serverLine));

    [Fact]
    public void ErrorInsideProcedure_PointsToBatchStart()
        => Assert.Equal(7, SqlErrorLineMapper.ToEditorLine(3, 5, 40, "dbo.MinhaProc"));

    [Fact]
    public void Header_IsSsmsLike()
    {
        Assert.Equal("Msg 207, Level 16, State 1, Line 12", SqlErrorLineMapper.FormatHeader(207, 16, 1, 12, null));
        Assert.Equal("Msg 50000, Level 16, State 1, Procedure p, Line 4", SqlErrorLineMapper.FormatHeader(50000, 16, 1, 12, "p", 4));
    }
}

public class SqlTextAnalyzerTests
{
    [Theory]
    [InlineData("SELECT * FROM dbo.Clientes", true)]
    [InlineData("  -- comentário\nselect top 10 * from sys.objects", true)]
    [InlineData("WITH c AS (SELECT 1 AS x) SELECT * FROM c", true)]
    [InlineData("SELECT 'DELETE FROM x' AS texto, [update] FROM t", true)]
    [InlineData("SELECT * INTO #t FROM x", false)]
    [InlineData("UPDATE x SET a = 1", false)]
    [InlineData("SELECT 1\nGO\nDELETE FROM x", false)]
    [InlineData("EXEC sp_who", false)]
    [InlineData("SET NOCOUNT ON; SELECT 1", false)]
    [InlineData("", false)]
    [InlineData("/* só comentário */", false)]
    public void IsReadOnlyQuery(string sql, bool expected) => Assert.Equal(expected, SqlTextAnalyzer.IsReadOnlyQuery(sql));

    [Fact]
    public void Strip_KeepsLength()
    {
        var sql = "SELECT 'a--b' /* x\ny */ -- z\n[q]";
        var stripped = SqlTextAnalyzer.StripCommentsAndLiterals(sql);
        Assert.Equal(sql.Length, stripped.Length);
        Assert.DoesNotContain("a--b", stripped);
        Assert.Equal(2, stripped.Count(c => c == '\n'));
    }
}

public class QueryHistoryStoreTests
{
    private static QueryHistoryStore NewStore() =>
        new(Path.Combine(Path.GetTempPath(), "SqlExpert-tests", Guid.NewGuid().ToString("N"), "history"));

    private static QueryHistoryEntry E(string sql, bool ok = true) => new(sql, DateTimeOffset.Now, "SqlExpertTeste", 12, ok);

    [Fact]
    public void Add_MostRecentFirst_AndRoundTrips()
    {
        var store = NewStore();
        store.Add("p1", E("SELECT 1"));
        store.Add("p1", E("SELECT 2", ok: false));
        var list = new QueryHistoryStore(store.Directory).Load("p1");
        Assert.Equal(new[] { "SELECT 2", "SELECT 1" }, list.Select(e => e.Sql));
        Assert.False(list[0].Success);
        Assert.Equal("SqlExpertTeste", list[0].Database);
    }

    [Fact]
    public void Add_DuplicateMovesToTop()
    {
        var store = NewStore();
        store.Add("p", E("SELECT 1"));
        store.Add("p", E("SELECT 2"));
        var list = store.Add("p", E("  SELECT 1  "));
        Assert.Equal(new[] { "SELECT 1", "SELECT 2" }, list.Select(e => e.Sql));
    }

    [Fact]
    public void Add_KeepsLast100()
    {
        var store = NewStore();
        for (var i = 0; i < 130; i++) store.Add("p", E($"SELECT {i}"));
        var list = store.Load("p");
        Assert.Equal(QueryHistoryStore.MaxEntries, list.Count);
        Assert.Equal("SELECT 129", list[0].Sql);
        Assert.Equal("SELECT 30", list[^1].Sql);
    }

    [Fact]
    public void PerConnection_AndBlankIgnored()
    {
        var store = NewStore();
        store.Add("a", E("SELECT 'a'"));
        store.Add("b", E("SELECT 'b'"));
        store.Add("b", E("   "));
        Assert.Single(store.Load("a"));
        Assert.Single(store.Load("b"));
        Assert.NotEqual(store.PathFor("a"), store.PathFor("b"));
    }

    [Fact]
    public void PathFor_SanitizesProfileId()
    {
        var store = NewStore();
        var path = store.PathFor(@"..\x/y:z");
        Assert.Equal(store.Directory, Path.GetDirectoryName(path));
        Assert.Equal("___x_y_z.json", Path.GetFileName(path));
    }

    [Fact]
    public void CorruptFile_ReadsEmpty()
    {
        var store = NewStore();
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.PathFor("p"), "{ not json");
        Assert.Empty(store.Load("p"));
        Assert.Single(store.Add("p", E("SELECT 1")));
    }

    [Fact]
    public void Preview_FirstNonEmptyLine()
    {
        Assert.Equal("SELECT *", QueryHistoryStore.PreviewOf("\n\n   SELECT *  \nFROM t"));
        Assert.Equal(120, QueryHistoryStore.PreviewOf(new string('x', 300)).Length);
    }
}

public class SqlFileTextTests
{
    private static string Temp() => Path.Combine(Path.GetTempPath(), "SqlExpert-tests", Guid.NewGuid().ToString("N"), "s.sql");

    [Fact]
    public void Utf8Bom_RoundTrips()
    {
        var path = Temp();
        SqlFileText.Write(path, "SELECT N'ação'");
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        var (text, enc) = SqlFileText.Read(path);
        Assert.Equal("SELECT N'ação'", text);
        Assert.Equal("utf-8", enc.WebName);
    }

    [Fact]
    public void Utf16Bom_IsDetected()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("SELECT 'é'")).ToArray();
        var (text, enc) = SqlFileText.Decode(bytes);
        Assert.Equal("SELECT 'é'", text);
        Assert.Equal(Encoding.Unicode.WebName, enc.WebName);
    }

    [Fact]
    public void AnsiFile_FallsBackTo1252()
    {
        var bytes = new byte[] { (byte)'S', (byte)'e', (byte)'l', 0xE7, 0xE3, (byte)'o' }; // "Selção" in Windows-1252
        var (text, enc) = SqlFileText.Decode(bytes);
        Assert.Equal("Selção", text);
        Assert.Equal(1252, enc.CodePage);
    }

    [Fact]
    public void Utf8WithoutBom_StaysWithoutBom()
    {
        var (text, enc) = SqlFileText.Decode(Encoding.UTF8.GetBytes("SELECT 'ç'"));
        Assert.Equal("SELECT 'ç'", text);
        Assert.Empty(enc.GetPreamble());
    }
}

public class TextSearchTests
{
    [Fact]
    public void FindNext_WrapsAround()
    {
        const string t = "select a from t; SELECT b";
        Assert.Equal((17, 6), TextSearch.FindNext(t, "SELECT", 1, matchCase: false, wholeWord: false));
        Assert.Equal((0, 6), TextSearch.FindNext(t, "SELECT", 20, matchCase: false, wholeWord: false));
        Assert.Equal((17, 6), TextSearch.FindNext(t, "SELECT", 0, matchCase: true, wholeWord: false));
        Assert.Equal((-1, 0), TextSearch.FindNext(t, "zzz", 0, false, false));
    }

    [Fact]
    public void FindNext_Backward()
        => Assert.Equal((0, 6), TextSearch.FindNext("select x select", "select", 9, false, false, backward: true));

    [Fact]
    public void WholeWord_RespectsSqlIdentifiers()
    {
        const string t = "id, cliente_id, @id, id";
        Assert.Equal(2, TextSearch.Count(t, "id", false, wholeWord: true));
        Assert.Equal(4, TextSearch.Count(t, "id", false, wholeWord: false));
    }

    [Fact]
    public void ReplaceAll_IsLiteral()
    {
        var (text, count) = TextSearch.ReplaceAll("a.b a.b axb", "a.b", "$1", false, false);
        Assert.Equal("$1 $1 axb", text);
        Assert.Equal(2, count);
    }

    [Fact]
    public void IsMatch_ExactSelection()
    {
        Assert.True(TextSearch.IsMatch("SELECT", "select", false, false));
        Assert.False(TextSearch.IsMatch("SELECT", "select", true, false));
        Assert.False(TextSearch.IsMatch("SELECTX", "select", false, false));
    }
}

public class SqlEditorStatusTests
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    [Fact]
    public void Summarize_Success()
    {
        var r = new SqlExecutionResult { Elapsed = TimeSpan.FromMilliseconds(123), RowsAffected = 3, BatchCount = 1, BatchesExecuted = 1 };
        var set = new SqlResultSet { Columns = new[] { new SqlResultColumn("a", "int", typeof(int)) }, TotalRowCount = 1500 };
        for (var i = 0; i < 1000; i++) set.Rows.Add(new object?[] { i });
        r.ResultSets.Add(set);
        Assert.Equal("Concluído em 0,123 s · 1.000 registro(s) (há mais — Fetch all) · 3 linha(s) afetada(s)", SqlEditorStatus.Summarize(r, Br));
        Assert.Equal("Result 1 (1.000+)", SqlEditorStatus.ResultHeader(1, set, Br));
    }

    [Fact]
    public void Summarize_ErrorAndCancel()
    {
        var r = new SqlExecutionResult { Elapsed = TimeSpan.FromSeconds(2), BatchCount = 3, BatchesExecuted = 1 };
        r.Messages.Add(new SqlMessage(SqlMessageKind.Error, "x", 12));
        Assert.Equal("Erro (linha 12) após 2,000 s · 1/3 lotes", SqlEditorStatus.Summarize(r, Br));
        var c = new SqlExecutionResult { Elapsed = TimeSpan.FromSeconds(1), Cancelled = true };
        Assert.StartsWith("Cancelado após 1,000 s", SqlEditorStatus.Summarize(c, Br));
    }

    [Theory]
    [InlineData(true, 0, "Autocommit")]
    [InlineData(false, 0, "Transação explícita (abre na próxima execução)")]
    [InlineData(true, 2, "Transação ABERTA (@@TRANCOUNT = 2)")]
    public void TransactionText(bool autocommit, int tranCount, string expected)
        => Assert.Equal(expected, SqlEditorStatus.TransactionText(autocommit, tranCount));
}

public class SqlEditorSessionOfflineTests
{
    [Fact]
    public async Task ConnectionFailure_IsReportedAsMessage()
    {
        using var s = new SqlEditorSession(_ => throw new InvalidOperationException("servidor indisponível"), "db");
        var r = await s.ExecuteAsync(new SqlExecutionRequest { Sql = "SELECT 1" });
        Assert.True(r.HasErrors);
        Assert.Contains("servidor indisponível", r.Messages.Single().Text);
        Assert.False(s.IsBusy);
        Assert.False(s.InTransaction);
    }

    [Fact]
    public async Task CancelWhileConnecting_IsCancelled()
    {
        using var s = new SqlEditorSession(async ct => { await Task.Delay(5000, ct); throw new InvalidOperationException(); }, "db");
        using var cts = new CancellationTokenSource(100);
        var r = await s.ExecuteAsync(new SqlExecutionRequest { Sql = "SELECT 1" }, cts.Token);
        Assert.True(r.Cancelled);
        Assert.False(r.HasErrors);
    }

    [Fact]
    public async Task CommitWithoutConnection_IsNoop()
    {
        using var s = new SqlEditorSession(_ => throw new InvalidOperationException(), "db");
        var r = await s.CommitAsync();
        Assert.False(r.HasErrors);
        Assert.Equal(0, s.TranCount);
    }
}

public class SqlEditorSettingsTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "SqlExpert-tests", Guid.NewGuid().ToString("N"), "settings.json");

    [Fact]
    public void Defaults()
    {
        var s = new UserSettingsStore(TempPath()).Load();
        Assert.Equal(1000, s.SqlFetchRows);
        Assert.Equal(UserSettings.DefaultSqlEditorFontSize, s.SqlEditorFontSize);
    }

    [Fact]
    public void RoundTrip_AndClamp()
    {
        var store = new UserSettingsStore(TempPath());
        store.Save(new UserSettings { SqlFetchRows = 250, SqlEditorFontSize = 99 });
        var s = store.Load();
        Assert.Equal(250, s.SqlFetchRows);
        Assert.Equal(UserSettings.MaxDataGridFontSize, s.SqlEditorFontSize);
        Assert.Equal(1, UserSettings.ClampFetchRows(-5));
    }
}

public class TSqlXshdTests
{
    private static XDocument Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return XDocument.Load(Path.Combine(dir!.FullName, "SqlExpert.App", "Resources", "TSql.xshd"));
    }

    [Fact]
    public void Keywords_AreUniqueAndCoverBasics()
    {
        var words = Load().Descendants().Where(e => e.Name.LocalName == "Word").Select(e => e.Value.ToUpperInvariant()).ToList();
        Assert.Equal(words.Count, words.Distinct().Count());
        foreach (var w in new[] { "SELECT", "FROM", "WHERE", "INSERT", "BEGIN", "COMMIT", "NVARCHAR", "GETDATE" })
            Assert.Contains(w, words);
    }

    [Fact]
    public void AllRegexes_Compile()
    {
        var doc = Load();
        var patterns = doc.Descendants().Where(e => e.Name.LocalName == "Rule").Select(e => e.Value)
            .Concat(doc.Descendants().Where(e => e.Name.LocalName == "Span")
                .SelectMany(e => new[] { (string?)e.Attribute("begin"), (string?)e.Attribute("end") }))
            .Where(p => !string.IsNullOrEmpty(p)).ToList();
        Assert.NotEmpty(patterns);
        foreach (var p in patterns) _ = new Regex(p!);
        var colors = doc.Descendants().Where(e => e.Name.LocalName == "Color").Select(e => (string?)e.Attribute("name")).ToHashSet();
        foreach (var used in doc.Descendants().Select(e => (string?)e.Attribute("color")).Where(c => c is not null))
            Assert.Contains(used, colors);
    }
}
