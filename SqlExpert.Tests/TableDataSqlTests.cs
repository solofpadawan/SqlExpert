using System.Data;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.Tests;

public class TableDataSqlTests
{
    private static TableDataSchema Clientes() => new()
    {
        Schema = "dbo",
        Table = "Clientes",
        Columns = new[]
        {
            new DataColumnInfo { Ordinal = 1, Name = "ClienteId", TypeName = "int", BaseTypeName = "int", IsIdentity = true, IsPrimaryKey = true, KeyOrdinal = 1 },
            new DataColumnInfo { Ordinal = 2, Name = "Nome", TypeName = "nvarchar", BaseTypeName = "nvarchar" },
            new DataColumnInfo { Ordinal = 3, Name = "Email", TypeName = "nvarchar", BaseTypeName = "nvarchar", IsNullable = true },
            new DataColumnInfo { Ordinal = 4, Name = "Total", TypeName = "decimal", BaseTypeName = "decimal", IsComputed = true },
            new DataColumnInfo { Ordinal = 5, Name = "Versao", TypeName = "timestamp", BaseTypeName = "timestamp", IsRowVersion = true }
        }
    };

    private static TableDataSchema ItensPedido() => new()
    {
        Schema = "dbo",
        Table = "ItensPedido",
        Columns = new[]
        {
            new DataColumnInfo { Ordinal = 1, Name = "PedidoId", BaseTypeName = "int", IsPrimaryKey = true, KeyOrdinal = 1 },
            new DataColumnInfo { Ordinal = 2, Name = "ProdutoId", BaseTypeName = "int", IsPrimaryKey = true, KeyOrdinal = 2 },
            new DataColumnInfo { Ordinal = 3, Name = "Quantidade", BaseTypeName = "int" }
        }
    };

    [Theory]
    [InlineData("Nome", "[Nome]")]
    [InlineData("Minha Coluna", "[Minha Coluna]")]
    [InlineData("a]b", "[a]]b]")]
    [InlineData("x]]", "[x]]]]]")]
    public void Quote_EscapesClosingBracket(string input, string expected)
        => Assert.Equal(expected, TableDataSql.Quote(input));

    [Fact]
    public void QualifiedName_QuotesSchemaAndTable()
        => Assert.Equal("[my]]schema].[Tab ela]", TableDataSql.QualifiedName("my]schema", "Tab ela"));

    [Fact]
    public void SelectTop_IsParameterized_AndOrdersByPk()
    {
        var sql = TableDataSql.BuildSelectTop(ItensPedido());
        Assert.Equal("SELECT TOP (@top) [PedidoId], [ProdutoId], [Quantidade] FROM [dbo].[ItensPedido] ORDER BY [PedidoId], [ProdutoId];", sql);
    }

    [Fact]
    public void SelectTop_WithoutPk_HasNoOrderBy()
    {
        var s = ItensPedido();
        foreach (var c in s.Columns) { c.IsPrimaryKey = false; c.KeyOrdinal = 0; }
        Assert.DoesNotContain("ORDER BY", TableDataSql.BuildSelectTop(s));
    }

    [Fact]
    public void SelectList_ReadsClrTypesAsText()
    {
        var s = new TableDataSchema
        {
            Table = "Lugares",
            Columns = new[] { new DataColumnInfo { Ordinal = 1, Name = "Geo", TypeName = "geography", IsClrType = true } }
        };
        Assert.Equal("[Geo].ToString() AS [Geo]", TableDataSql.SelectList(s));
        Assert.False(s.Columns[0].IsWritable);
    }

    [Fact]
    public void Insert_SkipsIdentityComputedRowVersionAndNulls_ThenFetchesByScopeIdentity()
    {
        var stmt = TableDataSql.BuildInsert(Clientes(), new Dictionary<string, object?>
        {
            ["ClienteId"] = 99,
            ["Nome"] = "Ana'; DROP TABLE x;--",
            ["Email"] = DBNull.Value,
            ["Total"] = 10m,
            ["Versao"] = new byte[] { 1 }
        });

        Assert.StartsWith("INSERT INTO [dbo].[Clientes] ([Nome]) VALUES (@p0);", stmt.Sql);
        Assert.Contains("WHERE [ClienteId] = SCOPE_IDENTITY();", stmt.Sql);
        Assert.DoesNotContain("DROP", stmt.Sql);
        Assert.True(stmt.ReturnsRow);
        var p = Assert.Single(stmt.Parameters);
        Assert.Equal("@p0", p.Name);
        Assert.Equal("Ana'; DROP TABLE x;--", p.Value);
        Assert.Equal("Nome", p.Column.Name);
    }

    [Fact]
    public void Insert_WithNothingWritable_UsesDefaultValues()
    {
        var stmt = TableDataSql.BuildInsert(Clientes(), new Dictionary<string, object?> { ["ClienteId"] = 1 });
        Assert.StartsWith("INSERT INTO [dbo].[Clientes] DEFAULT VALUES;", stmt.Sql);
        Assert.Empty(stmt.Parameters);
    }

