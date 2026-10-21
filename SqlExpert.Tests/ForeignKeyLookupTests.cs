using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SqlExpert.Core.Abstractions;
using SqlExpert.Core.DataForms;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;
using Xunit.Abstractions;

namespace SqlExpert.Tests;

public class ForeignKeyLookupMetadataTests
{
    [Fact]
    public void Query_ReadsForeignKeyCatalogViews_InConstraintOrder()
    {
        var sql = ForeignKeySql.TableForeignKeys;
        Assert.Contains("sys.foreign_keys", sql);
        Assert.Contains("sys.foreign_key_columns", sql);
        Assert.Contains("@schema", sql);
        Assert.Contains("@table", sql);
        Assert.Contains("fk.referenced_object_id", sql);
        Assert.Matches(@"ORDER BY fk\.name, fkc\.constraint_column_id", sql);
    }

    [Fact]
    public void Group_BuildsOneForeignKeyPerConstraint_ColumnsInOrder()
    {
        var rows = new[]
        {
            new ForeignKeyColumnRow("FK_Item_Pedido", "dbo", "PEDIDO", 2, "PED_NUM", "NUMERO", false),
            new ForeignKeyColumnRow("FK_Acum_CFOP", "dbo", "CFOP", 1, "CFOP_ID_INTERESTADUAL", "CFOP_ID", false),
            new ForeignKeyColumnRow("FK_Item_Pedido", "dbo", "PEDIDO", 1, "PED_EMP", "EMP_ID", true)
        };
        var fks = ForeignKeyLookup.Group(rows);
        Assert.Equal(new[] { "FK_Acum_CFOP", "FK_Item_Pedido" }, fks.Select(f => f.Name));
        var multi = fks[1];
        Assert.True(multi.IsMultiColumn);
        Assert.True(multi.IsDisabled);
        Assert.Equal(new[] { "PED_EMP", "PED_NUM" }, multi.LocalColumns);
        Assert.Equal(new[] { "EMP_ID", "NUMERO" }, multi.ReferencedColumns);
        Assert.Equal("dbo.CFOP", fks[0].ReferencedTitle);
        Assert.False(fks[0].IsMultiColumn);
    }

    private static DataColumnInfo Col(int ordinal, string name, string type, int maxLength = 0, bool pk = false) =>
        new() { Ordinal = ordinal, Name = name, TypeName = type, BaseTypeName = type, MaxLength = maxLength, IsPrimaryKey = pk, KeyOrdinal = pk ? 1 : 0 };

    private static readonly ForeignKeyInfo CfopFk = new()
    {
        Name = "FK_ACUM_CFOP_INTER", ReferencedSchema = "dbo", ReferencedTable = "CFOP",
        Columns = new[] { new ForeignKeyColumnPair("CFOP_ID_INTERESTADUAL", "CFOP_ID") }
    };

    [Fact]
    public void LookupSelect_TopOrderedByKey_AndFetchAll()
    {
        var cols = new[] { Col(1, "CFOP_ID", "int"), Col(2, "CFOP_CODIGO", "varchar", 10), Col(3, "CFOP_DESCRICAO", "varchar", 200) };
        Assert.Equal("SELECT TOP (@top) [CFOP_ID], [CFOP_CODIGO], [CFOP_DESCRICAO] FROM [dbo].[CFOP] ORDER BY [CFOP_ID];",
            ForeignKeySql.BuildLookupSelect(CfopFk, cols, 1, 1000));
        Assert.Equal("SELECT [CFOP_ID], [CFOP_CODIGO], [CFOP_DESCRICAO] FROM [dbo].[CFOP] ORDER BY [CFOP_ID];",
            ForeignKeySql.BuildLookupSelect(CfopFk, cols, 1, 0));
        var odd = new ForeignKeyInfo { Name = "x", ReferencedSchema = "s]1", ReferencedTable = "t]2", Columns = CfopFk.Columns };
        Assert.Contains("FROM [s]]1].[t]]2]", ForeignKeySql.BuildLookupSelect(odd, cols, 1, 5));
        Assert.Throws<ArgumentException>(() => ForeignKeySql.BuildLookupSelect(CfopFk, cols, 0, 5));
    }

