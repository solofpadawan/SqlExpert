namespace SqlExpert.Core.Services;

/// <summary>"Server Version" box: SERVERPROPERTY values → "SQL Server 2022 (16.0.1000.6) Express".</summary>
public static class ServerVersionText
{
    public const string Query = """
        SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)),
               CAST(SERVERPROPERTY('Edition') AS nvarchar(128)),
               CAST(SERVERPROPERTY('EngineEdition') AS int);
        """;

    public static string ReleaseName(string? productVersion)
    {
        var parts = (productVersion ?? "").Split('.');
        if (parts.Length == 0 || !int.TryParse(parts[0], out var major)) return "";
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var m) ? m : 0;
        return major switch
        {
            8 => "2000",
            9 => "2005",
            10 => minor >= 50 ? "2008 R2" : "2008",
            11 => "2012",
            12 => "2014",
            13 => "2016",
            14 => "2017",
            15 => "2019",
            16 => "2022",
            17 => "2025",
            _ => ""
        };
    }

    /// <summary>"Express Edition (64-bit)" → "Express"; "Enterprise Edition: Core-based Licensing (64-bit)" → "Enterprise".</summary>
    public static string ShortEdition(string? edition)
    {
        var e = (edition ?? "").Trim();
        var paren = e.IndexOf('(');
        if (paren >= 0) e = e[..paren];
        var colon = e.IndexOf(':');
        if (colon >= 0) e = e[..colon];
        e = e.Trim();
        if (e.EndsWith(" Edition", StringComparison.OrdinalIgnoreCase)) e = e[..^" Edition".Length];
        return e.Trim();
    }

    public static string Format(string? productVersion, string? edition, int? engineEdition = null)
    {
        var version = (productVersion ?? "").Trim();
        if (engineEdition == 5) return $"Azure SQL Database ({version})".Replace(" ()", "");
        if (engineEdition == 8) return $"Azure SQL Managed Instance ({version})".Replace(" ()", "");
        var release = ReleaseName(version);
        var name = release.Length > 0 ? $"SQL Server {release}" : "SQL Server";
        var text = version.Length > 0 ? $"{name} ({version})" : name;
        var ed = ShortEdition(edition);
        return ed.Length > 0 ? $"{text} {ed}" : text;
    }
}
