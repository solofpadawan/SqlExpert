using System.Diagnostics;
using SqlExpert.Core.Explorer;
using Xunit.Abstractions;

namespace SqlExpert.Tests;

public class ExplorerFilterTests
{
    private readonly ITestOutputHelper _out;
    public ExplorerFilterTests(ITestOutputHelper output) => _out = output;

    private sealed class TreeBuilder
    {
        public readonly List<ExplorerFilterEntry> Entries = new();
        public int Container(int parent, string text) { Entries.Add(new(parent, text, 0, ExplorerFilterRole.Container)); return Entries.Count - 1; }
        public int Obj(int parent, string schema, string name) { Entries.Add(new(parent, schema + "." + name, schema.Length + 1, ExplorerFilterRole.Object)); return Entries.Count - 1; }
        public int Placeholder(int parent) { Entries.Add(new(parent, "(loading...)", 0, ExplorerFilterRole.Placeholder)); return Entries.Count - 1; }
    }

    // Demo (0) > Demo db (1) > Tables (2) > dbo.Clientes (3), dbo.Pedidos (4), vendas.Ação (5)
    //                        > Triggers (6) > dbo.trg_Clientes_Upd (7)
    //                        > Views (8) > placeholder (9)
    // SQL Server (10) (disconnected: alias only)
    private static (TreeBuilder t, ExplorerFilterIndex idx) Sample()
    {
        var t = new TreeBuilder();
        var reg = t.Container(-1, "Demo");
        var db = t.Container(reg, "Demo");
        var tables = t.Container(db, "Tables");
        t.Obj(tables, "dbo", "Clientes");
        t.Obj(tables, "dbo", "Pedidos");
        t.Obj(tables, "vendas", "Ação");
        var trg = t.Container(db, "Triggers");
        t.Obj(trg, "dbo", "trg_Clientes_Upd");
        var views = t.Container(db, "Views");
        t.Placeholder(views);
        t.Container(-1, "SQL Server");
        return (t, new ExplorerFilterIndex(t.Entries));
    }

    [Fact]
    public void EmptyQuery_ReturnsNull()
    {
        var (_, idx) = Sample();
        Assert.Null(idx.Match(""));
        Assert.Null(idx.Match("   "));
        Assert.Null(idx.Match(null));
    }

    [Fact]
    public void DeepMatch_KeepsAncestorsVisibleAndExpanded()
    {
        var (_, idx) = Sample();
        var r = idx.Match("CLIENT")!;
        Assert.True(r.IsVisible[3]);   // dbo.Clientes
        Assert.True(r.IsVisible[7]);   // trigger
        Assert.False(r.IsVisible[4]);  // dbo.Pedidos
        Assert.False(r.IsVisible[8]);  // Views (nothing below matches)
        Assert.False(r.IsVisible[10]); // SQL Server
        foreach (var anc in new[] { 0, 1, 2, 6 })
        {
            Assert.True(r.IsVisible[anc]);
            Assert.True(r.IsExpanded[anc]);
            Assert.False(r.IsCovered[anc]);
        }
        Assert.Equal(1, r.VisibleChildren[2]);
        Assert.Equal(1, r.VisibleChildren[6]);
        Assert.Equal(4, r.MatchStart[3]); // "dbo.|Clientes"
        Assert.Equal(6, r.MatchLength);
        Assert.Equal(2, r.MatchCount);
    }

    [Fact]
    public void AccentInsensitive_AndPositionsMapToCaption()
    {
        var (_, idx) = Sample();
        var r = idx.Match("acao")!;
        Assert.True(r.IsVisible[5]);
        Assert.Equal("vendas.".Length, r.MatchStart[5]);
        var r2 = idx.Match("AÇÃO")!;
        Assert.True(r2.IsVisible[5]);
    }

    [Fact]
    public void UnqualifiedQuery_IgnoresSchema_QualifiedUsesWholeName()
    {
        var (_, idx) = Sample();
        var r = idx.Match("dbo")!;
        Assert.False(r.IsVisible[3]);
        Assert.False(r.IsVisible[4]);
        var q = idx.Match("dbo.ped")!;
        Assert.True(q.IsVisible[4]);
        Assert.False(q.IsVisible[3]);
        Assert.Equal(0, q.MatchStart[4]);
        var b = idx.Match("[dbo].[Ped")!;
        Assert.True(b.IsVisible[4]);
    }

    [Fact]
    public void MatchedContainer_ShowsWholeSubtreeUnfiltered()
    {
        var (_, idx) = Sample();
        var r = idx.Match("tables")!;
        Assert.True(r.IsCovered[2]);
        Assert.True(r.IsVisible[3] && r.IsVisible[4] && r.IsVisible[5]);
        Assert.False(r.IsExpanded[2]); // the folder itself is not auto-expanded
        Assert.True(r.IsExpanded[1]);
        Assert.False(r.IsVisible[6]);

        var v = idx.Match("view")!;
        Assert.True(v.IsVisible[9]); // placeholder under a matched folder
        Assert.Equal(-1, v.MatchStart[9]);
        Assert.Equal(0, v.VisibleChildren[8]); // placeholders never count
    }