    [Fact]
    public void LookupRowByKey_IsParameterized()
    {
        var cols = new[] { Col(1, "EMP_ID", "int"), Col(2, "NUMERO", "int"), Col(3, "NOME", "nvarchar", 120) };
        var fk = new ForeignKeyInfo
        {
            Name = "FK", ReferencedSchema = "dbo", ReferencedTable = "PEDIDO",
            Columns = new[] { new ForeignKeyColumnPair("PED_EMP", "EMP_ID"), new ForeignKeyColumnPair("PED_NUM", "NUMERO") }
        };
        var stmt = ForeignKeySql.BuildLookupRowByKey(fk, cols, 2, new object?[] { 758, 4 });
        Assert.Equal("SELECT [EMP_ID], [NUMERO], [NOME] FROM [dbo].[PEDIDO] WHERE [EMP_ID] = @k0 AND [NUMERO] = @k1;", stmt.Sql);
        Assert.Equal(new object?[] { 758, 4 }, stmt.Parameters.Select(p => p.Value));
        Assert.Equal("NUMERO", stmt.Parameters[1].Column.Name);
        Assert.Throws<ArgumentException>(() => ForeignKeySql.BuildLookupRowByKey(fk, cols, 2, new object?[] { 1 }));
    }
}

public class ForeignKeyLookupDisplayColumnTests
{
    private static DataColumnInfo Col(int ordinal, string name, string type, int maxLength = 0) =>
        new() { Ordinal = ordinal, Name = name, TypeName = type, BaseTypeName = type, MaxLength = maxLength };

    [Fact]
    public void Cfop_KeyFirst_ThenCodeAndDescription()
    {
        var cols = new[]
        {
            Col(1, "CFOP_ID", "int"), Col(2, "CFOP_CODIGO", "varchar", 10), Col(3, "CFOP_DESCRICAO", "varchar", 200),
            Col(4, "CFOP_TIPO", "int"), Col(5, "USR_CODIGO", "int"), Col(6, "DTHR_GRAV", "datetime")
        };
        var shown = ForeignKeyLookup.SelectDisplayColumns(cols, new[] { "CFOP_ID" });
        Assert.Equal(new[] { "CFOP_ID", "CFOP_CODIGO", "CFOP_DESCRICAO", "CFOP_TIPO", "USR_CODIGO", "DTHR_GRAV" }, shown.Select(c => c.Name));
        var three = ForeignKeyLookup.SelectDisplayColumns(cols, new[] { "cfop_id" }, maxColumns: 3);
        Assert.Equal(new[] { "CFOP_ID", "CFOP_CODIGO", "CFOP_DESCRICAO" }, three.Select(c => c.Name));
    }

    [Fact]
    public void PreferredCharacterColumns_BeatEarlierOnes_ShownInTableOrder()
    {
        var cols = new[]
        {
            Col(1, "ID", "int"), Col(2, "OBS", "varchar", 50), Col(3, "FOTO", "varbinary", -1), Col(4, "XMLDATA", "xml", -1),
            Col(5, "TEXTO_LONGO", "nvarchar", -1), Col(6, "VALOR", "decimal"), Col(7, "SIGLA", "char", 2), Col(8, "NOME", "nvarchar", 120)
        };
        var two = ForeignKeyLookup.SelectDisplayColumns(cols, new[] { "ID" }, maxColumns: 3);
        Assert.Equal(new[] { "ID", "SIGLA", "NOME" }, two.Select(c => c.Name));       // preferred names, table order
        var all = ForeignKeyLookup.SelectDisplayColumns(cols, new[] { "ID" }, maxColumns: 10);
        Assert.DoesNotContain(all, c => c.Name is "FOTO" or "XMLDATA");              // never binary / xml
        var five = ForeignKeyLookup.SelectDisplayColumns(cols, new[] { "ID" }, maxColumns: 5);
        Assert.Equal(new[] { "ID", "OBS", "TEXTO_LONGO", "SIGLA", "NOME" }, five.Select(c => c.Name)); // char before numeric, MAX last char tier
    }

