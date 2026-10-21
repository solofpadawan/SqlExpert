using SqlExpert.Core.Documents;

namespace SqlExpert.Tests;

public class DocumentManagerTests
{
    private sealed class Doc
    {
        public Doc(string key) => Key = key;
        public string Key { get; }
    }

    private static DocumentManager<Doc> Make() => new(d => d.Key);

    [Fact]
    public void Open_AddsAndActivates()
    {
        var m = Make();
        var (doc, created) = m.OpenOrActivate("a", () => new Doc("a"));
        Assert.True(created);
        Assert.Same(doc, m.Active);
        Assert.Single(m.Documents);
    }

    [Fact]
    public void Open_ExistingKey_ActivatesWithoutDuplicating_CaseInsensitive()
    {
        var m = Make();
        var (a, _) = m.OpenOrActivate("a", () => new Doc("a"));
        m.OpenOrActivate("b", () => new Doc("b"));
        var factoryCalled = false;
        var (again, created) = m.OpenOrActivate("A", () => { factoryCalled = true; return new Doc("A"); });
        Assert.False(created);
        Assert.False(factoryCalled);
        Assert.Same(a, again);
        Assert.Same(a, m.Active);
        Assert.Equal(2, m.Count);
    }

    [Fact]
    public void ActiveChanged_ReportsPreviousAndCurrent()
    {
        var m = Make();
        var events = new List<(Doc? Old, Doc? New)>();
        m.ActiveChanged += (o, n) => events.Add((o, n));
        var (a, _) = m.OpenOrActivate("a", () => new Doc("a"));
        var (b, _) = m.OpenOrActivate("b", () => new Doc("b"));
        m.Activate(b); // no-op
        Assert.Equal(new (Doc?, Doc?)[] { (null, a), (a, b) }, events);
    }

    [Fact]
    public void RemoveActive_ActivatesLeftNeighbour_ThenFirst_ThenNone()
    {
        var m = Make();
        var (a, _) = m.OpenOrActivate("a", () => new Doc("a"));
        var (b, _) = m.OpenOrActivate("b", () => new Doc("b"));
        var (c, _) = m.OpenOrActivate("c", () => new Doc("c"));

        m.Activate(b);
        Assert.True(m.Remove(b));
        Assert.Same(a, m.Active);

        Assert.True(m.Remove(a));
        Assert.Same(c, m.Active);

        Assert.True(m.Remove(c));
        Assert.Null(m.Active);
        Assert.Empty(m.Documents);
        Assert.False(m.Remove(c));
    }

    [Fact]
    public void RemoveInactive_KeepsActive()
    {
        var m = Make();
        var (a, _) = m.OpenOrActivate("a", () => new Doc("a"));
        var (b, _) = m.OpenOrActivate("b", () => new Doc("b"));
        m.Remove(a);
        Assert.Same(b, m.Active);
    }

    [Fact]
    public void Others_ExcludesKept_AndIsSnapshot()
    {
        var m = Make();
        var (a, _) = m.OpenOrActivate("a", () => new Doc("a"));
        var (b, _) = m.OpenOrActivate("b", () => new Doc("b"));
        var (c, _) = m.OpenOrActivate("c", () => new Doc("c"));
        var others = m.Others(b);
        Assert.Equal(new[] { a, c }, others);
        foreach (var d in others) m.Remove(d);
        Assert.Equal(new[] { b }, m.Documents);
        Assert.Same(b, m.Active);
    }

    [Fact]
    public void Cycle_WrapsBothWays()
    {
        var m = Make();
        var (a, _) = m.OpenOrActivate("a", () => new Doc("a"));
        var (b, _) = m.OpenOrActivate("b", () => new Doc("b"));
        var (c, _) = m.OpenOrActivate("c", () => new Doc("c"));
        Assert.Same(a, m.Cycle(1));
        Assert.Same(c, m.Cycle(-1));
        Assert.Same(b, m.Cycle(-1));
        Assert.Null(Make().Cycle(1));
    }

    [Fact]
    public void Activate_UnknownDocument_Throws()
        => Assert.Throws<InvalidOperationException>(() => Make().Activate(new Doc("x")));

    [Fact]
    public void TableKey_IsCaseInsensitiveAndDistinct()
    {
        Assert.Equal(DocumentKeys.Table("P1", "SqlExpertTeste", "dbo", "Clientes"),
                     DocumentKeys.Table("p1", "sqlexpertteste", "DBO", "CLIENTES"));
        Assert.NotEqual(DocumentKeys.Table("p1", "db", "dbo", "A"), DocumentKeys.Table("p1", "db", "sales", "A"));
        Assert.NotEqual(DocumentKeys.Table("p1", "db1", "dbo", "A"), DocumentKeys.Table("p1", "db2", "dbo", "A"));
    }

    [Fact]
    public void WindowTitle_WithDocument_IsClassicStyle()
        => Assert.Equal(
            @"SqlExpert - [Table : [dbo].[Clientes]] : SQL Server (localhost\SQLEXPRESS:SqlExpertTeste)",
            WindowTitles.Build(WindowTitles.TableCaption("dbo", "Clientes"), "SQL Server", "localhost", "SQLEXPRESS", "SqlExpertTeste"));

    [Fact]
    public void WindowTitle_WithoutInstance_UsesServerOnly()
        => Assert.Equal(
            "SqlExpert - [Table : [dbo].[X]] : Prod (srv01:Vendas)",
            WindowTitles.Build("Table : [dbo].[X]", "Prod", "srv01", null, "Vendas"));

    [Fact]
    public void WindowTitle_WithoutDocument_IsAppName()
        => Assert.Equal("SqlExpert", WindowTitles.Build(null, "SQL Server", "localhost", "SQLEXPRESS", "db"));
}
