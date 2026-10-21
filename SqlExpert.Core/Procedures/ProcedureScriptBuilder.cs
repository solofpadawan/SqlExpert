using System.Globalization;
using System.Text;

namespace SqlExpert.Core.Procedures;

/// <summary>
/// Builds procedure scripts from a <see cref="ProcedureDefinition"/>: the header generated from the parameter grid
/// (CREATE … PROCEDURE name, parameters, WITH options, AS), the full script, DROP, the New Procedure template,
/// the EXEC batch of Execute and the description (MS_Description) statements.
/// </summary>
public static class ProcedureScriptBuilder
{
    public const string NewLine = "\r\n";
    public const string NewProcedureName = "NEW_PROCEDURE";
    private const string Indent = "    ";

    /// <summary>[schema].[name] with closing brackets doubled.</summary>
    public static string QuoteName(string name) => "[" + (name ?? "").Replace("]", "]]") + "]";

    public static string QualifiedName(string schema, string name) =>
        QuoteName(string.IsNullOrWhiteSpace(schema) ? "dbo" : schema) + "." + QuoteName(name);

    /// <summary>N'...' literal with quotes doubled.</summary>
    public static string Literal(string? value) => value is null ? "NULL" : "N'" + value.Replace("'", "''") + "'";

    public static string VerbText(ProcedureVerb verb) => verb switch
    {
        ProcedureVerb.Alter => "ALTER",
        ProcedureVerb.CreateOrAlter => "CREATE OR ALTER",
        _ => "CREATE"
    };

    /// <summary>"varchar(50)", "decimal(18,2)", "nvarchar(max)", "int".</summary>
    public static string FormatType(string? typeName, string? size, string? scale)
    {
        var t = (typeName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(size)) return t;
        return string.IsNullOrWhiteSpace(scale) ? $"{t}({size.Trim()})" : $"{t}({size.Trim()},{scale.Trim()})";
    }

    /// <summary>One parameter line: "@id int = 0 OUTPUT".</summary>
    public static string FormatParameter(ProcedureParameter p)
    {
        var sb = new StringBuilder();
        sb.Append(p.Name).Append(' ').Append(p.FullType);
        if (!string.IsNullOrWhiteSpace(p.Default)) sb.Append(" = ").Append(p.Default.Trim());
        if (p.IsOutput) sb.Append(" OUTPUT");
        else if (p.IsReadOnly) sb.Append(" READONLY");
        return sb.ToString();
    }

    /// <summary>
    /// Header generated from the grid, ending with the "AS" line:
    /// <code>
    /// CREATE OR ALTER PROCEDURE [dbo].[X]
    ///     @a int = 0,
    ///     @b varchar(10) OUTPUT
    /// WITH RECOMPILE
    /// AS
    /// </code>
    /// </summary>
    public static string BuildHeader(ProcedureDefinition def, ProcedureVerb verb)
    {
        ArgumentNullException.ThrowIfNull(def);
        var sb = new StringBuilder();
        sb.Append(VerbText(verb)).Append(" PROCEDURE ").Append(QualifiedName(def.Schema, def.Name));
        var ps = def.Parameters.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
        for (var i = 0; i < ps.Count; i++)
        {
            sb.Append(NewLine).Append(Indent).Append(FormatParameter(ps[i]));
            if (i < ps.Count - 1) sb.Append(',');
        }
        if (!string.IsNullOrWhiteSpace(def.Options))
            sb.Append(NewLine).Append("WITH ").Append(def.Options.Trim());
        if (def.ForReplication)
            sb.Append(NewLine).Append("FOR REPLICATION");
        sb.Append(NewLine).Append("AS");
        return sb.ToString();
    }

    /// <summary>Preamble + header + body: the text that is compiled.</summary>
    public static string BuildScript(ProcedureDefinition def, ProcedureVerb verb) =>
        (def.Preamble ?? "") + BuildHeader(def, verb) + NewLine + (def.Body ?? "");

    /// <summary>Editor line (1-based, in <see cref="BuildScript"/>) where the body starts.</summary>
    public static int BodyStartLine(ProcedureDefinition def, ProcedureVerb verb) =>
        CountLines(def.Preamble ?? "") - 1 + CountLines(BuildHeader(def, verb)) + 1;