    [Fact]
    public void AccentsAndCase_InPreferredNames()
    {
        Assert.True(ForeignKeyLookup.PreferredRank("descrição") >= 0);
        Assert.True(ForeignKeyLookup.PreferredRank("RazaoSocial") >= 0);
        Assert.Equal(-1, ForeignKeyLookup.PreferredRank("VALOR"));
    }

    [Fact]
    public void MultiColumnKey_AllKeysFirst_UnknownKeyThrows()
    {
        var cols = new[] { Col(1, "NOME", "nvarchar", 60), Col(2, "EMP_ID", "int"), Col(3, "NUMERO", "int") };
        var shown = ForeignKeyLookup.SelectDisplayColumns(cols, new[] { "EMP_ID", "NUMERO" });
        Assert.Equal(new[] { "EMP_ID", "NUMERO", "NOME" }, shown.Select(c => c.Name));
        Assert.Throws<ArgumentException>(() => ForeignKeyLookup.SelectDisplayColumns(cols, new[] { "NAO_EXISTE" }));
        // At least one display column even when the key alone fills the limit.
        Assert.Equal(3, ForeignKeyLookup.SelectDisplayColumns(cols, new[] { "EMP_ID", "NUMERO" }, maxColumns: 2).Count);
    }
}

public class ForeignKeyLookupTargetTests
{
    private static readonly ForeignKeyInfo Single = new()
    {
        Name = "FK_CFOP", ReferencedSchema = "dbo", ReferencedTable = "CFOP",
        Columns = new[] { new ForeignKeyColumnPair("CFOP_ID", "CFOP_ID") }
    };

    private static readonly ForeignKeyInfo Multi = new()
    {
        Name = "FK_PEDIDO", ReferencedSchema = "dbo", ReferencedTable = "PEDIDO",
        Columns = new[] { new ForeignKeyColumnPair("EMP_ID", "EMP_ID"), new ForeignKeyColumnPair("PED_NUM", "NUMERO") }
    };

    private static readonly ForeignKeyInfo EmpOnly = new()
    {
        Name = "FK_EMPRESA", ReferencedSchema = "dbo", ReferencedTable = "EMPRESA",
        Columns = new[] { new ForeignKeyColumnPair("EMP_ID", "EMP_ID") }
    };

    private static LookupLocalColumn C(string name, bool editable = true, bool nullable = false) => new(name, editable, nullable);

    [Fact]
    public void SingleColumn_EnabledOnItsColumn()
    {
        var t = ForeignKeyLookup.ResolveTargets(new[] { Single }, new[] { C("ID"), C("cfop_id", nullable: true), C("NOME") });
        Assert.Single(t);
        var target = t["CFOP_ID"];
        Assert.True(target.IsEnabled);
        Assert.True(target.AllowsNull);
        Assert.Equal(new[] { "CFOP_ID" }, target.LocalColumns);
        Assert.Contains("dbo.CFOP", target.ToolTip);
    }

    [Fact]
    public void MultiColumn_AllPresent_SetsAllColumnsFromEitherCell()
    {
        var t = ForeignKeyLookup.ResolveTargets(new[] { Multi }, new[] { C("EMP_ID", nullable: true), C("PED_NUM") });
        Assert.Same(t["EMP_ID"], t["PED_NUM"]);
        Assert.True(t["PED_NUM"].IsEnabled);
        Assert.False(t["PED_NUM"].AllowsNull);              // one NOT NULL column: no "(null)"
        Assert.Equal(new[] { "EMP_ID", "PED_NUM" }, t["EMP_ID"].LocalColumns);
    }

