using SqlExpert.Core.Services;
using SqlExpert.Core.SqlEditor;

namespace SqlExpert.Tests;

/// <summary>Code completion (Ctrl+Space) and object links of the SQL Editor: prefix/context parsing and name matching.</summary>
public class SqlCompletionTests
{
    private static CompletionCatalog Catalog() => new("SqlExpertTeste",
        new[]
        {
            new CatalogObject("dbo", "Clientes", CompletionKind.Table),
            new CatalogObject("dbo", "Categorias", CompletionKind.Table),
            new CatalogObject("dbo", "Pedidos", CompletionKind.Table),
            new CatalogObject("dbo", "vwPedidosResumo", CompletionKind.View),
            new CatalogObject("dbo", "spPedidosPorCliente", CompletionKind.Procedure),
            new CatalogObject("dbo", "fnTotal", CompletionKind.Function),
            new CatalogObject("dbo", "trClientes", CompletionKind.Trigger),
            new CatalogObject("dbo", "seqNumero", CompletionKind.Sequence),
            new CatalogObject("dbo", "Order Details", CompletionKind.Table),
            new CatalogObject("vendas", "Clientes", CompletionKind.Table),
            new CatalogObject("vendas", "Metas", CompletionKind.Table),
        },
        new[]
        {
            new CatalogColumn("dbo", "Clientes", "ClienteId", "int", 1, false),
            new CatalogColumn("dbo", "Clientes", "Nome", "nvarchar(100)", 2, false),
            new CatalogColumn("dbo", "Clientes", "Email", "nvarchar(150)", 3),
            new CatalogColumn("dbo", "Pedidos", "PedidoId", "int", 1, false),
            new CatalogColumn("dbo", "Pedidos", "ClienteId", "int", 2, false),
            new CatalogColumn("dbo", "Pedidos", "Data", "datetime2", 3, false),
            new CatalogColumn("vendas", "Metas", "Valor", "decimal(15,2)", 1),
        },
        new[] { "dbo", "vendas", "rh" });

    // ===== Context (prefix / qualifier) =====

    [Theory]
    [InlineData("SELECT * FROM Cli|", "Cli", "")]
    [InlineData("SELECT * FROM |", "", "")]
    [InlineData("SELECT c.No| FROM Clientes c", "No", "c")]
    [InlineData("SELECT * FROM dbo.|", "", "dbo")]
    [InlineData("SELECT * FROM [dbo].[Cl|", "Cl", "dbo")]
    [InlineData("SELECT * FROM x.dbo.Pe|", "Pe", "x.dbo")]
    public void Analyze_PrefixAndQualifier(string marked, string prefix, string qualifier)
    {
        var caret = marked.IndexOf('|');
        var text = marked.Remove(caret, 1);
        var ctx = SqlCompletion.Analyze(text, caret);
        Assert.NotNull(ctx);
        Assert.Equal(prefix, ctx!.Prefix);
        Assert.Equal(qualifier, string.Join(".", ctx.Qualifier));
        // A bracketed word is replaced from its "[" (the item is inserted as [name]).
        Assert.Equal(caret - prefix.Length - (ctx.Bracketed ? 1 : 0), ctx.WordStart);
    }

    [Theory]
    [InlineData("SELECT 1 -- Cli|")]
    [InlineData("SELECT 1 /* Cli|")]
    [InlineData("SELECT 'Cli|")]
    [InlineData("SELECT N'abc Cli|")]
    [InlineData("DECLARE @Cli|")]
    public void Analyze_NoCompletionInCommentsStringsOrVariables(string marked)
    {
        var caret = marked.IndexOf('|');
        Assert.Null(SqlCompletion.Analyze(marked.Remove(caret, 1), caret));
    }

    [Fact]
    public void Analyze_Bracketed()
    {
        const string text = "SELECT * FROM [Ord";
        var ctx = SqlCompletion.Analyze(text, text.Length);
        Assert.NotNull(ctx);
        Assert.True(ctx!.Bracketed);
        Assert.Equal("Ord", ctx.Prefix);
    }

    // ===== Table references and aliases =====

    [Fact]
    public void TableReferences_AliasesJoinsAndCommaLists()
    {
        const string sql = "SELECT * FROM dbo.Clientes AS c INNER JOIN Pedidos p ON p.ClienteId = c.ClienteId, vendas.Metas m WHERE 1=1";
        var refs = SqlCompletion.TableReferences(SqlNameScanner.Tokenize(sql));
        Assert.Contains(refs, r => r.Name == "Clientes" && r.Schema == "dbo" && r.Alias == "c");
        Assert.Contains(refs, r => r.Name == "Pedidos" && r.Schema is null && r.Alias == "p");
        Assert.Contains(refs, r => r.Name == "Metas" && r.Schema == "vendas" && r.Alias == "m");
        Assert.DoesNotContain(refs, r => r.Name is "ON" or "WHERE");
    }

