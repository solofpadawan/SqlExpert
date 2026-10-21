using Microsoft.Data.SqlClient;
using SqlExpert.Core.Procedures;
using SqlExpert.Core.SqlEditor;
using Xunit.Abstractions;

namespace SqlExpert.Tests;

/// <summary>
/// Live tests of the procedure editor services against a real SQL Server. Each test creates its own procedure
/// with a unique name (sqlexpert_it_…), works only on it and drops it in a finally block; no existing object is touched.
/// Enabled by SQLEXPERT_IT_CONNSTR (see <see cref="SqlEditorSessionIntegrationTests"/>); without it the tests return.
/// </summary>
public class ProcedureIntegrationTests
{
    private readonly ITestOutputHelper _out;
    private static readonly string? ConnStr = Environment.GetEnvironmentVariable("SQLEXPERT_IT_CONNSTR");

    public ProcedureIntegrationTests(ITestOutputHelper output) => _out = output;

    private bool Enabled()
    {
        if (!string.IsNullOrWhiteSpace(ConnStr)) return true;
        _out.WriteLine("SQLEXPERT_IT_CONNSTR não definido: teste de integração ignorado.");
        return false;
    }

    private static SqlEditorSession NewSession(bool autocommit = true) =>
        new(async ct =>
        {
            var c = new SqlConnection(ConnStr);
            await c.OpenAsync(ct);
            return c;
        }, new SqlConnectionStringBuilder(ConnStr).InitialCatalog) { Autocommit = autocommit };

    private static string UniqueName() => "sqlexpert_it_" + Guid.NewGuid().ToString("N")[..12];

    private static async Task DropAsync(string name)
    {
        await using var c = new SqlConnection(ConnStr);
        await c.OpenAsync();
        await using var cmd = new SqlCommand($"IF OBJECT_ID(N'dbo.{name}', N'P') IS NOT NULL DROP PROCEDURE {ProcedureScriptBuilder.QualifiedName("dbo", name)};", c);
        await cmd.ExecuteNonQueryAsync();
    }

    private static ProcedureDefinition Definition(string name, string body) => new()
    {
        Schema = "dbo",
        Name = name,
        Parameters =
        {
            new ProcedureParameter { Name = "@a", TypeName = "int", Description = "valor de entrada" },
            new ProcedureParameter { Name = "@b", TypeName = "nvarchar", Size = "50", Default = "N'padrão'" },
            new ProcedureParameter { Name = "@c", TypeName = "int", IsOutput = true }
        },
        Body = body
    };

    private const string Body1 = "BEGIN\r\n    SET NOCOUNT ON;\r\n    SET @c = @a * 2;\r\n    SELECT @b AS b;\r\n    RETURN @a + 1;\r\nEND\r\n";