    [Fact]
    public void RegistrationAlias_Matches()
    {
        var (_, idx) = Sample();
        var r = idx.Match("sql s")!;
        Assert.True(r.IsVisible[10]);
        Assert.False(r.IsVisible[0]);
        Assert.Equal(1, r.MatchCount);
    }

    [Fact]
    public void Placeholder_NeverMatches()
    {
        var (_, idx) = Sample();
        var r = idx.Match("loading")!;
        Assert.False(r.IsVisible[9]);
        Assert.Equal(0, r.MatchCount);
    }

    [Fact]
    public void ParentAfterChild_Throws() =>
        Assert.Throws<ArgumentException>(() => new ExplorerFilterIndex(new[] { new ExplorerFilterEntry(0, "x", 0, ExplorerFilterRole.Container) }));

    [Fact]
    public void Cancellation_IsHonored()
    {
        var idx = new ExplorerFilterIndex(Synthetic(3000));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => idx.Match("a", cts.Token));
    }

    [Theory]
    [InlineData(505, null, " (505)")]
    [InlineData(505, 3, " (3/505)")]
    [InlineData(null, 2, " (2)")]
    public void FilteredCountText(int? count, int? filtered, string expected) =>
        Assert.Equal(expected, ExplorerNodeText.CountText(count, filtered));

    [Fact]
    public void Fold_KeepsLength()
    {
        foreach (var s in new[] { "Ação", "ÉLAN", "straße", "x\uD83D\uDE00y", "Ⅻ" })
            Assert.Equal(s.Length, ExplorerFilterText.Fold(s).Length);
        Assert.Equal("acao", ExplorerFilterText.Fold("Ação"));
    }

    /// <summary>~n nodes shaped like a big real server: 3 registrations, 4 databases, 9 folders each, objects spread over them.</summary>
    private static List<ExplorerFilterEntry> Synthetic(int n)
    {
        var t = new TreeBuilder();
        var folders = new[] { "Domains", "Tables", "Views", "Procedures", "Functions", "Triggers", "Sequences", "Roles", "Synonyms" };
        var rnd = new Random(42);
        var schemas = new[] { "dbo", "vendas", "estoque", "fiscal" };
        var words = new[] { "Cliente", "Pedido", "Item", "Produto", "Nota", "Fiscal", "Estoque", "Movimento", "Usuário", "Ação", "Log", "Config" };
        t.Container(-1, "SQL Server");
        t.Container(-1, "Homologação");
        var reg = t.Container(-1, "Demo");
        var perDb = (n - 3) / 4;
        for (var d = 0; d < 4; d++)
        {
            var db = t.Container(reg, "DB_" + d);
            var perFolder = (perDb - 1 - folders.Length) / folders.Length;
            foreach (var f in folders)
            {
                var fo = t.Container(db, f);
                for (var k = 0; k < perFolder; k++)
                    t.Obj(fo, schemas[rnd.Next(schemas.Length)], $"{words[rnd.Next(words.Length)]}_{words[rnd.Next(words.Length)]}_{k}");
            }
        }
        while (t.Entries.Count < n) t.Obj(reg + 1, "dbo", "Extra_" + t.Entries.Count);
        return t.Entries;
    }

    [Fact]
    public void Benchmark_5000Nodes_IsFast()
    {
        var entries = Synthetic(5000);
        Assert.Equal(5000, entries.Count);
        var queries = new[] { "c", "cl", "cli", "clie", "client", "cliente_p", "acao", "dbo.ped", "zzz", "fiscal" };

        // Warm-up (JIT, fold table).
        new ExplorerFilterIndex(entries).Match("x");

        const int rounds = 20;
        var sw = Stopwatch.StartNew();
        ExplorerFilterIndex? idx = null;
        for (var i = 0; i < rounds; i++) idx = new ExplorerFilterIndex(entries);
        var buildMs = sw.Elapsed.TotalMilliseconds / rounds;

        sw.Restart();
        var worst = 0.0;
        foreach (var q in queries)
        {
            var one = Stopwatch.StartNew();
            for (var i = 0; i < rounds; i++) Assert.NotNull(idx!.Match(q));
            worst = Math.Max(worst, one.Elapsed.TotalMilliseconds / rounds);
        }
        var avgMs = sw.Elapsed.TotalMilliseconds / (rounds * queries.Length);

        _out.WriteLine($"5000 nodes: build {buildMs:F3} ms, match avg {avgMs:F3} ms, worst query {worst:F3} ms");
        // Generous ceilings (CI machines are slow); real numbers are ~100x lower.
        Assert.True(buildMs < 100, $"index build took {buildMs:F2} ms");
        Assert.True(worst < 50, $"match took {worst:F2} ms");
    }
}
