using System.Text.Json;

namespace SqlExpert.Core.Updates;

/// <summary>One file attached to a GitHub release.</summary>
public sealed record ReleaseAsset(string Name, long Size, string DownloadUrl);

/// <summary>The fields of a GitHub release (REST API "releases" list) that the update check uses.</summary>
public sealed record ReleaseInfo(
    string TagName,
    string Name,
    string Body,
    bool IsDraft,
    bool IsPreRelease,
    string HtmlUrl,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>Parsed tag (null when the tag is not vX.Y.Z[-suffix]).</summary>
    public SemVersion? Version => SemVersion.TryParse(TagName, out var v) ? v : null;

    /// <summary>Parses the JSON array returned by GET /repos/{owner}/{repo}/releases. Malformed items are skipped.</summary>
    public static IReadOnlyList<ReleaseInfo> ParseList(string json)
    {
        var list = new List<ReleaseInfo>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object) continue;
            var tag = Str(r, "tag_name");
            if (string.IsNullOrWhiteSpace(tag)) continue;
            var assets = new List<ReleaseAsset>();
            if (r.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in a.EnumerateArray())
                {
                    var name = Str(x, "name");
                    var url = Str(x, "browser_download_url");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;
                    var size = x.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var n) ? n : -1;
                    assets.Add(new ReleaseAsset(name, size, url));
                }
            }
            DateTimeOffset? published = null;
            if (r.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String && p.TryGetDateTimeOffset(out var d))
                published = d;
            list.Add(new ReleaseInfo(
                tag.Trim(),
                Str(r, "name") ?? "",
                Str(r, "body") ?? "",
                Bool(r, "draft"),
                Bool(r, "prerelease"),
                Str(r, "html_url") ?? "",
                published,
                assets));
        }
        return list;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>A release newer than the running version, with the package to download.</summary>
public sealed record UpdateCandidate(ReleaseInfo Release, SemVersion Version, ReleaseAsset Package, ReleaseAsset? HashAsset, string? BodyHash);