    [Fact]
    public void TableReferences_UpdateInsertAndTempTables()
    {
        var refs = SqlCompletion.TableReferences(SqlNameScanner.Tokenize("UPDATE Clientes SET Nome = 'x'; INSERT INTO #t SELECT 1; DELETE FROM Pedidos WHERE 1=0"));
        Assert.Contains(refs, r => r.Name == "Clientes");
        Assert.Contains(refs, r => r.Name == "Pedidos");
        Assert.DoesNotContain(refs, r => r.Name.StartsWith('#'));
    }

    // ===== Items =====

    private static List<SqlCompletionItem> Items(string marked)
    {
        var caret = marked.IndexOf('|');
        return SqlCompletion.Complete(marked.Remove(caret, 1), caret, Catalog()).Items;
    }

    [Fact]
    public void Items_OnlyNamesStartingWithPrefix_CaseInsensitive_ObjectsBeforeKeywords()
    {
        var items = Items("SELECT * FROM cl|");
        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.StartsWith("cl", i.DisplayName.Contains('.') ? i.DisplayName[(i.DisplayName.IndexOf('.') + 1)..] : i.DisplayName, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, i => i.Kind == CompletionKind.Table && i.DisplayName == "Clientes");
        Assert.Contains(items, i => i.Kind == CompletionKind.Table && i.DisplayName == "vendas.Clientes" && i.InsertText == "vendas.Clientes");
        Assert.DoesNotContain(items, i => i.DisplayName == "Categorias");
        var firstKeyword = items.FindIndex(i => i.Kind == CompletionKind.Keyword);
        if (firstKeyword >= 0) Assert.All(items.Take(firstKeyword), i => Assert.NotEqual(CompletionKind.Keyword, i.Kind));
    }

    [Fact]
    public void Items_EmptyPrefix_ListsObjectKindsSorted()
    {
        var items = Items("SELECT * FROM |");
        var objects = items.Where(i => i.Kind != CompletionKind.Keyword && i.Kind != CompletionKind.Schema).ToList();
        Assert.Contains(objects, i => i.Kind == CompletionKind.View && i.DisplayName == "vwPedidosResumo");
        Assert.Contains(objects, i => i.Kind == CompletionKind.Procedure);
        Assert.Contains(objects, i => i.Kind == CompletionKind.Sequence);
        var ranks = objects.Select(i => CompletionCatalog.KindRank(i.Kind)).ToList();
        Assert.Equal(ranks.OrderBy(r => r), ranks);
        Assert.DoesNotContain(objects, i => i.Kind == CompletionKind.Trigger); // triggers only with a prefix
        Assert.Contains(Items("SELECT * FROM tr|"), i => i.Kind == CompletionKind.Trigger && i.DisplayName == "trClientes");
    }

    [Fact]
    public void Items_AfterAlias_ListsColumnsOfThatTable()
    {
        var items = Items("SELECT c.| FROM Clientes c JOIN Pedidos p ON p.ClienteId = c.ClienteId");
        Assert.Equal(new[] { "ClienteId", "Nome", "Email" }, items.Select(i => i.DisplayName));
        Assert.All(items, i => Assert.Equal(CompletionKind.Field, i.Kind));
        Assert.Equal("nvarchar(100)", items[1].Detail);

        var filtered = Items("SELECT p.Da| FROM Clientes c JOIN Pedidos p ON p.ClienteId = c.ClienteId");
        Assert.Equal("Data", Assert.Single(filtered).DisplayName);
    }

    [Fact]
    public void Items_AfterTableNameOrSchema()
    {
        Assert.Contains(Items("SELECT Clientes.E| FROM Clientes"), i => i.DisplayName == "Email");
        var vendas = Items("SELECT * FROM vendas.|");
        Assert.Contains(vendas, i => i.DisplayName == "Metas" && i.InsertText == "Metas");
        Assert.DoesNotContain(vendas, i => i.DisplayName == "Pedidos");
    }

    [Fact]
    public void Items_QuoteNamesThatNeedBrackets()
    {
        Assert.Contains(Items("SELECT * FROM Or|"), i => i.DisplayName == "Order Details" && i.InsertText == "[Order Details]");
        Assert.Equal("Clientes", SqlCompletion.Quote("Clientes"));
        Assert.Equal("[Order Details]", SqlCompletion.Quote("Order Details"));
        Assert.Equal("[select]", SqlCompletion.Quote("select"));
        Assert.Equal("[a]]b]", SqlCompletion.Quote("a]b"));
        Assert.Equal("[1abc]", SqlCompletion.Quote("1abc"));
    }

    [Fact]
    public void Items_KeywordsByPrefix()
    {
        var items = Items("SEL|");
        Assert.Contains(items, i => i.Kind == CompletionKind.Keyword && i.InsertText == "SELECT");
    }

