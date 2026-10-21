using Microsoft.Data.SqlClient;
using SqlExpert.Core.Abstractions;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;
using Xunit.Abstractions;

namespace SqlExpert.Tests;

public class PrimaryKeyDdlTests
{
    private static ColumnStructure Col(string name, string type = "int", bool nullable = false, int ordinal = 1, int? length = null) => new()
    {
        Ordinal = ordinal, Name = name, TypeName = type, IsNullable = nullable, Length = length, TypeSchema = "sys"
    };

    private static TableStructure Table(bool withPk = true, bool clustered = true)
    {
        var t = new TableStructure
        {
            Schema = "dbo", Name = "Pedidos", RowCount = 0,
            Columns = { Col("Id", ordinal: 1), Col("Ano", ordinal: 2), Col("Obs", "nvarchar", true, 3, 50) }
        };
        if (withPk)
            t.KeyConstraints.Add(new KeyConstraintStructure { Name = "PK_Pedidos", IsPrimaryKey = true, IsClustered = clustered, Columns = { new IndexColumnRef("Id") } });
        return t;
    }

    private static ForeignKeyStructure FkTo(string table, params string[] cols) => new()
    {
        Name = "FK_Itens_Pedidos", ParentSchema = "dbo", ParentTable = "Itens", Columns = cols.ToList(),
        ReferencedSchema = "dbo", ReferencedTable = table, ReferencedColumns = cols.ToList(), IsOutgoing = false
    };

    [Fact]
    public void NewField_NoKey_CreatesClusteredPk()
    {
        var t = Table(withPk: false);
        var f = new FieldDefinition { Name = "Codigo", DataType = "int", IsNullable = false, IsPrimaryKey = true, Description = "código" };
        var (stmts, plan) = FieldDdlBuilder.BuildAddColumn(t, f);
        Assert.Equal(3, stmts.Count);
        Assert.Equal("ALTER TABLE [dbo].[Pedidos] ADD [Codigo] int NOT NULL;", stmts[0]);
        Assert.Equal("ALTER TABLE [dbo].[Pedidos] ADD CONSTRAINT [PK_Pedidos] PRIMARY KEY CLUSTERED ([Codigo]);", stmts[1]);
        Assert.StartsWith("EXEC sys.sp_addextendedproperty", stmts[2]);
        Assert.False(plan.RecreatesExistingKey);
        Assert.Contains("Cria a chave primária [PK_Pedidos]", plan.Summary);
    }

    [Fact]
    public void NewField_NoKey_ButClusteredIndex_UsesNonclustered_AndFreeName()
    {
        var t = Table(withPk: false);
        t.Indexes.Add(new IndexStructure { Name = "IX_Ano", Type = 1, KeyColumns = { new IndexColumnRef("Ano") } });
        t.SchemaPrimaryKeyNames.Add("PK_Pedidos");
        t.SchemaPrimaryKeyNames.Add("PK_Pedidos_2");
        var f = new FieldDefinition { Name = "Codigo", IsNullable = false, IsPrimaryKey = true };
        var (stmts, _) = FieldDdlBuilder.BuildAddColumn(t, f);
        Assert.Equal("ALTER TABLE [dbo].[Pedidos] ADD CONSTRAINT [PK_Pedidos_3] PRIMARY KEY NONCLUSTERED ([Codigo]);", stmts[1]);
    }

    [Fact]
    public void NewField_ExistingKey_DropsAndRecreatesComposite_KeepingNameOrderAndClustering()
    {
        var t = Table(clustered: false);
        t.KeyConstraints[0].Columns.Add(new IndexColumnRef("Ano", IsDescending: true));
        var f = new FieldDefinition { Name = "Seq", IsNullable = false, IsPrimaryKey = true };
        var (stmts, plan) = FieldDdlBuilder.BuildAddColumn(t, f);
        Assert.Equal(new[]
        {
            "ALTER TABLE [dbo].[Pedidos] ADD [Seq] int NOT NULL;",
            "ALTER TABLE [dbo].[Pedidos] DROP CONSTRAINT [PK_Pedidos];",
            "ALTER TABLE [dbo].[Pedidos] ADD CONSTRAINT [PK_Pedidos] PRIMARY KEY NONCLUSTERED ([Id], [Ano] DESC, [Seq]);"
        }, stmts);
        Assert.True(plan.RecreatesExistingKey);
        Assert.False(plan.IsBlocked);
    }

