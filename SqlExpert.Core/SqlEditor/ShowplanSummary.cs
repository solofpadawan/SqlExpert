using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SqlExpert.Core.SqlEditor;

/// <summary>Totals of the actual execution plans (SET STATISTICS XML) of one execution.</summary>
public sealed class PlanStatistics
{
    public int Statements { get; set; }
    /// <summary>Sum of QueryPlan/@CompileTime (parse + compile), ms.</summary>
    public long PrepareMs { get; set; }
    public long CompileCpuMs { get; set; }
    /// <summary>Sum of QueryTimeStats/@ElapsedTime, ms (null when the server does not report it).</summary>
    public long? ExecuteMs { get; set; }
    public long? CpuMs { get; set; }
    public long LogicalReads { get; set; }
    public long PhysicalReads { get; set; }
    public long ReadAheads { get; set; }
    public long Scans { get; set; }
    /// <summary>One indented line per operator ("Clustered Index Scan [dbo].[Clientes].[PK_Clientes]").</summary>
    public List<string> PlanLines { get; } = new();
}

/// <summary>Reads SHOWPLAN XML documents into a short plan text and the "Performance info" figures.</summary>
public static class ShowplanSummary
{
    /// <summary>Column name of the result set SQL Server sends for SET STATISTICS XML / SHOWPLAN_XML.</summary>
    public const string ShowplanColumnName = "Microsoft SQL Server 2005 XML Showplan";

    public static bool IsShowplanResult(IReadOnlyList<string> columnNames) =>
        columnNames.Count == 1 && string.Equals(columnNames[0], ShowplanColumnName, StringComparison.OrdinalIgnoreCase);

    private const int MaxLinesPerStatement = 40;
    private const int MaxStatementsShown = 10;

    public static PlanStatistics Summarize(IEnumerable<string> showplanXml)
    {
        var stats = new PlanStatistics();
        var shown = 0;
        var hidden = 0;
        foreach (var xml in showplanXml)
        {
            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch (XmlException) { continue; }
            foreach (var stmt in doc.Descendants().Where(e => e.Name.LocalName == "StmtSimple"))
            {
                var plan = stmt.Elements().FirstOrDefault(e => e.Name.LocalName == "QueryPlan");
                if (plan is null) continue;
                stats.Statements++;
                stats.PrepareMs += Long(plan, "CompileTime");
                stats.CompileCpuMs += Long(plan, "CompileCPU");
                var time = plan.Elements().FirstOrDefault(e => e.Name.LocalName == "QueryTimeStats");
                if (time is not null)
                {
                    stats.ExecuteMs = (stats.ExecuteMs ?? 0) + Long(time, "ElapsedTime");
                    stats.CpuMs = (stats.CpuMs ?? 0) + Long(time, "CpuTime");
                }
                foreach (var c in plan.Descendants().Where(e => e.Name.LocalName == "RunTimeCountersPerThread"))
                {
                    stats.LogicalReads += Long(c, "ActualLogicalReads");
                    stats.PhysicalReads += Long(c, "ActualPhysicalReads");
                    stats.ReadAheads += Long(c, "ActualReadAheads");
                    stats.Scans += Long(c, "ActualScans");
                }

                if (shown >= MaxStatementsShown) { hidden++; continue; }
                shown++;
                var lines = new List<string>();
                var root = plan.Elements().FirstOrDefault(e => e.Name.LocalName == "RelOp");
                if (root is not null) Walk(root, 0, lines);
                var type = (string?)stmt.Attribute("StatementType");
                if (stats.PlanLines.Count > 0) stats.PlanLines.Add("");
                var text = OneLine((string?)stmt.Attribute("StatementText"));
                stats.PlanLines.Add(text.Length > 0 ? text : type ?? "");
                if (lines.Count > MaxLinesPerStatement)
                {
                    var extra = lines.Count - MaxLinesPerStatement;
                    lines.RemoveRange(MaxLinesPerStatement, extra);
                    lines.Add($"  ... (+{extra} operadores)");
                }
                stats.PlanLines.AddRange(lines);
            }
        }
        if (hidden > 0) stats.PlanLines.Add($"... (+{hidden} comando(s))");
        return stats;
    }