    /// <summary>Number of lines of a text ("" = 1, "a\nb" = 2).</summary>
    public static int CountLines(string text)
    {
        var n = 1;
        foreach (var c in text) if (c == '\n') n++;
        return n;
    }

    public static string BuildDrop(string schema, string name) => $"DROP PROCEDURE {QualifiedName(schema, name)};";

    /// <summary>New Procedure template: NEW_PROCEDURE, no parameters, BEGIN SET NOCOUNT ON; … END.</summary>
    public static ProcedureDefinition NewTemplate(string? schema) => new()
    {
        Schema = string.IsNullOrWhiteSpace(schema) ? "dbo" : schema,
        Name = NewProcedureName,
        Body = "BEGIN" + NewLine +
               Indent + "SET NOCOUNT ON;" + NewLine +
               NewLine +
               Indent + "/* corpo da procedure */" + NewLine +
               NewLine +
               "END" + NewLine
    };

    /// <summary>
    /// CREATE OR ALTER exists from SQL Server 2016 SP1 (13.0.4001). <paramref name="productVersion"/> is
    /// SERVERPROPERTY('ProductVersion'), e.g. "16.0.1000.6". Unknown versions are treated as modern.
    /// </summary>
    public static bool SupportsCreateOrAlter(string? productVersion)
    {
        if (string.IsNullOrWhiteSpace(productVersion)) return true;
        var parts = productVersion.Split('.');
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major)) return true;
        if (major != 13) return major > 13;
        return parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var build) && build >= 4001;
    }

    /// <summary>Verb used by Compile: CREATE OR ALTER when the server has it, otherwise ALTER / CREATE by existence.</summary>
    public static ProcedureVerb CompileVerb(string? productVersion, bool exists) =>
        SupportsCreateOrAlter(productVersion) ? ProcedureVerb.CreateOrAlter : exists ? ProcedureVerb.Alter : ProcedureVerb.Create;

    // ===== Description (MS_Description) =====

    /// <summary>
    /// Adds, updates or drops MS_Description of the procedure (<paramref name="parameter"/> = null) or of one parameter.
    /// Empty text drops the property.
    /// </summary>
    public static string BuildDescriptionStatement(string schema, string name, string? parameter, string? description)
    {
        var level = $"@level0type = N'SCHEMA', @level0name = {Literal(schema)}, @level1type = N'PROCEDURE', @level1name = {Literal(name)}";
        if (parameter is not null) level += $", @level2type = N'PARAMETER', @level2name = {Literal(parameter)}";
        var exists = parameter is null
            ? $"EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID({Literal(QualifiedName(schema, name))}) AND minor_id = 0 AND name = N'MS_Description')"
            : $"EXISTS (SELECT 1 FROM sys.extended_properties ep JOIN sys.parameters p ON p.object_id = ep.major_id AND p.parameter_id = ep.minor_id " +
              $"WHERE ep.class = 2 AND ep.major_id = OBJECT_ID({Literal(QualifiedName(schema, name))}) AND p.name = {Literal(parameter)} AND ep.name = N'MS_Description')";
        if (string.IsNullOrWhiteSpace(description))
            return $"IF {exists}{NewLine}    EXEC sys.sp_dropextendedproperty @name = N'MS_Description', {level};";
        var value = Literal(description);
        return $"IF {exists}{NewLine}    EXEC sys.sp_updateextendedproperty @name = N'MS_Description', @value = {value}, {level};{NewLine}" +
               $"ELSE{NewLine}    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = {value}, {level};";
    }

    /// <summary>Statements that make the parameter descriptions on the server equal to the grid (one per parameter).</summary>
    public static string BuildParameterDescriptions(ProcedureDefinition def, IReadOnlyDictionary<string, string>? current)
    {
        var sb = new StringBuilder();
        foreach (var p in def.Parameters.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
        {
            string? old = null;
            current?.TryGetValue(p.Name, out old);
            if (string.Equals(old ?? "", p.Description ?? "", StringComparison.Ordinal)) continue;
            sb.Append(BuildDescriptionStatement(def.Schema, def.Name, p.Name, p.Description)).Append(NewLine);
        }
        return sb.ToString();
    }

    // ===== Execute =====

    /// <summary>
    /// EXEC batch of Execute: declares the OUTPUT parameters (and empty table variables for READONLY TVPs),
    /// runs the procedure capturing RETURN and ends with one result set holding RETURN_VALUE and the OUTPUT values.
    /// </summary>
    public static string BuildExecuteBatch(string schema, string name, IReadOnlyList<ProcedureArgument> args)
    {
        var sb = new StringBuilder();
        sb.Append("DECLARE @RETURN_VALUE int;").Append(NewLine);
        var vars = new Dictionary<ProcedureArgument, string>();
        var i = 0;
        foreach (var a in args)
        {
            i++;
            if (!a.Parameter.IsOutput && !a.Parameter.IsReadOnly) continue;
            var v = "@p" + i.ToString(CultureInfo.InvariantCulture) + "_" + SafeVarSuffix(a.Parameter.Name);
            vars[a] = v;
            sb.Append("DECLARE ").Append(v).Append(' ').Append(a.Parameter.FullType);
            if (a.Parameter.IsOutput && a.Mode == ArgumentMode.Value)
                sb.Append(" = ").Append(FormatValue(a.Parameter, a.Value));
            sb.Append(';').Append(NewLine);
        }

        sb.Append("EXEC @RETURN_VALUE = ").Append(QualifiedName(schema, name));
        var passed = args.Where(a => a.Mode != ArgumentMode.Default || a.Parameter.IsOutput).ToList();
        for (var k = 0; k < passed.Count; k++)
        {
            var a = passed[k];
            sb.Append(k == 0 ? NewLine + Indent : "," + NewLine + Indent);
            sb.Append(a.Parameter.Name).Append(" = ");
            if (vars.TryGetValue(a, out var v))
                sb.Append(v).Append(a.Parameter.IsOutput ? " OUTPUT" : "");
            else
                sb.Append(a.Mode == ArgumentMode.Null ? "NULL" : FormatValue(a.Parameter, a.Value));
        }
        sb.Append(';').Append(NewLine);

        sb.Append("SELECT @RETURN_VALUE AS [RETURN_VALUE]");
        foreach (var a in args.Where(a => a.Parameter.IsOutput))
            sb.Append(", ").Append(vars[a]).Append(" AS ").Append(QuoteName(a.Parameter.Name));
        sb.Append(';');
        return sb.ToString();
    }

    private static string SafeVarSuffix(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.TrimStart('@'))
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        return sb.Length == 0 ? "x" : sb.ToString();
    }

    private static readonly HashSet<string> NumericTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bit", "tinyint", "smallint", "int", "bigint", "decimal", "numeric", "money", "smallmoney", "float", "real"
    };

    private static readonly HashSet<string> BinaryTypes = new(StringComparer.OrdinalIgnoreCase) { "binary", "varbinary", "image", "timestamp", "rowversion" };

    /// <summary>
    /// Value typed in the Execute dialog as a T-SQL literal: numbers raw for numeric types, 0x… raw for binary types,
    /// everything else as N'…' (converted by the server to the parameter type).
    /// </summary>
    public static string FormatValue(ProcedureParameter p, string? value)
    {
        if (value is null) return "NULL";
        var t = p.TypeName.Trim().Trim('[', ']');
        var v = value.Trim();
        if (NumericTypes.Contains(t) && decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return v;
        if (NumericTypes.Contains(t) && decimal.TryParse(v, NumberStyles.Float, CultureInfo.GetCultureInfo("pt-BR"), out var br))
            return br.ToString(CultureInfo.InvariantCulture);
        if (BinaryTypes.Contains(t) && v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && v.Skip(2).All(Uri.IsHexDigit)) return v;
        return Literal(value);
    }
}

public enum ArgumentMode
{
    /// <summary>Pass <see cref="ProcedureArgument.Value"/>.</summary>
    Value,
    /// <summary>Pass NULL.</summary>
    Null,
    /// <summary>Omit the parameter (its default is used); OUTPUT parameters are still passed to read them back.</summary>
    Default
}

/// <summary>A value chosen in the Execute dialog.</summary>
public sealed record ProcedureArgument(ProcedureParameter Parameter, ArgumentMode Mode, string? Value = null);