    [Fact]
    public void ExistingKey_ReferencedByForeignKey_IsRefused()
    {
        var t = Table();
        t.ForeignKeys.Add(FkTo("Pedidos", "Id"));
        var (_, plan) = FieldDdlBuilder.BuildAddColumn(t, new FieldDefinition { Name = "Seq", IsNullable = false, IsPrimaryKey = true });
        Assert.True(plan.IsBlocked);
        Assert.Contains("[FK_Itens_Pedidos] de [dbo].[Itens]", plan.Blockers[0]);

        // an FK on other columns of the table does not depend on the key
        var t2 = Table();
        t2.ForeignKeys.Add(FkTo("Pedidos", "Ano"));
        Assert.False(FieldDdlBuilder.BuildAddColumn(t2, new FieldDefinition { Name = "Seq", IsNullable = false, IsPrimaryKey = true }).Key.IsBlocked);
        // an outgoing FK of this table to another table does not either
        var t3 = Table();
        t3.ForeignKeys.Add(new ForeignKeyStructure { Name = "FK_Pedidos_Clientes", ParentSchema = "dbo", ParentTable = "Pedidos", Columns = { "Id" }, ReferencedSchema = "dbo", ReferencedTable = "Clientes", ReferencedColumns = { "Id" }, IsOutgoing = true });
        Assert.False(FieldDdlBuilder.BuildAddColumn(t3, new FieldDefinition { Name = "Seq", IsNullable = false, IsPrimaryKey = true }).Key.IsBlocked);
    }

    [Fact]
    public void EditField_AddNullableColumnToKey_AltersNotNullBeforeAdd()
    {
        var t = Table();
        var original = t.FindColumn("Obs")!;
        var updated = FieldDefinition.FromColumn(original);
        updated.IsNullable = false;
        updated.IsPrimaryKey = true;
        var (stmts, plan) = FieldDdlBuilder.BuildAlterColumn(t, original, updated);
        Assert.Equal(new[]
        {
            "ALTER TABLE [dbo].[Pedidos] DROP CONSTRAINT [PK_Pedidos];",
            "ALTER TABLE [dbo].[Pedidos] ALTER COLUMN [Obs] nvarchar(50) NOT NULL;",
            "ALTER TABLE [dbo].[Pedidos] ADD CONSTRAINT [PK_Pedidos] PRIMARY KEY CLUSTERED ([Id], [Obs]);"
        }, stmts);
        Assert.Contains("Inclui [Obs]", plan.Summary);
    }

    [Fact]
    public void EditField_RemoveFromKey()
    {
        var t = Table();
        t.KeyConstraints[0].Columns.Add(new IndexColumnRef("Ano"));
        var original = t.FindColumn("Ano")!;
        var updated = FieldDefinition.FromColumn(original); // IsPrimaryKey = false → leaves the key
        var (stmts, plan) = FieldDdlBuilder.BuildAlterColumn(t, original, updated);
        Assert.Equal(new[]
        {
            "ALTER TABLE [dbo].[Pedidos] DROP CONSTRAINT [PK_Pedidos];",
            "ALTER TABLE [dbo].[Pedidos] ADD CONSTRAINT [PK_Pedidos] PRIMARY KEY CLUSTERED ([Id]);"
        }, stmts);
        Assert.Contains("Tira [Ano]", plan.Summary);

        // only key field → the key is dropped
        var single = Table();
        var id = single.FindColumn("Id")!;
        var (s2, p2) = FieldDdlBuilder.BuildAlterColumn(single, id, FieldDefinition.FromColumn(id));
        Assert.Equal(new[] { "ALTER TABLE [dbo].[Pedidos] DROP CONSTRAINT [PK_Pedidos];" }, s2);
        Assert.Contains("Remove a chave primária", p2.Summary);
    }

    [Fact]
    public void EditField_KeyFieldUnchanged_OrOnlyRenamed_DoesNotTouchKey()
    {
        var t = Table();
        var id = t.FindColumn("Id")!;
        var same = FieldDefinition.FromColumn(id);
        same.IsPrimaryKey = true;
        Assert.Empty(FieldDdlBuilder.BuildAlterColumn(t, id, same).Statements);

        var renamed = same.Clone();
        renamed.Name = "PedidoId";
        var (stmts, plan) = FieldDdlBuilder.BuildAlterColumn(t, id, renamed);
        Assert.True(plan.IsEmpty);
        Assert.Single(stmts);
        Assert.StartsWith("EXEC sys.sp_rename", stmts[0]);
    }

