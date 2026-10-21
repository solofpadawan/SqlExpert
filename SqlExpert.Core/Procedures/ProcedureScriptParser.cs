using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlExpert.Core.Procedures;

/// <summary>
/// Splits a procedure definition (sys.sql_modules / OBJECT_DEFINITION, or text typed in the editor) into
/// name, parameters, WITH options and the body after AS, using the T-SQL parser of ScriptDom.
/// The body is cut from the original text, so its comments and formatting are kept exactly.
/// Anything that cannot be represented by the grid (CLR, numbered or natively compiled procedures, VARYING
/// cursor parameters, several statements…) fails, and the editor falls back to the full-text mode.
/// </summary>
public static class ProcedureScriptParser
{
    private static Dom.TSqlParser NewParser() => new Dom.TSql160Parser(initialQuotedIdentifiers: true);

    public static ProcedureParseResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Fail("Texto vazio.");

        var parsed = ParseSingleProcedure(text, out var error);
        if (parsed is null) return Fail(error!);
        var (stmt, tokens) = parsed.Value;

        if (stmt.MethodSpecifier is not null) return Fail("Procedure CLR (EXTERNAL NAME): edite no modo texto completo.");
        if (stmt.ProcedureReference?.Name is null) return Fail("Nome da procedure não encontrado.");
        if (stmt.ProcedureReference.Number is not null) return Fail("Procedure numerada (;n): edite no modo texto completo.");
        if (stmt.ProcedureReference.Name.DatabaseIdentifier is not null || stmt.ProcedureReference.Name.ServerIdentifier is not null)
            return Fail("Nome com database/servidor: edite no modo texto completo.");
        if (stmt.Options.Any(o => o.OptionKind == Dom.ProcedureOptionKind.NativeCompilation))
            return Fail("Procedure nativamente compilada: edite no modo texto completo.");

        var asIndex = FindAsToken(stmt, tokens);
        if (asIndex < 0) return Fail("Palavra-chave AS do cabeçalho não encontrada.");

        var def = new ProcedureDefinition
        {
            Schema = stmt.ProcedureReference.Name.SchemaIdentifier?.Value ?? "dbo",
            Name = stmt.ProcedureReference.Name.BaseIdentifier.Value,
            ForReplication = stmt.IsForReplication,
            SourceVerb = stmt switch
            {
                Dom.AlterProcedureStatement => ProcedureVerb.Alter,
                Dom.CreateOrAlterProcedureStatement => ProcedureVerb.CreateOrAlter,
                _ => ProcedureVerb.Create
            }
        };

        foreach (var p in stmt.Parameters)
        {
            if (p.IsVarying) return Fail($"Parâmetro {p.VariableName.Value} CURSOR VARYING: edite no modo texto completo.");
            if (p.Nullable is not null) return Fail($"Parâmetro {p.VariableName.Value} com NULL/NOT NULL: edite no modo texto completo.");
            var param = new ProcedureParameter { Name = p.VariableName.Value };
            SplitType(text, p.DataType, param);
            if (p.Value is not null) param.Default = Slice(text, p.Value);
            param.IsOutput = p.Modifier == Dom.ParameterModifier.Output;
            param.IsReadOnly = p.Modifier == Dom.ParameterModifier.ReadOnly;
            def.Parameters.Add(param);
        }

        if (stmt.Options.Count > 0)
            def.Options = OptionsText(text, stmt, tokens, asIndex);

        // Comments inside the header would be lost by a regenerated header: the editor opens in full-text mode.
        for (var i = stmt.FirstTokenIndex; i < asIndex; i++)
        {
            if (tokens[i].TokenType is Dom.TSqlTokenType.SingleLineComment or Dom.TSqlTokenType.MultilineComment)
            {
                def.HeaderHasComments = true;
                break;
            }
        }

        var preamble = text[..stmt.StartOffset];
        def.Preamble = string.IsNullOrWhiteSpace(preamble) && !preamble.Contains("--") && !preamble.Contains("/*") ? null : preamble;
        if (def.Preamble is not null && string.IsNullOrWhiteSpace(def.Preamble)) def.Preamble = null;