    [Fact]
    public void Items_NoneInsideComment()
    {
        Assert.Empty(Items("-- Cl|"));
    }

    // ===== Catalog =====

    [Fact]
    public void Catalog_ResolvePrefersDefaultSchema_AndReportsAmbiguity()
    {
        var cat = Catalog();
        Assert.Equal("dbo", cat.Resolve("clientes", null)!.Schema);
        Assert.Equal("vendas", cat.Resolve("Clientes", "vendas")!.Schema);
        Assert.Equal("vendas", cat.Resolve("Metas", null)!.Schema); // only one
        Assert.Null(cat.Resolve("Clientes", null, "rh"));       // two candidates, none in the default schema
        Assert.True(cat.IsSchema("RH"));
        Assert.Equal(3, cat.ColumnsOf("dbo", "CLIENTES").Count);
    }

    [Theory]
    [InlineData("U", CompletionKind.Table)]
    [InlineData("V ", CompletionKind.View)]
    [InlineData("P", CompletionKind.Procedure)]
    [InlineData("IF", CompletionKind.Function)]
    [InlineData("TR", CompletionKind.Trigger)]
    [InlineData("SO", CompletionKind.Sequence)]
    [InlineData("SN", CompletionKind.Synonym)]
    [InlineData("TY", CompletionKind.Type)]
    public void Loader_KindOf(string type, CompletionKind kind) => Assert.Equal(kind, CompletionCatalogLoader.KindOf(type));

    [Theory]
    [InlineData("nvarchar", -1, 0, 0, "nvarchar(max)")]
    [InlineData("nvarchar", 200, 0, 0, "nvarchar(100)")]
    [InlineData("varchar", 50, 0, 0, "varchar(50)")]
    [InlineData("decimal", 9, 15, 2, "decimal(15,2)")]
    [InlineData("int", 4, 10, 0, "int")]
    [InlineData("datetime2", 8, 27, 7, "datetime2")]
    [InlineData("datetime2", 7, 23, 3, "datetime2(3)")]
    public void Loader_FormatType(string type, int maxLength, int precision, int scale, string expected) =>
        Assert.Equal(expected, CompletionCatalogLoader.FormatType(type, maxLength, precision, scale));

    // ===== Object links =====

    [Fact]
    public void Links_MatchKnownObjects_SkipCommentsStringsAliasesAndTemp()
    {
        const string sql = "-- Clientes\nSELECT 'Pedidos', c.Nome FROM dbo.Clientes c JOIN Pedidos AS Categorias ON 1=1 JOIN #Clientes t ON 1=1\nEXEC spPedidosPorCliente 1";
        var links = SqlObjectLinks.Find(sql, Catalog());
        var texts = links.Select(l => sql.Substring(l.Start, l.Length)).ToList();
        Assert.Equal(new[] { "dbo.Clientes", "Pedidos", "spPedidosPorCliente" }, texts);
        Assert.Equal(CompletionKind.Procedure, links[2].Target.Kind);
        Assert.Equal("dbo", links[0].Target.Schema);
    }

    [Fact]
    public void Links_ColumnAfterDotIsNotAnObject_AndSchemaQualifiedResolves()
    {
        const string sql = "SELECT x.Clientes FROM vendas.Clientes x, [vendas].[Metas]";
        var links = SqlObjectLinks.Find(sql, Catalog());
        Assert.Equal(2, links.Count);
        Assert.Equal("vendas.Clientes", sql.Substring(links[0].Start, links[0].Length));
        Assert.Equal("vendas", links[0].Target.Schema);
        Assert.Equal("Metas", links[1].Target.Name);
    }

    [Fact]
    public void Links_ThreePartOnlyForCurrentDatabase()
    {
        var links = SqlObjectLinks.Find("SELECT * FROM SqlExpertTeste.dbo.Clientes, OutraBase.dbo.Pedidos", Catalog());
        Assert.Single(links);
        Assert.Equal("Clientes", links[0].Target.Name);
    }

    [Fact]
    public void Links_At_BinarySearch()
    {
        const string sql = "SELECT * FROM Clientes JOIN Pedidos ON 1=1";
        var links = SqlObjectLinks.Find(sql, Catalog());
        Assert.Equal("Clientes", SqlObjectLinks.At(links, sql.IndexOf("Clientes") + 3)!.Target.Name);
        Assert.Equal("Pedidos", SqlObjectLinks.At(links, sql.IndexOf("Pedidos"))!.Target.Name);
        Assert.Null(SqlObjectLinks.At(links, 2));
    }

    [Fact]
    public void Links_CaseInsensitive()
    {
        var links = SqlObjectLinks.Find("select * from CLIENTES", Catalog());
        Assert.Equal("Clientes", Assert.Single(links).Target.Name);
    }
}