    [Fact]
    public void EditField_KeyFieldTypeChange_RecreatesKeyAroundAlter_WithNewName()
    {
        var t = Table();
        var id = t.FindColumn("Id")!;
        var updated = FieldDefinition.FromColumn(id);
        updated.IsPrimaryKey = true;
        updated.DataType = "bigint";
        updated.Name = "PedidoId";
        var (stmts, _) = FieldDdlBuilder.BuildAlterColumn(t, id, updated);
        Assert.Equal("ALTER TABLE [dbo].[Pedidos] DROP CONSTRAINT [PK_Pedidos];", stmts[0]);
        Assert.Equal("ALTER TABLE [dbo].[Pedidos] ALTER COLUMN [Id] bigint NOT NULL;", stmts[1]);
        Assert.StartsWith("EXEC sys.sp_rename", stmts[2]);
        Assert.Equal("ALTER TABLE [dbo].[Pedidos] ADD CONSTRAINT [PK_Pedidos] PRIMARY KEY CLUSTERED ([PedidoId]);", stmts[3]);
    }

    [Fact]
    public void Validate_KeyRules()
    {
        Assert.Equal("Um campo da chave primária não pode aceitar NULL.",
            FieldDdlBuilder.Validate(new FieldDefinition { Name = "a", IsNullable = true, IsPrimaryKey = true }, isNew: true));
        Assert.Contains("(max)", FieldDdlBuilder.Validate(new FieldDefinition { Name = "a", DataType = "nvarchar", Length = -1, IsNullable = false, IsPrimaryKey = true }, true));
        Assert.Contains("xml", FieldDdlBuilder.Validate(new FieldDefinition { Name = "a", DataType = "xml", IsNullable = false, IsPrimaryKey = true }, true));
        Assert.Null(FieldDdlBuilder.Validate(new FieldDefinition { Name = "a", DataType = "nvarchar", Length = 20, IsNullable = false, IsPrimaryKey = true }, true));
    }

    [Fact]
    public void DuplicateKeyWarning()
    {
        var f = new FieldDefinition { Name = "a", IsNullable = false, IsPrimaryKey = true, DefaultExpression = "0" };
        Assert.True(FieldDdlBuilder.NeedsDuplicateKeyWarning(f, 2));
        Assert.False(FieldDdlBuilder.NeedsDuplicateKeyWarning(f, 1));
        f.IsIdentity = true;
        Assert.False(FieldDdlBuilder.NeedsDuplicateKeyWarning(f, 5));
        var guid = new FieldDefinition { Name = "g", DataType = "uniqueidentifier", IsNullable = false, IsPrimaryKey = true, DefaultExpression = "newid()" };
        Assert.False(FieldDdlBuilder.NeedsDuplicateKeyWarning(guid, 5));
    }

    [Fact]
    public void LegacyOverloads_IgnoreTheKeyOption()
    {
        var f = new FieldDefinition { Name = "Codigo", IsNullable = false, IsPrimaryKey = true };
        Assert.Single(FieldDdlBuilder.BuildAddColumn("dbo", "Pedidos", f));
    }

    [Fact]
    public void ConstraintsTab_CreatePk_ValidatesAndMakesColumnsNotNull()
    {
        var t = Table(withPk: false);
        Assert.Equal("Escolha ao menos um campo.", PrimaryKeyDdlBuilder.ValidateNewPrimaryKey(t, Array.Empty<string>(), "PK_x"));
        Assert.Contains("não existe", PrimaryKeyDdlBuilder.ValidateNewPrimaryKey(t, new[] { "Nope" }, "PK_x"));
        Assert.Contains("já tem", PrimaryKeyDdlBuilder.ValidateNewPrimaryKey(Table(), new[] { "Id" }, "PK_x"));
        Assert.Null(PrimaryKeyDdlBuilder.ValidateNewPrimaryKey(t, new[] { "Ano", "Obs" }, "PK_Pedidos"));
        var stmts = PrimaryKeyDdlBuilder.BuildCreatePrimaryKey(t, new[] { "Ano", "Obs" }, "PK_Pedidos", clustered: true);
        Assert.Equal(new[]
        {
            "ALTER TABLE [dbo].[Pedidos] ALTER COLUMN [Obs] nvarchar(50) NOT NULL;",
            "ALTER TABLE [dbo].[Pedidos] ADD CONSTRAINT [PK_Pedidos] PRIMARY KEY CLUSTERED ([Ano], [Obs]);"
        }, stmts);
    }