        var asToken = tokens[asIndex];
        var bodyStart = asToken.Offset + asToken.Text.Length;
        var bodyEnd = text.Length;
        for (var i = stmt.LastTokenIndex + 1; i < tokens.Count; i++)
        {
            if (tokens[i].TokenType == Dom.TSqlTokenType.Go)
            {
                bodyEnd = tokens[i].Offset;
                break;
            }
        }
        var body = text[bodyStart..bodyEnd];
        if (bodyEnd < text.Length) body = body.TrimEnd();
        def.Body = TrimBodyStart(body);
        return new ProcedureParseResult { Definition = def };
    }

    /// <summary>Drops the rest of the "AS" line: spaces/tabs, then one line break when the line ends there.</summary>
    internal static string TrimBodyStart(string body)
    {
        var i = 0;
        while (i < body.Length && (body[i] == ' ' || body[i] == '\t')) i++;
        if (i < body.Length && body[i] == '\r') { i++; if (i < body.Length && body[i] == '\n') i++; }
        else if (i < body.Length && body[i] == '\n') i++;
        return body[i..];
    }

    /// <summary>
    /// Replaces CREATE / ALTER / CREATE OR ALTER of a full procedure text with <paramref name="verb"/>
    /// (everything else untouched). Null when the text is not a single procedure statement.
    /// </summary>
    public static string? RewriteVerb(string text, ProcedureVerb verb)
    {
        var parsed = ParseSingleProcedure(text, out _);
        if (parsed is null) return null;
        var (stmt, tokens) = parsed.Value;
        for (var i = stmt.FirstTokenIndex; i <= stmt.LastTokenIndex; i++)
        {
            if (!IsProcedureKeyword(tokens[i])) continue;
            var end = tokens[i].Offset + tokens[i].Text.Length;
            return text[..stmt.StartOffset] + ProcedureScriptBuilder.VerbText(verb) + " PROCEDURE" + text[end..];
        }
        return null;
    }

    /// <summary>(schema, name) of a single procedure statement, or null.</summary>
    public static (string Schema, string Name)? ParseName(string text)
    {
        var parsed = ParseSingleProcedure(text, out _);
        var n = parsed?.Statement.ProcedureReference?.Name;
        return n is null ? null : (n.SchemaIdentifier?.Value ?? "dbo", n.BaseIdentifier.Value);
    }

    /// <summary>Syntax errors of a text ("Linha 3: Incorrect syntax near …"), empty when it parses.</summary>
    public static IReadOnlyList<string> SyntaxErrors(string text)
    {
        NewParser().Parse(new StringReader(text ?? ""), out var errors);
        return errors.Select(e => $"Linha {e.Line}: {e.Message}").ToList();
    }

    /// <summary>
    /// Variables (DECLARE @x …, including table variables) and cursors (DECLARE c CURSOR …) declared in a body or
    /// full text, with their 1-based line in that text. Null when the text does not parse.
    /// </summary>
    public static (IReadOnlyList<DeclaredItem> Variables, IReadOnlyList<DeclaredItem> Cursors)? FindDeclarations(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (Array.Empty<DeclaredItem>(), Array.Empty<DeclaredItem>());
        var fragment = NewParser().Parse(new StringReader(text), out var errors);
        if (errors.Count > 0 || fragment is null) return null;
        var v = new DeclarationVisitor(text);
        fragment.Accept(v);
        return (v.Variables, v.Cursors);
    }

    private sealed class DeclarationVisitor : Dom.TSqlFragmentVisitor
    {
        private readonly string _text;
        public DeclarationVisitor(string text) => _text = text;
        public List<DeclaredItem> Variables { get; } = new();
        public List<DeclaredItem> Cursors { get; } = new();

        public override void Visit(Dom.DeclareVariableElement node)
        {
            if (node is Dom.ProcedureParameter) return;
            Variables.Add(new DeclaredItem(node.VariableName.Value, node.DataType is null ? "" : Slice(_text, node.DataType),
                node.Value is null ? null : OneLine(Slice(_text, node.Value)), node.StartLine));
        }

        public override void Visit(Dom.DeclareTableVariableBody node)
        {
            if (node.VariableName is null) return;
            Variables.Add(new DeclaredItem(node.VariableName.Value, "TABLE", null, node.StartLine));
        }

        public override void Visit(Dom.DeclareCursorStatement node)
        {
            var select = node.CursorDefinition?.Select is { } s ? OneLine(Slice(_text, s)) : null;
            Cursors.Add(new DeclaredItem(node.Name.Value, "CURSOR", select, node.StartLine));
        }

        private static string OneLine(string s)
        {
            var t = string.Join(" ", s.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
            return t.Length > 200 ? t[..200] + "…" : t;
        }
    }

    // ===== helpers =====

    private static ProcedureParseResult Fail(string message) => new() { Errors = new[] { message } };

    private static (Dom.ProcedureStatementBody Statement, IList<Dom.TSqlParserToken> Tokens)? ParseSingleProcedure(string text, out string? error)
    {
        error = null;
        var fragment = NewParser().Parse(new StringReader(text), out var errors);
        if (errors.Count > 0)
        {
            error = string.Join(Environment.NewLine, errors.Take(5).Select(e => $"Linha {e.Line}: {e.Message}"));
            return null;
        }
        if (fragment is not Dom.TSqlScript script)
        {
            error = "Texto não reconhecido.";
            return null;
        }
        var statements = script.Batches.SelectMany(b => b.Statements).ToList();
        if (statements.Count != 1 || statements[0] is not Dom.ProcedureStatementBody stmt)
        {
            error = statements.Count == 1
                ? "O texto não é um CREATE/ALTER PROCEDURE."
                : $"O texto deve ter um único comando CREATE/ALTER PROCEDURE (encontrados {statements.Count}).";
            return null;
        }
        return (stmt, script.ScriptTokenStream);
    }

    private static int FindAsToken(Dom.ProcedureStatementBody stmt, IList<Dom.TSqlParserToken> tokens)
    {
        if (stmt.StatementList is { Statements.Count: > 0 } list)
        {
            for (var i = list.FirstTokenIndex - 1; i >= stmt.FirstTokenIndex; i--)
            {
                var t = tokens[i].TokenType;
                if (t is Dom.TSqlTokenType.WhiteSpace or Dom.TSqlTokenType.SingleLineComment or Dom.TSqlTokenType.MultilineComment) continue;
                return t == Dom.TSqlTokenType.As ? i : -1;
            }
            return -1;
        }
        // Empty body: the AS after the last header element.
        var from = stmt.ProcedureReference.LastTokenIndex;
        if (stmt.Parameters.Count > 0) from = Math.Max(from, stmt.Parameters[^1].LastTokenIndex);
        if (stmt.Options.Count > 0) from = Math.Max(from, stmt.Options[^1].LastTokenIndex);
        for (var i = from + 1; i <= stmt.LastTokenIndex && i < tokens.Count; i++)
            if (tokens[i].TokenType == Dom.TSqlTokenType.As) return i;
        return -1;
    }

    private static bool IsProcedureKeyword(Dom.TSqlParserToken t) =>
        t.TokenType is not (Dom.TSqlTokenType.SingleLineComment or Dom.TSqlTokenType.MultilineComment or Dom.TSqlTokenType.WhiteSpace)
        && (string.Equals(t.Text, "PROCEDURE", StringComparison.OrdinalIgnoreCase) || string.Equals(t.Text, "PROC", StringComparison.OrdinalIgnoreCase));

    private static bool IsTrivia(Dom.TSqlParserToken t) =>
        t.TokenType is Dom.TSqlTokenType.WhiteSpace or Dom.TSqlTokenType.SingleLineComment or Dom.TSqlTokenType.MultilineComment;

    /// <summary>
    /// Text of the WITH options, from the first option to the token before FOR REPLICATION / AS
    /// (option fragments do not always cover their whole text, e.g. EXECUTE AS OWNER).
    /// </summary>
    private static string OptionsText(string text, Dom.ProcedureStatementBody stmt, IList<Dom.TSqlParserToken> tokens, int asIndex)
    {
        var first = stmt.Options[0].FirstTokenIndex;
        var end = asIndex - 1;
        while (end > first && IsTrivia(tokens[end])) end--;
        if (stmt.IsForReplication && string.Equals(tokens[end].Text, "REPLICATION", StringComparison.OrdinalIgnoreCase))
        {
            end--;
            while (end > first && IsTrivia(tokens[end])) end--;
            if (string.Equals(tokens[end].Text, "FOR", StringComparison.OrdinalIgnoreCase)) end--;
            while (end > first && IsTrivia(tokens[end])) end--;
        }
        var start = tokens[first].Offset;
        var stop = tokens[end].Offset + tokens[end].Text.Length;
        return text[start..stop].Trim().TrimEnd(',').Trim();
    }

    private static string Slice(string text, Dom.TSqlFragment f) => text.Substring(f.StartOffset, f.FragmentLength);

    private static void SplitType(string text, Dom.DataTypeReference type, ProcedureParameter target)
    {
        if (type is Dom.ParameterizedDataTypeReference { Parameters.Count: > 0 } pt && pt.Name is not null)
        {
            target.TypeName = Slice(text, pt.Name);
            target.Size = Slice(text, pt.Parameters[0]);
            target.Scale = pt.Parameters.Count > 1 ? Slice(text, pt.Parameters[1]) : null;
            return;
        }
        target.TypeName = Slice(text, type);
    }
}
