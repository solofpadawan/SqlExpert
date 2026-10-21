using System.Text;
using System.Text.RegularExpressions;

namespace SqlExpert.Core.Updates;

/// <summary>
/// Chooses the release to offer: the highest non-draft release whose tag is a valid version greater than the
/// running one and that carries a package "SqlExpert-X.Y.Z-win-x64.zip". Pre-releases count only when the user opted in.
/// </summary>
public static class ReleaseSelector
{
    public const string RepositoryOwner = "solofpadawan";
    public const string RepositoryName = "SqlExpert";
    public static string ReleasesApiUrl => $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases";
    public static string ReleasesPageUrl => $"https://github.com/{RepositoryOwner}/{RepositoryName}/releases";

    private static readonly Regex PackageName = new(@"^SqlExpert-(?<v>[0-9A-Za-z.\-]+)-win-x64\.zip$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HexHash = new(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{64}(?![0-9A-Fa-f])", RegexOptions.CultureInvariant);
    private static readonly Regex BodyHashLine = new(@"SHA-?256\s*[:=]\s*`?\s*(?<h>[0-9A-Fa-f]{64})(?![0-9A-Fa-f])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Default of "Receber versões de teste (pre-release)" when the user never changed it: on during the 1.x series
    /// (all 1.x releases so far are published as pre-release) and whenever the running build is itself a pre-release.
    /// </summary>
    public static bool DefaultIncludePreReleases(SemVersion current) => current.IsPreRelease || current.Major < 2;

    public static UpdateCandidate? SelectUpdate(
        IEnumerable<ReleaseInfo> releases, SemVersion current, bool includePreReleases, string? skippedVersion = null)
    {
        SemVersion? skipped = null;
        if (!string.IsNullOrWhiteSpace(skippedVersion)) SemVersion.TryParse(skippedVersion, out skipped);
        UpdateCandidate? best = null;
        foreach (var r in releases)
        {
            if (r.IsDraft) continue;
            var v = r.Version;
            if (v is null || v <= current) continue;
            // A tag like v1.2.0-rc.1 is a test build even if the release was not ticked as pre-release.
            var pre = r.IsPreRelease || v.IsPreRelease;
            if (pre && !includePreReleases) continue;
            if (skipped is not null && v.Equals(skipped)) continue;
            var package = SelectPackage(r, v);
            if (package is null) continue;
            if (best is not null && v <= best.Version) continue;
            best = new UpdateCandidate(r, v, package, SelectHashAsset(r, package), ParseBodyHash(r.Body));
        }
        return best;
    }

    /// <summary>The "SqlExpert-*-win-x64.zip" asset; the one whose name carries the tag version wins when there are several.</summary>
    public static ReleaseAsset? SelectPackage(ReleaseInfo release, SemVersion? version = null)
    {
        version ??= release.Version;
        ReleaseAsset? any = null;
        foreach (var a in release.Assets)
        {
            var m = PackageName.Match(a.Name);
            if (!m.Success) continue;
            if (version is not null && SemVersion.TryParse(m.Groups["v"].Value, out var av) && av!.Equals(version))
                return a;
            any ??= a;
        }
        return any;
    }

    /// <summary>"&lt;package&gt;.sha256" (or "&lt;package&gt;.sha256.txt", "SHA256SUMS[.txt]") next to the package.</summary>
    public static ReleaseAsset? SelectHashAsset(ReleaseInfo release, ReleaseAsset package)
    {
        string[] names = { package.Name + ".sha256", package.Name + ".sha256.txt", "SHA256SUMS", "SHA256SUMS.txt", "sha256sums.txt" };
        foreach (var n in names)
        {
            var a = release.Assets.FirstOrDefault(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
            if (a is not null) return a;
        }
        return null;
    }

    /// <summary>"SHA256: &lt;64 hex&gt;" line of the release notes (lower case), or null.</summary>
    public static string? ParseBodyHash(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        var m = BodyHashLine.Match(body);
        return m.Success ? m.Groups["h"].Value.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Hash for <paramref name="packageName"/> in a .sha256 file: "hex  name" lines (sha256sum) are matched by name,
    /// otherwise the first 64-hex token (Get-FileHash output or a bare hash) is used. Lower case; null when none.
    /// </summary>
    public static string? ParseHashFile(string? content, string packageName)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        string? first = null;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            var m = HexHash.Match(line);
            if (!m.Success) continue;
            var hex = m.Value.ToLowerInvariant();
            first ??= hex;
            var rest = line[(m.Index + m.Length)..].Trim().TrimStart('*').Trim();
            if (rest.Length > 0 && string.Equals(Path.GetFileName(rest), packageName, StringComparison.OrdinalIgnoreCase))
                return hex;
        }
        return first;
    }

    /// <summary>Release notes (GitHub markdown) as readable plain text: headings, bold, links and bullets simplified.</summary>
    public static string NotesToPlainText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "(sem notas de versão)";
        var sb = new StringBuilder();
        foreach (var raw in markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;
            if (trimmed.StartsWith("```", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith('#'))
            {
                trimmed = trimmed.TrimStart('#').Trim();
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine(Inline(trimmed));
                continue;
            }
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal) || trimmed.StartsWith("+ ", StringComparison.Ordinal))
                trimmed = "• " + trimmed[2..];
            sb.Append(new string(' ', Math.Min(indent, 8))).AppendLine(Inline(trimmed));
        }
        return sb.ToString().Trim();
    }

    private static string Inline(string s)
    {
        s = Regex.Replace(s, @"!\[([^\]]*)\]\([^)]*\)", "$1");
        s = Regex.Replace(s, @"\[([^\]]+)\]\(([^)]+)\)", "$1 ($2)");
        s = Regex.Replace(s, @"(\*\*|__)(.+?)\1", "$2");
        s = Regex.Replace(s, @"`([^`]*)`", "$1");
        return s;
    }
}
