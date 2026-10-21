using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.Tests;

public class FieldDdlBuilderTests
{
    private static ColumnStructure Col(string name, string type, int? len = null, bool nullable = true) =>
        new() { Ordinal = 1, Name = name, TypeName = type, Length = len, IsNullable = nullable };

    [Theory]
    [InlineData("nvarchar", 50, null, null, "nvarchar(50)")]
    [InlineData("varchar", -1, null, null, "varchar(max)")]
    [InlineData("decimal", null, 18, 2, "decimal(18,2)")]
    [InlineData("datetime2", null, null, 3, "datetime2(3)")]
    [InlineData("int", 4, 10, 0, "int")]
    public void FormatType_AppliesModifiers(string type, int? len, int? prec, int? scale, string expected)
    {
        var f = new FieldDefinition { DataType = type, Length = len, Precision = prec, Scale = scale };
        Assert.Equal(expected, FieldDdlBuilder.FormatType(f));
    }

    [Fact]
    public void FormatType_UserDefinedType_IsQualified()
    {
        Assert.Equal("[dbo].[Phone]", FieldDdlBuilder.FormatType(new FieldDefinition { DataType = "Phone", TypeSchema = "dbo", Length = 20 }));
    }

    [Fact]
    public void AddColumn_QuotesIdentifiersAndAddsDefaultAndDescription()
    {
        var f = new FieldDefinition
        {
            Name = "Obs]x", DataType = "nvarchar", Length = 100, IsNullable = false,
            DefaultExpression = "N''", Collation = "Latin1_General_CI_AS", Description = "It's a note"
        };
        var sql = FieldDdlBuilder.BuildAddColumn("dbo", "Cli]entes", f);
        Assert.Equal(2, sql.Count);
        Assert.Equal("ALTER TABLE [dbo].[Cli]]entes] ADD [Obs]]x] nvarchar(100) COLLATE Latin1_General_CI_AS NOT NULL CONSTRAINT [DF_Cli]]entes_Obs]]x] DEFAULT (N'');", sql[0]);
        Assert.Contains("sp_addextendedproperty", sql[1]);
        Assert.Contains("N'It''s a note'", sql[1]);
        Assert.Contains("@level2name = N'Obs]x'", sql[1]);
    }

    [Fact]
    public void AddColumn_Identity()
    {
        var f = new FieldDefinition { Name = "Id", DataType = "bigint", IsNullable = false, IsIdentity = true, IdentitySeed = 100, IdentityIncrement = 5 };
        Assert.Equal("ALTER TABLE [dbo].[T] ADD [Id] bigint IDENTITY(100,5) NOT NULL;", FieldDdlBuilder.BuildAddColumn("dbo", "T", f).Single());
    }

    [Fact]
    public void AddColumn_NullableWithDefault_UsesWithValues()
    {
        var f = new FieldDefinition { Name = "Ativo", DataType = "bit", IsNullable = true, DefaultExpression = "1" };
        Assert.EndsWith("DEFAULT (1) WITH VALUES;", FieldDdlBuilder.BuildAddColumn("dbo", "T", f)[0]);
    }