    [Fact]
    public void MultiColumn_MissingOrReadOnlyColumn_DisablesLookup()
    {
        var missing = ForeignKeyLookup.ResolveTargets(new[] { Multi }, new[] { C("PED_NUM") });
        Assert.False(missing["PED_NUM"].IsEnabled);
        Assert.Contains("EMP_ID", missing["PED_NUM"].DisabledReason);
        Assert.False(missing["PED_NUM"].AllowsNull);

        var readOnly = ForeignKeyLookup.ResolveTargets(new[] { Multi }, new[] { C("EMP_ID", editable: false), C("PED_NUM") });
        Assert.False(readOnly["PED_NUM"].IsEnabled);
        Assert.Contains("somente leitura", readOnly["EMP_ID"].DisabledReason);
    }

    [Fact]
    public void ColumnInSeveralForeignKeys_PrefersEnabled_ThenFewerColumns()
    {
        var both = ForeignKeyLookup.ResolveTargets(new[] { Multi, EmpOnly }, new[] { C("EMP_ID"), C("PED_NUM") });
        Assert.Equal("FK_EMPRESA", both["EMP_ID"].ForeignKey.Name);
        Assert.Equal("FK_PEDIDO", both["PED_NUM"].ForeignKey.Name);

        var multiDisabled = ForeignKeyLookup.ResolveTargets(new[] { Multi, EmpOnly }, new[] { C("EMP_ID") });
        Assert.Equal("FK_EMPRESA", multiDisabled["EMP_ID"].ForeignKey.Name);
        Assert.True(multiDisabled["EMP_ID"].IsEnabled);
    }

    [Fact]
    public void NoForeignKeys_NoTargets()
    {
        Assert.Empty(ForeignKeyLookup.ResolveTargets(Array.Empty<ForeignKeyInfo>(), new[] { C("A") }));
    }
}

public class ForeignKeyLookupPreselectTests
{
    private static readonly List<object[]> Rows = new()
    {
        new object[] { 1, "1.101", "COMPRA PARA INDUSTRIALIZACAO" },
        new object[] { 2, "2.101", "COMPRA PARA INDUSTRIALIZACAO" },
        new object[] { 544, "6.933", "PRESTACAO DE SERVICO TRIBUTADO" }
    };

    [Fact]
    public void FindsCurrentValue_AcrossNumericTypes()
    {
        Assert.Equal(2, ForeignKeyLookup.FindRowIndex(Rows, new object?[] { 544 }));
        Assert.Equal(2, ForeignKeyLookup.FindRowIndex(Rows, new object?[] { 544L }));
        Assert.Equal(1, ForeignKeyLookup.FindRowIndex(Rows, new object?[] { (short)2 }));
        Assert.Equal(0, ForeignKeyLookup.FindRowIndex(Rows, new object?[] { 1m }));
        Assert.Equal(-1, ForeignKeyLookup.FindRowIndex(Rows, new object?[] { 999 }));
    }

    [Fact]
    public void NullOrEmptyCurrent_SelectsNothing()
    {
        Assert.Equal(-1, ForeignKeyLookup.FindRowIndex(Rows, new object?[] { DBNull.Value }));
        Assert.Equal(-1, ForeignKeyLookup.FindRowIndex(Rows, new object?[] { null }));
        Assert.Equal(-1, ForeignKeyLookup.FindRowIndex(Rows, Array.Empty<object?>()));
    }