    private static string OneLine(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = string.Join(" ", s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        return t.Length > 160 ? t[..157] + "..." : t;
    }

    private static void Walk(XElement relOp, int depth, List<string> lines)
    {
        var physical = (string?)relOp.Attribute("PhysicalOp") ?? "?";
        var logical = (string?)relOp.Attribute("LogicalOp");
        var sb = new StringBuilder();
        sb.Append(' ', 2 + depth * 2).Append(physical);
        if (logical is not null && !string.Equals(logical, physical, StringComparison.OrdinalIgnoreCase)
            && physical is "Nested Loops" or "Hash Match" or "Merge Join" or "Stream Aggregate" or "Concatenation")
            sb.Append(" (").Append(logical).Append(')');

        // Objects of this operator: Object elements whose nearest RelOp ancestor is this one.
        var objects = relOp.Descendants().Where(e => e.Name.LocalName == "Object" && NearestRelOp(e) == relOp)
            .Select(ObjectName).Where(n => n.Length > 0).Distinct().ToList();
        if (objects.Count > 0) sb.Append("  ").Append(string.Join(", ", objects));
        lines.Add(sb.ToString());

        foreach (var child in relOp.Descendants().Where(e => e.Name.LocalName == "RelOp" && NearestRelOp(e) == relOp).ToList())
            Walk(child, depth + 1, lines);
    }

    private static XElement? NearestRelOp(XElement e)
    {
        for (var p = e.Parent; p is not null; p = p.Parent)
            if (p.Name.LocalName == "RelOp") return p;
        return null;
    }

    private static string ObjectName(XElement o)
    {
        var parts = new[] { (string?)o.Attribute("Schema"), (string?)o.Attribute("Table") }.Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (parts.Count == 0) return "";
        var name = string.Join(".", parts);
        var index = (string?)o.Attribute("Index");
        if (!string.IsNullOrEmpty(index)) name += "." + index;
        var alias = (string?)o.Attribute("Alias");
        if (!string.IsNullOrEmpty(alias)) name += " " + alias;
        return name;
    }

    private static long Long(XElement e, string attribute) =>
        long.TryParse((string?)e.Attribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    /// <summary>
    /// Messages-panel text: "Plan" + operators, then "------ Performance info ------" with the figures
    /// (pt-BR numbers: "Execute time = 1.234ms").
    /// </summary>
    public static (string Plan, string Performance) Format(PlanStatistics? stats, long rowsFetched, TimeSpan clientElapsed, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.GetCultureInfo("pt-BR");
        string N(long v) => v.ToString("N0", culture);
        var plan = new StringBuilder("Plan");
        if (stats is null || stats.PlanLines.Count == 0)
            plan.Append(Environment.NewLine).Append("(sem plano: nenhum comando com plano de execução)");
        else
            foreach (var l in stats.PlanLines) plan.Append(Environment.NewLine).Append(l);

        var perf = new StringBuilder("------ Performance info ------");
        void Line(string label, string value) => perf.Append(Environment.NewLine).Append(label).Append(" = ").Append(value);
        if (stats is not null && stats.Statements > 0)
        {
            Line("Prepare time", N(stats.PrepareMs) + "ms");
            Line("Execute time", stats.ExecuteMs is { } ex ? N(ex) + "ms" : "n/d");
            Line("CPU time", stats.CpuMs is { } cpu ? N(cpu) + "ms" : "n/d");
        }
        Line("Client time", N((long)Math.Round(clientElapsed.TotalMilliseconds)) + "ms");
        Line("Rows fetched", N(rowsFetched));
        if (stats is not null && stats.Statements > 0)
        {
            Line("Logical reads (cache)", N(stats.LogicalReads));
            Line("Physical reads (disk)", N(stats.PhysicalReads));
            Line("Read-ahead reads", N(stats.ReadAheads));
            Line("Scans", N(stats.Scans));
            Line("Statements", N(stats.Statements));
        }
        return (plan.ToString(), perf.ToString());
    }
}