    [Theory]
    [InlineData("", "nvarchar", 10, "nome")]
    [InlineData("A", "nvarchar", 5000, "4000")]
    [InlineData("A", "char", -1, "max")]
    [InlineData("A", "varchar", null, "tamanho")]
    public void Validate_RejectsBadLengths(string name, string type, int? len, string fragment)
    {
        var err = FieldDdlBuilder.Validate(new FieldDefinition { Name = name, DataType = type, Length = len }, true);
        Assert.NotNull(err);
        Assert.Contains(fragment, err!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_IdentityRules()
    {
        Assert.NotNull(FieldDdlBuilder.Validate(new FieldDefinition { Name = "A", DataType = "nvarchar", Length = 5, IsIdentity = true, IsNullable = false }, true));
        Assert.NotNull(FieldDdlBuilder.Validate(new FieldDefinition { Name = "A", DataType = "int", IsIdentity = true, IsNullable = true }, true));
        Assert.Null(FieldDdlBuilder.Validate(new FieldDefinition { Name = "A", DataType = "int", IsIdentity = true, IsNullable = false }, true));
        Assert.NotNull(FieldDdlBuilder.Validate(new FieldDefinition { Name = "A", DataType = "int", Collation = "Latin1_General_CI_AS" }, true));
    }

    [Fact]
    public void Validate_Decimal()
    {
        Assert.Null(FieldDdlBuilder.Validate(new FieldDefinition { Name = "P", DataType = "decimal", Precision = 10, Scale = 2 }, true));
        Assert.NotNull(FieldDdlBuilder.Validate(new FieldDefinition { Name = "P", DataType = "decimal", Precision = 5, Scale = 6 }, true));
        Assert.NotNull(FieldDdlBuilder.Validate(new FieldDefinition { Name = "P", DataType = "numeric", Precision = 39, Scale = 0 }, true));
    }

    [Fact]
    public void NeedsRowsWarning_OnlyForNotNullWithoutDefault()
    {
        var f = new FieldDefinition { Name = "X", DataType = "int", IsNullable = false };
        Assert.True(FieldDdlBuilder.NeedsRowsWarning(f, 3));
        Assert.False(FieldDdlBuilder.NeedsRowsWarning(f, 0));
        f.DefaultExpression = "0";
        Assert.False(FieldDdlBuilder.NeedsRowsWarning(f, 3));
    }

    [Fact]
    public void AlterColumn_NoChange_ReturnsEmpty()
    {
        var c = Col("Nome", "nvarchar", 100, false);
        c.Collation = "Latin1_General_CI_AS";
        Assert.Empty(FieldDdlBuilder.BuildAlterColumn("dbo", "Clientes", c, FieldDefinition.FromColumn(c)));
    }

    [Fact]
    public void AlterColumn_TypeChangeKeepsCollation()
    {
        var c = Col("Nome", "nvarchar", 100, false);
        c.Collation = "Latin1_General_CI_AS";
        var f = FieldDefinition.FromColumn(c);
        f.Length = 200;
        var sql = FieldDdlBuilder.BuildAlterColumn("dbo", "Clientes", c, f);
        Assert.Equal("ALTER TABLE [dbo].[Clientes] ALTER COLUMN [Nome] nvarchar(200) COLLATE Latin1_General_CI_AS NOT NULL;", sql.Single());
    }

    [Fact]
    public void AlterColumn_RenameDefaultAndDescription_InOrder()
    {
        var c = Col("Status", "varchar", 20, false);
        c.DefaultName = "DF__Pedidos__Status__123"; c.DefaultDefinition = "('ABERTO')"; c.DefaultIsSystemNamed = true;
        c.Description = "old";
        var f = FieldDefinition.FromColumn(c);
        f.Name = "Situacao";
        f.Length = 30;
        f.DefaultExpression = "'NOVO'";
        f.Description = "new";
        var sql = FieldDdlBuilder.BuildAlterColumn("dbo", "Pedidos", c, f);
        Assert.Equal(5, sql.Count);
        Assert.Equal("ALTER TABLE [dbo].[Pedidos] DROP CONSTRAINT [DF__Pedidos__Status__123];", sql[0]);
        Assert.Equal("ALTER TABLE [dbo].[Pedidos] ALTER COLUMN [Status] varchar(30) NOT NULL;", sql[1]);
        Assert.Equal("EXEC sys.sp_rename N'[dbo].[Pedidos].[Status]', N'Situacao', N'COLUMN';", sql[2]);
        Assert.Equal("ALTER TABLE [dbo].[Pedidos] ADD CONSTRAINT [DF_Pedidos_Situacao] DEFAULT ('NOVO') FOR [Situacao];", sql[3]);
        Assert.StartsWith("EXEC sys.sp_updateextendedproperty", sql[4]);
        Assert.Contains("@level2name = N'Situacao'", sql[4]);
    }

    [Fact]
    public void AlterColumn_SameDefaultDifferentParens_IsNotAChange()
    {
        var c = Col("Qtd", "int", null, false);
        c.DefaultName = "DF_X"; c.DefaultDefinition = "((0))";
        var f = FieldDefinition.FromColumn(c);
        f.DefaultExpression = "0";
        Assert.Empty(FieldDdlBuilder.BuildAlterColumn("dbo", "T", c, f));
    }

    [Fact]
    public void AlterColumn_RemoveDefaultAndDescription()
    {
        var c = Col("Qtd", "int", null, true);
        c.DefaultName = "DF_X"; c.DefaultDefinition = "((0))"; c.Description = "d";
        var f = FieldDefinition.FromColumn(c);
        f.DefaultExpression = ""; f.Description = "";
        var sql = FieldDdlBuilder.BuildAlterColumn("dbo", "T", c, f);
        Assert.Equal("ALTER TABLE [dbo].[T] DROP CONSTRAINT [DF_X];", sql[0]);
        Assert.StartsWith("EXEC sys.sp_dropextendedproperty", sql[1]);
        Assert.Equal(2, sql.Count);
    }

    [Fact]
    public void AlterColumn_AddDescription()
    {
        var c = Col("Qtd", "int");
        var f = FieldDefinition.FromColumn(c);
        f.Description = "Quantidade";
        Assert.StartsWith("EXEC sys.sp_addextendedproperty", FieldDdlBuilder.BuildAlterColumn("dbo", "T", c, f).Single());
    }

    private static TableStructure Sample()
    {
        var t = new TableStructure { Schema = "dbo", Name = "Pedidos", RowCount = 2 };
        t.Columns.Add(new ColumnStructure { Ordinal = 1, Name = "PedidoId", TypeName = "int", IsIdentity = true, IdentitySeed = 1, IdentityIncrement = 1 });
        t.Columns.Add(new ColumnStructure { Ordinal = 2, Name = "ClienteId", TypeName = "int" });
        t.Columns.Add(new ColumnStructure { Ordinal = 3, Name = "Status", TypeName = "varchar", Length = 20, DefaultName = "DF_P_S", DefaultDefinition = "('ABERTO')" });
        t.Columns.Add(new ColumnStructure { Ordinal = 4, Name = "Total", TypeName = "decimal", Precision = 12, Scale = 2, IsNullable = true });
        t.Columns.Add(new ColumnStructure { Ordinal = 5, Name = "Obs", TypeName = "nvarchar", Length = -1, IsNullable = true });
        t.KeyConstraints.Add(new KeyConstraintStructure { Name = "PK_Pedidos", IsPrimaryKey = true, IsClustered = true, Columns = { new IndexColumnRef("PedidoId") } });
        t.ForeignKeys.Add(new ForeignKeyStructure { Name = "FK_Pedidos_Clientes", ParentSchema = "dbo", ParentTable = "Pedidos", Columns = { "ClienteId" }, ReferencedSchema = "dbo", ReferencedTable = "Clientes", ReferencedColumns = { "ClienteId" }, IsOutgoing = true });
        t.ForeignKeys.Add(new ForeignKeyStructure { Name = "FK_Itens_Pedidos", ParentSchema = "dbo", ParentTable = "ItensPedido", Columns = { "PedidoId" }, ReferencedSchema = "dbo", ReferencedTable = "Pedidos", ReferencedColumns = { "PedidoId" }, DeleteAction = "CASCADE" });
        t.Indexes.Add(new IndexStructure { Name = "IX_Total", Type = 2, KeyColumns = { new IndexColumnRef("Total", true) }, IncludedColumns = { "Status" } });
        t.CheckConstraints.Add(new CheckConstraintStructure { Name = "CK_Total", Definition = "([Total]>=(0))", ColumnName = "Total" });
        return t;
    }

    [Theory]
    [InlineData("PedidoId", "chave primária")]
    [InlineData("ClienteId", "FK_Pedidos_Clientes")]
    [InlineData("Status", "IX_Total")]
    [InlineData("Total", "IX_Total")]
    public void DropBlockers_DetectsKeysAndIndexes(string column, string fragment)
    {
        var reasons = FieldDdlBuilder.GetDropBlockers(Sample(), column);
        Assert.Contains(reasons, r => r.Contains(fragment));
    }

    [Fact]
    public void DropBlockers_FreeColumn_IsAllowed() => Assert.Empty(FieldDdlBuilder.GetDropBlockers(Sample(), "Obs"));

    [Fact]
    public void DropColumn_DropsDefaultAndCheckFirst()
    {
        var t = Sample();
        t.Indexes.Clear();
        var sql = FieldDdlBuilder.BuildDropColumn(t, "Total");
        Assert.Equal(new[]
        {
            "ALTER TABLE [dbo].[Pedidos] DROP CONSTRAINT [CK_Total];",
            "ALTER TABLE [dbo].[Pedidos] DROP COLUMN [Total];"
        }, sql);
        var sql2 = FieldDdlBuilder.BuildDropColumn(t, "Status");
        Assert.Equal("ALTER TABLE [dbo].[Pedidos] DROP CONSTRAINT [DF_P_S];", sql2[0]);
    }

    [Fact]
    public void StripOuterParens()
    {
        Assert.Equal("0", SqlText.StripOuterParens("((0))"));
        Assert.Equal("(0)+(1)", SqlText.StripOuterParens("((0)+(1))"));
        Assert.Equal("'(x)'", SqlText.StripOuterParens("('(x)')"));
        Assert.Equal("", SqlText.StripOuterParens(null));
    }

    [Fact]
    public void RebuildScript_ReordersCopiesAndRecreates()
    {
        var t = Sample();
        t.IdentityCurrent = 57;
        t.Triggers.Add(new TriggerStructure { Name = "trg_x", Definition = "CREATE TRIGGER [dbo].[trg_x] ON [dbo].[Pedidos] AFTER INSERT AS SELECT 'a';" });
        t.Description = "Pedidos";
        t.Columns[4].Description = "Observação";
        var s = TableRebuildScriptBuilder.Build(t, new[] { "PedidoId", "Obs", "ClienteId", "Status", "Total" });

        var create = s.IndexOf("CREATE TABLE [dbo].[SqlExpertTmp_Pedidos]");
        Assert.True(create > 0);
        Assert.True(s.IndexOf("[Obs] nvarchar(max) NULL") < s.IndexOf("[ClienteId] int NOT NULL"));
        Assert.Contains("[PedidoId] int IDENTITY(1,1) NOT NULL", s);
        Assert.Contains("SET IDENTITY_INSERT [dbo].[SqlExpertTmp_Pedidos] ON;", s);
        Assert.Contains("INSERT INTO [dbo].[SqlExpertTmp_Pedidos] ([PedidoId], [Obs], [ClienteId], [Status], [Total])", s);
        Assert.True(s.IndexOf("ALTER TABLE [dbo].[ItensPedido] DROP CONSTRAINT [FK_Itens_Pedidos];") < create);
        Assert.Contains("DROP TABLE [dbo].[Pedidos];", s);
        Assert.Contains("EXEC sys.sp_rename N'[dbo].[SqlExpertTmp_Pedidos]', N'Pedidos', N'OBJECT';", s);
        Assert.Contains("DBCC CHECKIDENT (N'[dbo].[Pedidos]', RESEED, 57);", s);
        Assert.Contains("ADD CONSTRAINT [PK_Pedidos] PRIMARY KEY CLUSTERED ([PedidoId] ASC);", s);
        Assert.Contains("CREATE NONCLUSTERED INDEX [IX_Total] ON [dbo].[Pedidos] ([Total] DESC) INCLUDE ([Status]);", s);
        Assert.Contains("ADD CONSTRAINT [DF_P_S] DEFAULT ('ABERTO') FOR [Status];", s);
        Assert.Contains("WITH CHECK ADD CONSTRAINT [CK_Total] CHECK ([Total]>=(0));", s);
        Assert.Contains("ALTER TABLE [dbo].[ItensPedido] WITH CHECK ADD CONSTRAINT [FK_Itens_Pedidos] FOREIGN KEY ([PedidoId]) REFERENCES [dbo].[Pedidos] ([PedidoId]) ON DELETE CASCADE;", s);
        Assert.Contains("EXEC sys.sp_executesql N'CREATE TRIGGER [dbo].[trg_x] ON [dbo].[Pedidos] AFTER INSERT AS SELECT ''a'';';", s);
        Assert.Contains("@level2name = N'Obs'", s);
        Assert.StartsWith("-- SqlExpert", s);
        Assert.Contains("BEGIN TRANSACTION;", s);
        Assert.EndsWith("COMMIT TRANSACTION;" + Environment.NewLine, s);
    }

    [Fact]
    public void RebuildScript_SkipsComputedAndRowVersionInCopy()
    {
        var t = new TableStructure { Schema = "s", Name = "T" };
        t.Columns.Add(new ColumnStructure { Ordinal = 1, Name = "A", TypeName = "int" });
        t.Columns.Add(new ColumnStructure { Ordinal = 2, Name = "B", TypeName = "int", IsComputed = true, ComputedDefinition = "([A]*(2))", IsPersisted = true });
        t.Columns.Add(new ColumnStructure { Ordinal = 3, Name = "V", TypeName = "timestamp" });
        var s = TableRebuildScriptBuilder.Build(t, new[] { "B", "V", "A" });
        Assert.Contains("[B] AS ([A]*(2)) PERSISTED", s);
        Assert.Contains("[V] rowversion NOT NULL", s);
        Assert.Contains("INSERT INTO [s].[SqlExpertTmp_T] ([A])", s);
        Assert.DoesNotContain("IDENTITY_INSERT", s);
    }

    [Fact]
    public void OrderColumns_AppendsMissing()
    {
        var names = TableRebuildScriptBuilder.OrderColumns(Sample(), new[] { "Total", "nope" }).Select(c => c.Name);
        Assert.Equal(new[] { "Total", "PedidoId", "ClienteId", "Status", "Obs" }, names);
    }

    [Fact]
    public void FieldList_IsTabSeparatedWithHeader()
    {
        var cols = new[]
        {
            new ColumnInfo { Ordinal = 2, Name = "Preco", DataType = "decimal", Precision = 10, Scale = 2, IsNullable = false, DefaultValue = "((0))" },
            new ColumnInfo { Ordinal = 1, Name = "Id", DataType = "int", IsPrimaryKey = true, Scale = 0 },
            new ColumnInfo { Ordinal = 3, Name = "Obs", DataType = "nvarchar", Size = -1, IsNullable = true, Collation = "Latin1_General_CI_AS", Description = "a\tb\nc" }
        };
        var lines = FieldListFormatter.ToTabSeparated(cols).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("#\tPK\tFK\tUNQ\tField Name\tField Type\tSize\tScale\tNull\tDefault\tCollation\tDescription", lines[0]);
        Assert.Equal("1\tPK\t\t\tId\tint\t\t\tNOT NULL\t\t\t", lines[1]);
        Assert.Equal("2\t\t\t\tPreco\tdecimal\t10\t2\tNOT NULL\t((0))\t\t", lines[2]);
        Assert.Equal("3\t\t\t\tObs\tnvarchar\tmax\t\tNULL\t\tLatin1_General_CI_AS\ta b c", lines[3]);
    }

    [Fact]
    public void TableStructureSql_HasAllResultSets()
    {
        var sql = TableStructureSql.Load;
        foreach (var fragment in new[] { "@schema", "@table", "sys.key_constraints", "sys.check_constraints", "sys.foreign_keys", "sys.indexes", "sys.triggers", "MS_Description", "IDENT_CURRENT" })
            Assert.Contains(fragment, sql);
    }
}