    [Fact]
    public void Strings_CaseInsensitive_IgnoringTrailingSpaces_MultiColumn()
    {
        var rows = new List<object[]>
        {
            new object[] { "SP ", 1, "São Paulo" },
            new object[] { "RJ", 2, "Rio" }
        };
        Assert.Equal(0, ForeignKeyLookup.FindRowIndex(rows, new object?[] { "sp", 1L }));
        Assert.Equal(-1, ForeignKeyLookup.FindRowIndex(rows, new object?[] { "sp", 2 }));
        Assert.Equal(1, ForeignKeyLookup.FindRowIndex(rows, new object?[] { "RJ", 2 }));
        var g = Guid.NewGuid();
        Assert.True(ForeignKeyLookup.KeyEquals(g, g.ToString()));
        Assert.False(ForeignKeyLookup.KeyEquals(DBNull.Value, 0));
        Assert.True(ForeignKeyLookup.KeyEquals(null, DBNull.Value));
    }

    [Fact]
    public void CoerceValue_ToLocalColumnType()
    {
        Assert.Equal(544, ForeignKeyLookup.CoerceValue(544L, typeof(int)));
        Assert.IsType<short>(ForeignKeyLookup.CoerceValue(7, typeof(short)));
        Assert.Equal(DBNull.Value, ForeignKeyLookup.CoerceValue(null, typeof(int)));
        Assert.Equal(DBNull.Value, ForeignKeyLookup.CoerceValue(DBNull.Value, typeof(string)));
        var g = Guid.NewGuid();
        Assert.Equal(g, ForeignKeyLookup.CoerceValue(g.ToString(), typeof(Guid)));
        Assert.Equal("12", ForeignKeyLookup.CoerceValue(12, typeof(string)));
        Assert.Equal(3.5m, ForeignKeyLookup.CoerceValue(3.5, typeof(decimal)));
    }

    [Fact]
    public void FilterRows_AccentAndCaseInsensitive_AllWords()
    {
        var texts = new List<IReadOnlyList<string>>
        {
            new[] { "1", "1.101", "COMPRA PARA INDUSTRIALIZAÇÃO" },
            new[] { "544", "6.933", "PRESTACAO DE SERVICO" },
            new[] { "3", "3.101", "Compra de serviço" }
        };
        Assert.Equal(new[] { 0, 2 }, ForeignKeyLookup.FilterRows(texts, "compra"));
        Assert.Equal(new[] { 1, 2 }, ForeignKeyLookup.FilterRows(texts, "servico"));
        Assert.Equal(new[] { 2 }, ForeignKeyLookup.FilterRows(texts, "compra serv"));
        Assert.Equal(new[] { 0 }, ForeignKeyLookup.FilterRows(texts, "industrializacao"));
        Assert.Equal(new[] { 0, 1, 2 }, ForeignKeyLookup.FilterRows(texts, "  "));
        Assert.Equal(new[] { 1 }, ForeignKeyLookup.FilterRows(texts, "6.93"));
    }

    [Fact]
    public void FetchedText_LikeReference()
    {
        var fk = new ForeignKeyInfo { Name = "FK", ReferencedTable = "CFOP", Columns = new[] { new ForeignKeyColumnPair("A", "B") } };
        var data = new LookupData { ForeignKey = fk, Rows = new List<object[]> { new object[] { 1 }, new object[] { 2 } }, IsComplete = true };
        Assert.Equal("2 records fetched", ForeignKeyLookup.FetchedText(data, 2));
        Assert.Equal("1 of 2 records", ForeignKeyLookup.FetchedText(data, 1));
        var cut = new LookupData { ForeignKey = fk, Rows = data.Rows, IsComplete = false, Top = 2 };
        Assert.Equal("2 records fetched (TOP 2)", ForeignKeyLookup.FetchedText(cut, 2));
    }
}