    [Fact]
    public void Insert_WithoutIdentity_FetchesBySuppliedPk()
    {
        var stmt = TableDataSql.BuildInsert(ItensPedido(), new Dictionary<string, object?>
        {
            ["PedidoId"] = 1, ["ProdutoId"] = 2, ["Quantidade"] = 3
        });
        Assert.Equal(
            "INSERT INTO [dbo].[ItensPedido] ([PedidoId], [ProdutoId], [Quantidade]) VALUES (@p0, @p1, @p2); " +
            "SELECT [PedidoId], [ProdutoId], [Quantidade] FROM [dbo].[ItensPedido] WHERE [PedidoId] = @k0 AND [ProdutoId] = @k1;",
            stmt.Sql);
        Assert.Equal(5, stmt.Parameters.Count);
        Assert.True(stmt.ReturnsRow);
    }

    [Fact]
    public void Insert_WithoutIdentityAndMissingPk_DoesNotFetch()
    {
        var stmt = TableDataSql.BuildInsert(ItensPedido(), new Dictionary<string, object?> { ["PedidoId"] = 1, ["Quantidade"] = 3 });
        Assert.False(stmt.ReturnsRow);
        Assert.DoesNotContain("SELECT", stmt.Sql);
    }

    [Fact]
    public void Update_SetsOnlyWritableChangedColumns_KeyedByOriginalPk()
    {
        var stmt = TableDataSql.BuildUpdate(Clientes(),
            new Dictionary<string, object?> { ["Nome"] = "Bia", ["Email"] = DBNull.Value, ["Total"] = 5m },
            new Dictionary<string, object?> { ["ClienteId"] = 7 });

        Assert.NotNull(stmt);
        Assert.StartsWith("UPDATE [dbo].[Clientes] SET [Nome] = @p0, [Email] = @p1 WHERE [ClienteId] = @k0;", stmt!.Sql);
        Assert.Contains("IF @@ROWCOUNT > 0 SELECT", stmt.Sql);
        Assert.Equal("Bia", stmt.Parameters.Single(p => p.Name == "@p0").Value);
        Assert.Null(stmt.Parameters.Single(p => p.Name == "@p1").Value);
        Assert.Equal(7, stmt.Parameters.Single(p => p.Name == "@k0").Value);
        Assert.Equal(7, stmt.Parameters.Single(p => p.Name == "@n0").Value);
    }

    [Fact]
    public void Update_OfPkColumn_ReselectsByNewKey()
    {
        var stmt = TableDataSql.BuildUpdate(ItensPedido(),
            new Dictionary<string, object?> { ["ProdutoId"] = 5 },
            new Dictionary<string, object?> { ["PedidoId"] = 1, ["ProdutoId"] = 2 });
        Assert.NotNull(stmt);
        Assert.Contains("WHERE [PedidoId] = @k0 AND [ProdutoId] = @k1;", stmt!.Sql);
        Assert.Equal(2, stmt.Parameters.Single(p => p.Name == "@k1").Value);
        Assert.Equal(5, stmt.Parameters.Single(p => p.Name == "@n1").Value);
    }

    [Fact]
    public void Update_WithOnlyReadOnlyChanges_ReturnsNull()
        => Assert.Null(TableDataSql.BuildUpdate(Clientes(),
            new Dictionary<string, object?> { ["Total"] = 1m },
            new Dictionary<string, object?> { ["ClienteId"] = 1 }));

    [Fact]
    public void Delete_IsKeyedByPk()
    {
        var stmt = TableDataSql.BuildDelete(ItensPedido(), new Dictionary<string, object?> { ["PedidoId"] = 1, ["ProdutoId"] = 2 });
        Assert.Equal("DELETE FROM [dbo].[ItensPedido] WHERE [PedidoId] = @k0 AND [ProdutoId] = @k1;", stmt.Sql);
        Assert.Equal(new object?[] { 1, 2 }, stmt.Parameters.Select(p => p.Value).ToArray());
    }

    [Fact]
    public void Delete_WithoutPk_Throws()
    {
        var s = ItensPedido();
        foreach (var c in s.Columns) { c.IsPrimaryKey = false; c.KeyOrdinal = 0; }
        Assert.Throws<InvalidOperationException>(() => TableDataSql.BuildDelete(s, new Dictionary<string, object?>()));
    }

    [Fact]
    public void Delete_WithMissingKeyValue_Throws()
        => Assert.Throws<ArgumentException>(() =>
            TableDataSql.BuildDelete(ItensPedido(), new Dictionary<string, object?> { ["PedidoId"] = 1, ["ProdutoId"] = DBNull.Value }));

    [Theory]
    [InlineData("nvarchar", SqlDbType.NVarChar)]
    [InlineData("varchar", SqlDbType.VarChar)]
    [InlineData("datetime2", SqlDbType.DateTime2)]
    [InlineData("decimal", SqlDbType.Decimal)]
    [InlineData("INT", SqlDbType.Int)]
    public void MapSqlDbType_KnownTypes(string name, SqlDbType expected)
        => Assert.Equal(expected, TableDataSql.MapSqlDbType(name));

    [Fact]
    public void MapSqlDbType_Unknown_IsNull()
        => Assert.Null(TableDataSql.MapSqlDbType("sql_variant"));
}
