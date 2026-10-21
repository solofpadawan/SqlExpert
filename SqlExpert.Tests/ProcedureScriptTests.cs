using SqlExpert.Core.Procedures;
using SqlExpert.Core.SqlEditor;

namespace SqlExpert.Tests;

/// <summary>Procedure editor core: ScriptDom split/merge, header generation, error line mapping, template, EXEC batch.</summary>
public class ProcedureScriptTests
{
    private static string N(string s) => s.Replace("\r\n", "\n");

    private static ProcedureDefinition ParseOk(string sql)
    {
        var r = ProcedureScriptParser.Parse(sql);
        Assert.True(r.Success, string.Join(" | ", r.Errors));
        return r.Definition!;
    }

    /// <summary>split → header from the grid + original body → split again gives the same parts.</summary>
    private static void AssertRoundTrip(ProcedureDefinition def)
    {
        var script = ProcedureScriptBuilder.BuildScript(def, ProcedureVerb.CreateOrAlter);
        var again = ParseOk(script);
        Assert.Equal(def.Schema, again.Schema);
        Assert.Equal(def.Name, again.Name);
        Assert.Equal(N(def.Body), N(again.Body));
        Assert.Equal(def.Options, again.Options);
        Assert.Equal(def.ForReplication, again.ForReplication);
        Assert.Equal(N(def.Preamble ?? ""), N(again.Preamble ?? ""));
        Assert.Equal(def.Parameters.Count, again.Parameters.Count);
        for (var i = 0; i < def.Parameters.Count; i++)
        {
            var a = def.Parameters[i];
            var b = again.Parameters[i];
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.FullType, b.FullType);
            Assert.Equal(a.Default, b.Default);
            Assert.Equal(a.IsOutput, b.IsOutput);
            Assert.Equal(a.IsReadOnly, b.IsReadOnly);
        }
        Assert.Equal(ProcedureVerb.CreateOrAlter, again.SourceVerb);
    }

    [Fact]
    public void Parse_ParamsWithDefaultsAndOutput()
    {
        var def = ParseOk("CREATE PROCEDURE dbo.AtualizaCodigo\r\n    @id_item INTEGER,\r\n    @cod_ean VARCHAR(16) = NULL,\r\n" +
                          "    @valor decimal(18, 2) = 0.5,\r\n    @total int OUTPUT,\r\n    @nome nvarchar(max) = N'x''y' OUT\r\nAS\r\nBEGIN\r\n    SET NOCOUNT ON;\r\n    UPDATE t SET c = @cod_ean WHERE id = @id_item;\r\nEND\r\n");
        Assert.Equal("dbo", def.Schema);
        Assert.Equal("AtualizaCodigo", def.Name);
        Assert.Equal(5, def.Parameters.Count);
        Assert.Equal(("@id_item", "INTEGER", (string?)null, (string?)null), (def.Parameters[0].Name, def.Parameters[0].TypeName, def.Parameters[0].Size, def.Parameters[0].Default));
        Assert.Equal(("VARCHAR", "16", "NULL"), (def.Parameters[1].TypeName, def.Parameters[1].Size, def.Parameters[1].Default));
        Assert.Equal(("decimal", "18", "2", "0.5"), (def.Parameters[2].TypeName, def.Parameters[2].Size, def.Parameters[2].Scale, def.Parameters[2].Default));
        Assert.True(def.Parameters[3].IsOutput);
        Assert.Equal(("nvarchar", "max", "N'x''y'"), (def.Parameters[4].TypeName, def.Parameters[4].Size, def.Parameters[4].Default));
        Assert.True(def.Parameters[4].IsOutput);
        Assert.Equal("BEGIN\r\n    SET NOCOUNT ON;\r\n    UPDATE t SET c = @cod_ean WHERE id = @id_item;\r\nEND\r\n", def.Body);
        Assert.False(def.HeaderHasComments);
        AssertRoundTrip(def);
    }

    [Fact]
    public void Parse_ReadOnlyTvp()
    {
        var def = ParseOk("CREATE PROC [dbo].[Importa] @linhas dbo.TipoLinhas READONLY, @modo tinyint = 1 AS SELECT COUNT(*) FROM @linhas;");
        Assert.Equal("dbo.TipoLinhas", def.Parameters[0].TypeName);
        Assert.True(def.Parameters[0].IsReadOnly);
        Assert.False(def.Parameters[0].IsOutput);
        Assert.Equal("SELECT COUNT(*) FROM @linhas;", def.Body);
        Assert.Contains("@linhas dbo.TipoLinhas READONLY", ProcedureScriptBuilder.BuildHeader(def, ProcedureVerb.Create));
        AssertRoundTrip(def);
    }

    [Theory]
    [InlineData("WITH RECOMPILE", "RECOMPILE")]
    [InlineData("WITH EXECUTE AS OWNER", "EXECUTE AS OWNER")]
    [InlineData("WITH RECOMPILE, EXECUTE AS 'dbo'", "RECOMPILE, EXECUTE AS 'dbo'")]
    [InlineData("WITH EXECUTE AS CALLER, ENCRYPTION", "EXECUTE AS CALLER, ENCRYPTION")]
    public void Parse_WithOptions(string with, string options)
    {
        var def = ParseOk($"CREATE PROCEDURE dbo.P @a int\n{with}\nAS\nSELECT @a;");
        Assert.Equal(options, def.Options);
        Assert.Equal("SELECT @a;", def.Body);
        AssertRoundTrip(def);
    }

    [Fact]
    public void Parse_ForReplication()
    {
        var def = ParseOk("CREATE PROCEDURE dbo.R FOR REPLICATION AS SELECT 1;");
        Assert.True(def.ForReplication);
        Assert.EndsWith("FOR REPLICATION\r\nAS", ProcedureScriptBuilder.BuildHeader(def, ProcedureVerb.Alter));
        AssertRoundTrip(def);
    }

    [Fact]
    public void Parse_CommentsBeforeAndInside_ArePreserved()
    {
        var sql = "/* =============\n   Autor: equipe\n   ============= */\n-- linha solta\nCREATE PROCEDURE dbo.Comentada\n    @x int\nAS\n" +
                  "-- comentário logo depois do AS\nBEGIN\n    /* bloco\n       de várias linhas */\n    SELECT @x; -- fim de linha\n\n\n    RETURN 0;\nEND\n-- comentário depois do END\n";
        var def = ParseOk(sql);
        Assert.Equal("/* =============\n   Autor: equipe\n   ============= */\n-- linha solta\n", def.Preamble);
        Assert.Equal("-- comentário logo depois do AS\nBEGIN\n    /* bloco\n       de várias linhas */\n    SELECT @x; -- fim de linha\n\n\n    RETURN 0;\nEND\n-- comentário depois do END\n", def.Body);
        Assert.False(def.HeaderHasComments);
        AssertRoundTrip(def);
    }

    [Fact]
    public void Parse_CommentsInsideHeader_AreFlagged()
    {
        var def = ParseOk("CREATE PROCEDURE dbo.H\n    @a int, -- o código\n    /* nome */ @b varchar(10)\nAS\nSELECT 1;");
        Assert.True(def.HeaderHasComments);
        Assert.Equal(2, def.Parameters.Count);
    }

    [Fact]
    public void Parse_Brackets_AndQuotedNames()
    {
        var def = ParseOk("CREATE PROCEDURE [Vendas Online].[Relatório ]]Mensal] @ano int AS SELECT 1");
        Assert.Equal("Vendas Online", def.Schema);
        Assert.Equal("Relatório ]Mensal", def.Name);
        Assert.Equal("@ano", def.Parameters[0].Name);
        Assert.StartsWith("CREATE OR ALTER PROCEDURE [Vendas Online].[Relatório ]]Mensal]", ProcedureScriptBuilder.BuildHeader(def, ProcedureVerb.CreateOrAlter));
        Assert.Equal("Relatório ]Mensal", ParseOk(ProcedureScriptBuilder.BuildScript(def, ProcedureVerb.Create)).Name);
    }

    [Fact]
    public void Parse_NoParams_NoSchema()
    {
        var def = ParseOk("create procedure SemParametros\nas\nbegin\n  select getdate()\nend");
        Assert.Equal("dbo", def.Schema);
        Assert.Empty(def.Parameters);
        Assert.Equal("begin\n  select getdate()\nend", def.Body);
        Assert.Equal("CREATE PROCEDURE [dbo].[SemParametros]\r\nAS", ProcedureScriptBuilder.BuildHeader(def, ProcedureVerb.Create));
        AssertRoundTrip(def);
    }

    [Theory]
    [InlineData("ALTER PROCEDURE dbo.A AS SELECT 1", ProcedureVerb.Alter)]
    [InlineData("CREATE OR ALTER PROCEDURE dbo.A AS SELECT 1", ProcedureVerb.CreateOrAlter)]
    [InlineData("CREATE PROC dbo.A AS SELECT 1", ProcedureVerb.Create)]
    public void Parse_Verbs(string sql, ProcedureVerb verb)
    {
        var def = ParseOk(sql);
        Assert.Equal(verb, def.SourceVerb);
        Assert.Equal("SELECT 1", def.Body);
    }

    [Fact]
    public void Parse_AsOnSameLineAsBody_KeepsBody()
    {
        var def = ParseOk("CREATE PROCEDURE dbo.L @a int AS SET NOCOUNT ON; SELECT @a;\nSELECT 2;");
        Assert.Equal("SET NOCOUNT ON; SELECT @a;\nSELECT 2;", def.Body);
    }

    [Fact]
    public void Parse_TrailingGo_IsNotPartOfBody()
    {
        var def = ParseOk("CREATE PROCEDURE dbo.G AS\nBEGIN\n  SELECT 1;\nEND\nGO\n");
        Assert.Equal("BEGIN\n  SELECT 1;\nEND", def.Body);
    }

    [Theory]
    [InlineData("CREATE PROCEDURE dbo.X AS SELECT FROM WHERE")]                     // syntax error
    [InlineData("SELECT 1")]                                                       // not a procedure
    [InlineData("CREATE PROCEDURE dbo.A AS SELECT 1\nGO\nCREATE PROCEDURE dbo.B AS SELECT 2")] // two statements
    [InlineData("CREATE PROCEDURE dbo.Clr @a int AS EXTERNAL NAME Asm.Cls.Metodo")] // CLR
    [InlineData("CREATE PROCEDURE dbo.Num;2 AS SELECT 1")]                          // numbered
    [InlineData("CREATE PROCEDURE dbo.Cur @c CURSOR VARYING OUTPUT AS SELECT 1")]   // cursor varying
    [InlineData("")]
    public void Parse_Unsupported_FallsBack(string sql)
    {
        var r = ProcedureScriptParser.Parse(sql);
        Assert.False(r.Success);
        Assert.NotEmpty(r.Errors);
    }

    [Fact]
    public void RewriteVerb_ChangesOnlyTheVerb()
    {
        const string sql = "-- cabeçalho\nCREATE   PROC dbo.V @a int\nAS\nSELECT @a; -- PROCEDURE no comentário";
        Assert.Equal("-- cabeçalho\nALTER PROCEDURE dbo.V @a int\nAS\nSELECT @a; -- PROCEDURE no comentário", ProcedureScriptParser.RewriteVerb(sql, ProcedureVerb.Alter));
        Assert.StartsWith("CREATE OR ALTER PROCEDURE dbo.V", ProcedureScriptParser.RewriteVerb("ALTER PROCEDURE dbo.V AS SELECT 1", ProcedureVerb.CreateOrAlter));
        Assert.Null(ProcedureScriptParser.RewriteVerb("SELECT 1", ProcedureVerb.Alter));
        Assert.Equal(("dbo", "V"), ProcedureScriptParser.ParseName(sql));
    }

    // ===== header generation =====

    [Fact]
    public void BuildHeader_FromGrid()
    {
        var def = new ProcedureDefinition { Schema = "vendas", Name = "Fecha" };
        def.Parameters.Add(new ProcedureParameter { Name = "id", TypeName = "int" });
        def.Parameters.Add(new ProcedureParameter { Name = "@valor", TypeName = "decimal", Size = "18", Scale = "2", Default = "0" });
        def.Parameters.Add(new ProcedureParameter { Name = "@msg", TypeName = "nvarchar", Size = "max", IsOutput = true });
        def.Parameters.Add(new ProcedureParameter { Name = "", TypeName = "int" }); // blank row is ignored
        def.Options = "RECOMPILE";
        Assert.Equal("CREATE OR ALTER PROCEDURE [vendas].[Fecha]\r\n    @id int,\r\n    @valor decimal(18,2) = 0,\r\n    @msg nvarchar(max) OUTPUT\r\nWITH RECOMPILE\r\nAS",
            ProcedureScriptBuilder.BuildHeader(def, ProcedureVerb.CreateOrAlter));
        Assert.Equal("@id", def.Parameters[0].Name);
    }

    [Fact]
    public void Parameter_OutputAndReadOnly_AreExclusive()
    {
        var p = new ProcedureParameter { Name = "@t", TypeName = "dbo.Lista", IsReadOnly = true };
        p.IsOutput = true;
        Assert.False(p.IsReadOnly);
        p.IsReadOnly = true;
        Assert.False(p.IsOutput);
        var changes = 0;
        p.Changed += (_, _) => changes++;
        p.Size = "10";
        p.Size = "10";
        Assert.Equal(1, changes);
        Assert.Equal("dbo.Lista(10)", p.FullType);
    }

    [Theory]
    [InlineData("16.0.1000.6", true)]
    [InlineData("13.0.4001.0", true)]
    [InlineData("13.0.1601.5", false)]
    [InlineData("12.0.6024.0", false)]
    [InlineData(null, true)]
    public void CreateOrAlter_NeedsSql2016Sp1(string? version, bool expected)
    {
        Assert.Equal(expected, ProcedureScriptBuilder.SupportsCreateOrAlter(version));
        Assert.Equal(expected ? ProcedureVerb.CreateOrAlter : ProcedureVerb.Alter, ProcedureScriptBuilder.CompileVerb(version, exists: true));
        Assert.Equal(expected ? ProcedureVerb.CreateOrAlter : ProcedureVerb.Create, ProcedureScriptBuilder.CompileVerb(version, exists: false));
    }

    // ===== template =====

    [Fact]
    public void NewTemplate_IsNewProcedureWithNoCountBody()
    {
        var def = ProcedureScriptBuilder.NewTemplate("vendas");
        Assert.Equal("NEW_PROCEDURE", def.Name);
        Assert.Equal("vendas", def.Schema);
        Assert.Empty(def.Parameters);
        Assert.StartsWith("BEGIN\r\n    SET NOCOUNT ON;", def.Body);
        Assert.EndsWith("END\r\n", def.Body);
        Assert.Equal("dbo", ProcedureScriptBuilder.NewTemplate(null).Schema);
        var script = ProcedureScriptBuilder.BuildScript(def, ProcedureVerb.Create);
        Assert.StartsWith("CREATE PROCEDURE [vendas].[NEW_PROCEDURE]\r\nAS\r\nBEGIN", script);
        Assert.Empty(ProcedureScriptParser.SyntaxErrors(script));
        AssertRoundTrip(def);
    }

    // ===== error line mapping =====

    [Fact]
    public void BodyStartLine_CountsPreambleAndHeader()
    {
        var def = new ProcedureDefinition { Name = "E", Body = "BEGIN\r\n  SELECT 1;\r\nEND" };
        Assert.Equal(3, ProcedureScriptBuilder.BodyStartLine(def, ProcedureVerb.Create)); // CREATE / AS / body
        def.Parameters.Add(new ProcedureParameter { Name = "@a", TypeName = "int" });
        def.Parameters.Add(new ProcedureParameter { Name = "@b", TypeName = "int" });
        Assert.Equal(5, ProcedureScriptBuilder.BodyStartLine(def, ProcedureVerb.Create));
        def.Preamble = "-- a\r\n-- b\r\n";
        Assert.Equal(7, ProcedureScriptBuilder.BodyStartLine(def, ProcedureVerb.Create));
        var script = ProcedureScriptBuilder.BuildScript(def, ProcedureVerb.Create).Replace("\r\n", "\n").Split('\n');
        Assert.Equal("BEGIN", script[7 - 1]);
    }

    [Theory]
    [InlineData(5, 5, 1)]
    [InlineData(9, 5, 5)]
    [InlineData(4, 5, null)]
    [InlineData(0, 5, null)]
    public void ToBodyLine_MapsScriptLines(int scriptLine, int bodyStart, int? expected)
        => Assert.Equal(expected, ProcedureErrorLines.ToBodyLine(scriptLine, bodyStart));

    [Fact]
    public void MapToBody_RewritesErrorLines()
    {
        var msgs = new[]
        {
            new SqlMessage(SqlMessageKind.Error, "Msg 207, Level 16, State 1, Procedure E, Line 7\nInvalid column name 'x'.", EditorLine: 1, Number: 207, Severity: 16, TextLine: 7),
            new SqlMessage(SqlMessageKind.Error, "Msg 2715 …", EditorLine: 1, TextLine: 2),
            new SqlMessage(SqlMessageKind.Info, "PRINT")
        };
        var mapped = ProcedureErrorLines.MapToBody(msgs, bodyStartLine: 5);
        Assert.Equal(3, mapped[0].EditorLine);
        Assert.StartsWith("[corpo, linha 3]", mapped[0].Text);
        Assert.Equal(1, mapped[1].EditorLine);
        Assert.StartsWith("[cabeçalho", mapped[1].Text);
        Assert.Same(msgs[2], mapped[2]);
        Assert.Equal(7, ProcedureErrorLines.MapToFullText(msgs)[0].EditorLine);
    }

    // ===== declarations (Variables / Cursors) =====

    [Fact]
    public void FindDeclarations_VariablesAndCursors()
    {
        var body = "BEGIN\n  DECLARE @a int = 1, @b varchar(10);\n  DECLARE @t TABLE (id int);\n  DECLARE c CURSOR LOCAL FAST_FORWARD FOR\n    SELECT id FROM dbo.X;\n  SELECT @a;\nEND";
        var d = ProcedureScriptParser.FindDeclarations(body);
        Assert.NotNull(d);
        Assert.Equal(new[] { "@a", "@b", "@t" }, d!.Value.Variables.Select(v => v.Name));
        Assert.Equal("int", d.Value.Variables[0].Type);
        Assert.Equal("1", d.Value.Variables[0].Value);
        Assert.Equal(2, d.Value.Variables[0].Line);
        Assert.Equal("TABLE", d.Value.Variables[2].Type);
        var c = Assert.Single(d.Value.Cursors);
        Assert.Equal("c", c.Name);
        Assert.Equal(4, c.Line);
        Assert.Contains("SELECT id FROM dbo.X", c.Value);
        // Parameters are not variables.
        var full = ProcedureScriptParser.FindDeclarations("CREATE PROCEDURE dbo.P @p int AS DECLARE @v int; SELECT @v;");
        Assert.Equal(new[] { "@v" }, full!.Value.Variables.Select(v => v.Name));
        Assert.Null(ProcedureScriptParser.FindDeclarations("BEGIN SELECT FROM END"));
    }

    // ===== Execute =====

    [Fact]
    public void BuildExecuteBatch_InputOutputReturnAndTvp()
    {
        var id = new ProcedureParameter { Name = "@id", TypeName = "int" };
        var nome = new ProcedureParameter { Name = "@nome", TypeName = "nvarchar", Size = "50", Default = "NULL" };
        var total = new ProcedureParameter { Name = "@total", TypeName = "decimal", Size = "18", Scale = "2", IsOutput = true };
        var tvp = new ProcedureParameter { Name = "@lista", TypeName = "dbo.Lista", IsReadOnly = true };
        var flag = new ProcedureParameter { Name = "@flag", TypeName = "bit", Default = "0" };
        var sql = ProcedureScriptBuilder.BuildExecuteBatch("dbo", "Calc", new[]
        {
            new ProcedureArgument(id, ArgumentMode.Value, "42"),
            new ProcedureArgument(nome, ArgumentMode.Value, "O'Brien"),
            new ProcedureArgument(total, ArgumentMode.Value, "1,5"),
            new ProcedureArgument(tvp, ArgumentMode.Value),
            new ProcedureArgument(flag, ArgumentMode.Default)
        });
        Assert.Equal(
            "DECLARE @RETURN_VALUE int;\r\n" +
            "DECLARE @p3_total decimal(18,2) = 1.5;\r\n" +
            "DECLARE @p4_lista dbo.Lista;\r\n" +
            "EXEC @RETURN_VALUE = [dbo].[Calc]\r\n    @id = 42,\r\n    @nome = N'O''Brien',\r\n    @total = @p3_total OUTPUT,\r\n    @lista = @p4_lista;\r\n" +
            "SELECT @RETURN_VALUE AS [RETURN_VALUE], @p3_total AS [@total];", sql);
        Assert.Empty(ProcedureScriptParser.SyntaxErrors(sql));
    }

    [Fact]
    public void FormatValue_Literals()
    {
        var bin = new ProcedureParameter { Name = "@b", TypeName = "varbinary", Size = "max" };
        Assert.Equal("0x0A0B", ProcedureScriptBuilder.FormatValue(bin, "0x0A0B"));
        Assert.Equal("N'0xZZ'", ProcedureScriptBuilder.FormatValue(bin, "0xZZ"));
        var d = new ProcedureParameter { Name = "@d", TypeName = "date" };
        Assert.Equal("N'2026-10-21'", ProcedureScriptBuilder.FormatValue(d, "2026-10-21"));
        var i = new ProcedureParameter { Name = "@i", TypeName = "int" };
        Assert.Equal("N'abc'", ProcedureScriptBuilder.FormatValue(i, "abc"));
        Assert.Equal("NULL", ProcedureScriptBuilder.FormatValue(i, null));
    }

    // ===== description / drop =====

    [Fact]
    public void DescriptionStatements_AddUpdateDrop()
    {
        var add = ProcedureScriptBuilder.BuildDescriptionStatement("dbo", "P", null, "Atualiza o código");
        Assert.Contains("sp_updateextendedproperty", add);
        Assert.Contains("sp_addextendedproperty @name = N'MS_Description', @value = N'Atualiza o código', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'PROCEDURE', @level1name = N'P';", add);
        var drop = ProcedureScriptBuilder.BuildDescriptionStatement("dbo", "P", "@id", "  ");
        Assert.Contains("sp_dropextendedproperty", drop);
        Assert.Contains("@level2type = N'PARAMETER', @level2name = N'@id'", drop);
        Assert.Empty(ProcedureScriptParser.SyntaxErrors(add));
        Assert.Empty(ProcedureScriptParser.SyntaxErrors(drop));

        var def = new ProcedureDefinition { Name = "P" };
        def.Parameters.Add(new ProcedureParameter { Name = "@a", TypeName = "int", Description = "igual" });
        def.Parameters.Add(new ProcedureParameter { Name = "@b", TypeName = "int", Description = "nova" });
        var sync = ProcedureScriptBuilder.BuildParameterDescriptions(def, new Dictionary<string, string> { ["@a"] = "igual" });
        Assert.DoesNotContain("N'@a'", sync);
        Assert.Contains("N'@b'", sync);
        Assert.Equal("DROP PROCEDURE [dbo].[P];", ProcedureScriptBuilder.BuildDrop("dbo", "P"));
    }
}