/// <summary>Static audit of the App wiring of the lookup (no GUI is started).</summary>
public class ForeignKeyLookupWiringTests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "SqlExpert.App");
    }

    [Fact]
    public void PopupGrid_IsReadOnly_OneWay_LightLines_AndDetached()
    {
        var src = File.ReadAllText(Path.Combine(AppDir(), "Views", "ForeignKeyLookupPopup.cs"));
        Assert.Contains("IsReadOnly = true", src);
        Assert.Contains("Mode = BindingMode.OneWay", src);
        Assert.Contains("SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, \"GridLineBrush\")", src);
        Assert.Contains("SetResourceReference(DataGrid.VerticalGridLinesBrushProperty, \"GridLineBrush\")", src);
        Assert.Contains("StaysOpen = false", src);
        Assert.Contains("Data of [", src);
        Assert.Contains("_popup = new Popup", src);
        Assert.DoesNotMatch(new Regex(@"Mode\s*=\s*BindingMode\.TwoWay"), src);
    }

    [Fact]
    public void Grid_UsesLookupColumnOnlyForEditableForeignKeys()
    {
        var grid = File.ReadAllText(Path.Combine(AppDir(), "Views", "TableDataGrid.xaml.cs"));
        Assert.Contains("spec.HasEditableLookup && !_vm.IsReadOnly && !spec.IsReadOnly", grid);
        Assert.Contains("new ForeignKeyTextColumn(spec.Lookup!)", grid);
        Assert.Contains("Grid.CancelEdit(DataGridEditingUnit.Cell)", grid);
        var form = File.ReadAllText(Path.Combine(AppDir(), "Views", "TableDataGrid.Form.cs"));
        Assert.Contains("OpenFormLookup(lookup, tb, host)", form);
        var column = File.ReadAllText(Path.Combine(AppDir(), "Views", "ForeignKeyTextColumn.cs"));
        Assert.Contains("Key.F4", column);
        Assert.Contains("Key.Down", column);
    }
}

/// <summary>
/// Live, read-only checks against SQLEXPERT_IT_CONNSTR (SqlExpertTeste: Pedidos→Clientes, Produtos→Categorias,
/// ItensPedido→Pedidos/Produtos). Only SELECTs on catalog views and the referenced tables; nothing is written.
/// </summary>
public class ForeignKeyLookupIntegrationTests
{
    private readonly ITestOutputHelper _out;
    private static readonly string? ConnStr = Environment.GetEnvironmentVariable("SQLEXPERT_IT_CONNSTR");

    public ForeignKeyLookupIntegrationTests(ITestOutputHelper output) => _out = output;

    private async Task<SqlConnection?> OpenAsync()
    {
        if (string.IsNullOrWhiteSpace(ConnStr))
        {
            _out.WriteLine("SQLEXPERT_IT_CONNSTR não definido: teste de integração ignorado.");
            return null;
        }
        var c = new SqlConnection(ConnStr);
        await c.OpenAsync();
        if (!string.Equals(c.Database, "SqlExpertTeste", StringComparison.OrdinalIgnoreCase))
        {
            _out.WriteLine($"Banco {c.Database} não é SqlExpertTeste: teste ignorado.");
            await c.DisposeAsync();
            return null;
        }
        return c;
    }