    [Fact]
    public void ConstraintsTab_DropBlockers_AndKinds()
    {
        var t = Table();
        t.ForeignKeys.Add(FkTo("Pedidos", "Id"));
        t.CheckConstraints.Add(new CheckConstraintStructure { Name = "CK_Ano", Definition = "([Ano]>(2000))" });
        Assert.Single(PrimaryKeyDdlBuilder.GetDropConstraintBlockers(t, "PK_Pedidos"));
        Assert.Empty(PrimaryKeyDdlBuilder.GetDropConstraintBlockers(t, "CK_Ano"));
        Assert.Equal("PRIMARY KEY", PrimaryKeyDdlBuilder.ConstraintKind(t, "pk_pedidos"));
        Assert.Equal("CHECK", PrimaryKeyDdlBuilder.ConstraintKind(t, "CK_Ano"));
        Assert.Null(PrimaryKeyDdlBuilder.ConstraintKind(t, "FK_Itens_Pedidos")); // declared on another table
        Assert.Equal("ALTER TABLE [dbo].[Pedidos] DROP CONSTRAINT [CK_Ano];", PrimaryKeyDdlBuilder.DropConstraint("dbo", "Pedidos", "CK_Ano"));
    }

    [Fact]
    public void PkName_TruncatedTo128()
    {
        var t = new TableStructure { Schema = "dbo", Name = new string('t', 128) };
        var n = PrimaryKeyDdlBuilder.NewPrimaryKeyName(t);
        Assert.Equal(128, n.Length);
        t.SchemaPrimaryKeyNames.Add(n);
        var n2 = PrimaryKeyDdlBuilder.NewPrimaryKeyName(t);
        Assert.Equal(128, n2.Length);
        Assert.EndsWith("_2", n2);
    }
}

/// <summary>
/// Live primary-key changes on a temporary table (sqlexpert_it_pk_…) created and dropped by the test in SqlExpertTeste.
/// Enabled by SQLEXPERT_IT_CONNSTR; no existing object is touched.
/// </summary>
public class PrimaryKeyIntegrationTests
{
    private readonly ITestOutputHelper _out;
    private static readonly string? ConnStr = Environment.GetEnvironmentVariable("SQLEXPERT_IT_CONNSTR");

    public PrimaryKeyIntegrationTests(ITestOutputHelper output) => _out = output;

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