    [Fact]
    public async Task Create_Read_Alter_Execute_Describe()
    {
        if (!Enabled()) return;
        var name = UniqueName();
        try
        {
            using var s = NewSession();
            var version = await s.RunOnConnectionAsync((c, ct) => ProcedureCatalog.GetServerVersionAsync(c, ct));
            _out.WriteLine("Servidor: " + version);
            Assert.False(await s.RunOnConnectionAsync((c, ct) => ProcedureCatalog.ExistsAsync(c, "dbo", name, ct)));

            // Create
            var def = Definition(name, Body1);
            var create = await s.ExecuteAsync(new SqlExecutionRequest { Sql = ProcedureScriptBuilder.BuildScript(def, ProcedureScriptBuilder.CompileVerb(version, false)) });
            Assert.False(create.HasErrors, string.Join(" | ", create.Messages.Select(m => m.Text)));
            Assert.True(await s.RunOnConnectionAsync((c, ct) => ProcedureCatalog.ExistsAsync(c, "dbo", name, ct)));

            // Parameter + procedure descriptions
            var descr = await s.ExecuteAsync(new SqlExecutionRequest
            {
                Sql = ProcedureScriptBuilder.BuildParameterDescriptions(def, null) +
                      ProcedureScriptBuilder.BuildDescriptionStatement("dbo", name, null, "procedure de teste")
            });
            Assert.False(descr.HasErrors, string.Join(" | ", descr.Messages.Select(m => m.Text)));

            // Read back
            var info = await s.RunOnConnectionAsync((c, ct) => ProcedureCatalog.LoadAsync(c, "dbo", name, ct));
            Assert.NotNull(info);
            Assert.Equal("procedure de teste", info!.Description);
            Assert.Equal("valor de entrada", info.ParameterDescriptions["@a"]);
            var parsed = ProcedureScriptParser.Parse(info.Definition);
            Assert.True(parsed.Success, string.Join(" | ", parsed.Errors));
            Assert.Equal(new[] { "@a", "@b", "@c" }, parsed.Definition!.Parameters.Select(p => p.Name));
            Assert.True(parsed.Definition.Parameters[2].IsOutput);
            Assert.Equal("N'padrão'", parsed.Definition.Parameters[1].Default);
            Assert.Contains("SET @c = @a * 2;", parsed.Definition.Body);

            // Alter (same verb rules as the editor: the object exists now)
            parsed.Definition.Body = parsed.Definition.Body.Replace("RETURN @a + 1;", "RETURN @a + 5;");
            var alter = await s.ExecuteAsync(new SqlExecutionRequest { Sql = ProcedureScriptBuilder.BuildScript(parsed.Definition, ProcedureScriptBuilder.CompileVerb(version, true)) });
            Assert.False(alter.HasErrors, string.Join(" | ", alter.Messages.Select(m => m.Text)));
            var info2 = await s.RunOnConnectionAsync((c, ct) => ProcedureCatalog.LoadAsync(c, "dbo", name, ct));
            Assert.Contains("RETURN @a + 5;", info2!.Definition);

            // Execute: @a = 7, @b default, @c OUTPUT
            var p = parsed.Definition.Parameters;
            var exec = ProcedureScriptBuilder.BuildExecuteBatch("dbo", name, new[]
            {
                new ProcedureArgument(p[0], ArgumentMode.Value, "7"),
                new ProcedureArgument(p[1], ArgumentMode.Default),
                new ProcedureArgument(p[2], ArgumentMode.Null)
            });
            var r = await s.ExecuteAsync(new SqlExecutionRequest { Sql = exec, StopOnError = false });
            Assert.False(r.HasErrors, string.Join(" | ", r.Messages.Select(m => m.Text)));
            Assert.Equal(2, r.ResultSets.Count);
            Assert.Equal("padrão", r.ResultSets[0].Rows[0][0]);
            var outputs = r.ResultSets[1];
            Assert.Equal(new[] { "RETURN_VALUE", "@c" }, outputs.Columns.Select(c => c.Name));
            Assert.Equal(12, outputs.Rows[0][0]);
            Assert.Equal(14, outputs.Rows[0][1]);

            // Other tabs
            var deps = await s.RunOnConnectionAsync((c, ct) => ProcedureCatalog.GetDependenciesAsync(c, "dbo", name, ct));
            Assert.NotNull(deps);
            var grants = await s.RunOnConnectionAsync((c, ct) => ProcedureCatalog.GetPermissionsAsync(c, "dbo", name, ct));
            var ddl = ProcedureCatalog.BuildDdl(info2, grants);
            Assert.Contains(name, ddl);
            Assert.Contains("sp_addextendedproperty", ddl);

            // Recompile (sp_refreshsqlmodule) of this procedure only
            await using (var c = new SqlConnection(ConnStr))
            {
                await c.OpenAsync();
                var run = await ProcedureCatalog.RunAsync(c, new[] { ProcedureCatalog.BuildTarget(ProcedureCatalog.MaintenanceKind.RecompileProcedures, "dbo", name) });
                Assert.Equal(1, run.Done);
                Assert.Empty(run.Failures);
            }
        }
        finally
        {
            await DropAsync(name);
        }
    }

    [Fact]
    public async Task CompileError_IsMappedToBodyLine()
    {
        if (!Enabled()) return;
        var name = UniqueName();
        try
        {
            using var s = NewSession();
            var version = await s.RunOnConnectionAsync((c, ct) => ProcedureCatalog.GetServerVersionAsync(c, ct));
            // Syntax error on body line 4.
            var def = Definition(name, "BEGIN\r\n    SET NOCOUNT ON;\r\n    SELECT 1;\r\n    SELECT FROM WHERE;\r\nEND\r\n");
            var verb = ProcedureScriptBuilder.CompileVerb(version, false);
            var r = await s.ExecuteAsync(new SqlExecutionRequest { Sql = ProcedureScriptBuilder.BuildScript(def, verb) });
            Assert.True(r.HasErrors);
            var mapped = ProcedureErrorLines.MapToBody(r.Messages, ProcedureScriptBuilder.BodyStartLine(def, verb));
            var error = mapped.First(m => m.Kind == SqlMessageKind.Error);
            _out.WriteLine(error.Text);
            Assert.Equal(4, error.EditorLine);
            Assert.False(await s.RunOnConnectionAsync((c, ct) => ProcedureCatalog.ExistsAsync(c, "dbo", name, ct)));
        }
        finally
        {
            await DropAsync(name);
        }
    }

    [Fact]
    public async Task CompileInsideTransaction_RollbackRemovesIt()
    {
        if (!Enabled()) return;
        var name = UniqueName();
        try
        {
            using var s = NewSession(autocommit: false);
            var version = await s.RunOnConnectionAsync((c, ct) => ProcedureCatalog.GetServerVersionAsync(c, ct));
            var r = await s.ExecuteAsync(new SqlExecutionRequest { Sql = ProcedureScriptBuilder.BuildScript(Definition(name, Body1), ProcedureScriptBuilder.CompileVerb(version, false)) });
            Assert.False(r.HasErrors, string.Join(" | ", r.Messages.Select(m => m.Text)));
            Assert.True(s.InTransaction);
            // Visible on the editor's own connection while the transaction is open…
            Assert.True(await s.RunOnConnectionAsync((c, ct) => ProcedureCatalog.ExistsAsync(c, "dbo", name, ct)));
            await s.RollbackAsync();
            Assert.False(s.InTransaction);
            // …and gone after Rollback.
            Assert.False(await s.RunOnConnectionAsync((c, ct) => ProcedureCatalog.ExistsAsync(c, "dbo", name, ct)));
        }
        finally
        {
            await DropAsync(name);
        }
    }
}