    private sealed class ConnStrFactory : ISqlConnectionFactory
    {
        public string BuildConnectionString(ConnectionProfile profile, string? password = null) => ConnStr!;
        public async Task<SqlConnection> CreateOpenConnectionAsync(string connectionProfileId, CancellationToken cancellationToken = default)
        {
            var c = new SqlConnection(ConnStr);
            await c.OpenAsync(cancellationToken);
            return c;
        }
        public Task<SqlConnection> OpenAsync(ConnectionProfile profile, string? password = null, CancellationToken cancellationToken = default) =>
            CreateOpenConnectionAsync("", cancellationToken);
        public Task TestConnectionAsync(ConnectionProfile profile, string? password = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task ItensPedido_HasTwoSingleColumnForeignKeys()
    {
        await using var conn = await OpenAsync();
        if (conn is null) return;
        var fks = await TableDataService.ReadForeignKeysAsync(conn, "dbo", "ItensPedido");
        foreach (var f in fks) _out.WriteLine($"{f.Name}: {string.Join(",", f.LocalColumns)} -> {f.ReferencedTitle}({string.Join(",", f.ReferencedColumns)})");
        var pedidos = Assert.Single(fks, f => f.ReferencedTable == "Pedidos");
        Assert.Equal(new[] { "PedidoId" }, pedidos.LocalColumns);
        Assert.Equal(new[] { "PedidoId" }, pedidos.ReferencedColumns);
        Assert.Equal("dbo", pedidos.ReferencedSchema);
        var produtos = Assert.Single(fks, f => f.ReferencedTable == "Produtos");
        Assert.Equal(new[] { "ProdutoId" }, produtos.LocalColumns);
        Assert.Empty(await TableDataService.ReadForeignKeysAsync(conn, "dbo", "Clientes"));
    }

    [Fact]
    public async Task Schema_CarriesForeignKeys_AndLookupReadsReferencedRows()
    {
        if (await OpenAsync() is not { } probe) return;
        await probe.DisposeAsync();
        var service = new TableDataService(new ConnStrFactory());
        var schema = await service.GetSchemaAsync("it", "SqlExpertTeste", "dbo", "Produtos");
        var fk = Assert.Single(schema.ForeignKeys);
        Assert.Equal("dbo.Categorias", fk.ReferencedTitle);

        var targets = ForeignKeyLookup.ResolveTargets(schema.ForeignKeys,
            schema.Columns.Select(c => new LookupLocalColumn(c.Name, c.IsWritable, c.IsNullable)).ToList());
        Assert.True(targets["CategoriaId"].IsEnabled);
        Assert.False(targets["CategoriaId"].AllowsNull);   // CategoriaId is NOT NULL

        var data = await service.GetLookupDataAsync("it", "SqlExpertTeste", fk, 1000, null);
        Assert.Equal(new[] { "CategoriaId", "Nome" }, data.Columns.Select(c => c.Name));
        Assert.Equal(1, data.KeyCount);
        Assert.True(data.IsComplete);
        Assert.NotEmpty(data.Rows);
        var keys = data.Rows.Select(r => Convert.ToInt64(r[0])).ToList();
        Assert.Equal(keys.OrderBy(k => k), keys);              // ordered by the key
        var last = data.Rows[^1][0];
        Assert.Equal(data.Rows.Count - 1, ForeignKeyLookup.FindRowIndex(data.Rows, new object?[] { last }));
    }

    [Fact]
    public async Task TopN_AppendsCurrentRowOutsideTheFirstRows()
    {
        await using var conn = await OpenAsync();
        if (conn is null) return;
        var fks = await TableDataService.ReadForeignKeysAsync(conn, "dbo", "Pedidos");
        var fk = Assert.Single(fks);
        Assert.Equal("dbo.Clientes", fk.ReferencedTitle);

        var all = await TableDataService.ReadLookupDataAsync(conn, fk, 0, null);
        Assert.True(all.IsComplete);
        Assert.Equal(0, all.Top);
        Assert.Equal("ClienteId", all.Columns[0].Name);
        Assert.Contains(all.Columns, c => c.Name == "Nome");
        Assert.True(all.Columns.Count <= ForeignKeyLookup.DefaultMaxColumns);
        Assert.True(all.Rows.Count >= 2, "SqlExpertTeste.Clientes deveria ter 2+ linhas");

        var lastKey = all.Rows[^1][0];
        var top1 = await TableDataService.ReadLookupDataAsync(conn, fk, 1, new object?[] { lastKey });
        Assert.False(top1.IsComplete);
        Assert.True(top1.CurrentRowAppended);
        Assert.Equal(2, top1.Rows.Count);
        Assert.Equal(1, ForeignKeyLookup.FindRowIndex(top1.Rows, new object?[] { lastKey }));

        var firstKey = all.Rows[0][0];
        var top1First = await TableDataService.ReadLookupDataAsync(conn, fk, 1, new object?[] { firstKey });
        Assert.False(top1First.CurrentRowAppended);
        Assert.Single(top1First.Rows);
    }
}