    private static async Task ExecAsync(string sql)
    {
        await using var c = new SqlConnection(ConnStr);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task NewField_EditField_And_ConstraintsTab_OnTempTable()
    {
        if (string.IsNullOrWhiteSpace(ConnStr))
        {
            _out.WriteLine("SQLEXPERT_IT_CONNSTR não definido: teste de integração ignorado.");
            return;
        }
        var db = new SqlConnectionStringBuilder(ConnStr).InitialCatalog;
        if (!string.Equals(db, "SqlExpertTeste", StringComparison.OrdinalIgnoreCase))
        {
            _out.WriteLine($"Banco {db} não é SqlExpertTeste: teste ignorado.");
            return;
        }
        var name = "sqlexpert_it_pk_" + Guid.NewGuid().ToString("N")[..10];
        var child = name + "_child";
        var svc = new TableStructureService(new ConnStrFactory());
        Task<TableStructure?> Load() => svc.LoadAsync("", db, "dbo", name);
        try
        {
            await ExecAsync($"CREATE TABLE dbo.[{name}] (A int NOT NULL, B nvarchar(20) NULL CONSTRAINT [DF_{name}_B] DEFAULT (N'x'));");

            // 1) New field with Primary key on a table without key → PK_<table> with the new field
            var t = (await Load())!;
            var (s1, p1) = FieldDdlBuilder.BuildAddColumn(t, new FieldDefinition { Name = "Id", DataType = "int", IsNullable = false, IsIdentity = true, IsPrimaryKey = true });
            Assert.False(p1.IsBlocked);
            await svc.ExecuteInTransactionAsync("", db, s1);
            t = (await Load())!;
            var pk = PrimaryKeyDdlBuilder.PrimaryKey(t)!;
            Assert.Equal("PK_" + name, pk.Name);
            Assert.True(pk.IsClustered);
            Assert.Equal(new[] { "Id" }, pk.Columns.Select(c => c.Name));

            // 2) Edit field A: include it in the existing key → composite (Id, A), same name
            var a = t.FindColumn("A")!;
            var ua = FieldDefinition.FromColumn(a);
            ua.IsPrimaryKey = true;
            var (s2, p2) = FieldDdlBuilder.BuildAlterColumn(t, a, ua);
            Assert.True(p2.RecreatesExistingKey);
            await svc.ExecuteInTransactionAsync("", db, s2);
            t = (await Load())!;
            Assert.Equal(new[] { "Id", "A" }, PrimaryKeyDdlBuilder.PrimaryKey(t)!.Columns.Select(c => c.Name));

            // 3) Edit field B (nullable, with default): into the key → NOT NULL + key (Id, A, B)
            await ExecAsync($"UPDATE dbo.[{name}] SET B = N'x' WHERE B IS NULL;");
            var b = t.FindColumn("B")!;
            var ub = FieldDefinition.FromColumn(b);
            ub.IsPrimaryKey = true;
            ub.IsNullable = false;
            await svc.ExecuteInTransactionAsync("", db, FieldDdlBuilder.BuildAlterColumn(t, b, ub).Statements);
            t = (await Load())!;
            Assert.False(t.FindColumn("B")!.IsNullable);
            Assert.Equal(new[] { "Id", "A", "B" }, PrimaryKeyDdlBuilder.PrimaryKey(t)!.Columns.Select(c => c.Name));
            Assert.Equal("N'x'", SqlText.StripOuterParens(t.FindColumn("B")!.DefaultDefinition));

            // 4) A foreign key referencing the key blocks changes (and Drop constraint)
            await ExecAsync($"CREATE TABLE dbo.[{child}] (Id int NOT NULL, A int NOT NULL, B nvarchar(20) NOT NULL, " +
                            $"CONSTRAINT [FK_{child}] FOREIGN KEY (Id, A, B) REFERENCES dbo.[{name}] (Id, A, B));");
            t = (await Load())!;
            var bb = t.FindColumn("B")!;
            var out1 = FieldDefinition.FromColumn(bb);
            Assert.True(FieldDdlBuilder.BuildAlterColumn(t, bb, out1).Key.IsBlocked);
            Assert.NotEmpty(PrimaryKeyDdlBuilder.GetDropConstraintBlockers(t, "PK_" + name));
            await ExecAsync($"DROP TABLE dbo.[{child}];");

            // 5) Edit field B: out of the key → (Id, A)
            t = (await Load())!;
            bb = t.FindColumn("B")!;
            await svc.ExecuteInTransactionAsync("", db, FieldDdlBuilder.BuildAlterColumn(t, bb, FieldDefinition.FromColumn(bb)).Statements);
            t = (await Load())!;
            Assert.Equal(new[] { "Id", "A" }, PrimaryKeyDdlBuilder.PrimaryKey(t)!.Columns.Select(c => c.Name));

            // 6) Constraints tab: drop the key, then create it again on (A) NONCLUSTERED
            await svc.ExecuteInTransactionAsync("", db, new[] { PrimaryKeyDdlBuilder.DropConstraint("dbo", name, "PK_" + name) });
            t = (await Load())!;
            Assert.Null(PrimaryKeyDdlBuilder.PrimaryKey(t));
            var newName = PrimaryKeyDdlBuilder.NewPrimaryKeyName(t);
            Assert.Null(PrimaryKeyDdlBuilder.ValidateNewPrimaryKey(t, new[] { "A" }, newName));
            await svc.ExecuteInTransactionAsync("", db, PrimaryKeyDdlBuilder.BuildCreatePrimaryKey(t, new[] { "A" }, newName, clustered: false));
            t = (await Load())!;
            var again = PrimaryKeyDdlBuilder.PrimaryKey(t)!;
            Assert.False(again.IsClustered);
            Assert.Equal(new[] { "A" }, again.Columns.Select(c => c.Name));
            _out.WriteLine($"ok: {name} → {again.Name}");
        }
        finally
        {
            await ExecAsync($"IF OBJECT_ID(N'dbo.[{child}]', N'U') IS NOT NULL DROP TABLE dbo.[{child}]; IF OBJECT_ID(N'dbo.[{name}]', N'U') IS NOT NULL DROP TABLE dbo.[{name}];");
        }
    }
}
