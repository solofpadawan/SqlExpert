using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;
using SqlExpert.Core.Updates;

namespace SqlExpert.Tests;

public class SemVersionTests
{
    [Theory]
    [InlineData("v1.0.1", 1, 0, 1, null)]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("V2.0", 2, 0, 0, null)]
    [InlineData("v1.1.0-beta.2", 1, 1, 0, "beta.2")]
    [InlineData("1.0.0+abc", 1, 0, 0, null)]
    [InlineData(" v1.0.0-rc.1+build.5 ", 1, 0, 0, "rc.1")]
    public void Parse_Valid(string text, int major, int minor, int patch, string? pre)
    {
        Assert.True(SemVersion.TryParse(text, out var v));
        Assert.Equal(major, v!.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(patch, v.Patch);
        Assert.Equal(pre, v.PreRelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("v")]
    [InlineData("1")]
    [InlineData("1.0.0.0")]
    [InlineData("latest")]
    [InlineData("v1.x.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-beta..1")]
    public void Parse_Invalid(string? text) => Assert.False(SemVersion.TryParse(text, out _));

    [Theory]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("v1.0.0", "1.0.0", 0)]
    [InlineData("1.10.0", "1.9.9", 1)]
    [InlineData("2.0.0", "1.99.99", 1)]
    [InlineData("1.0.0-beta", "1.0.0", -1)]
    [InlineData("1.0.1-beta", "1.0.0", 1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11", -1)]
    [InlineData("1.0.0-rc.1", "1.0.0-beta.11", 1)]
    [InlineData("1.0.0+build1", "1.0.0+build2", 0)]
    public void Compare_FollowsSemverPrecedence(string a, string b, int expected)
    {
        var c = SemVersion.Parse(a).CompareTo(SemVersion.Parse(b));
        Assert.Equal(expected, Math.Sign(c));
    }

    [Fact]
    public void ToString_HasNoPrefix() => Assert.Equal("1.1.0-beta.2", SemVersion.Parse("v1.1.0-beta.2").ToString());

    [Fact]
    public void DefaultPreReleaseOption_OnDuring1x_OffFrom2()
    {
        Assert.True(ReleaseSelector.DefaultIncludePreReleases(SemVersion.Parse("1.0.1")));
        Assert.True(ReleaseSelector.DefaultIncludePreReleases(SemVersion.Parse("2.0.0-beta")));
        Assert.False(ReleaseSelector.DefaultIncludePreReleases(SemVersion.Parse("2.0.0")));
    }
}

public class ReleaseSelectionTests
{
    internal static ReleaseInfo Release(string tag, bool pre = false, bool draft = false, string body = "", params string[] assets) =>
        new(tag, tag, body, draft, pre, $"https://example.invalid/releases/{tag}", null,
            (assets.Length == 0 ? new[] { $"SqlExpert-{tag.TrimStart('v')}-win-x64.zip" } : assets)
                .Select(a => new ReleaseAsset(a, 1000, "https://example.invalid/dl/" + a)).ToList());

    private static readonly SemVersion Current = SemVersion.Parse("1.0.1");

    [Fact]
    public void PicksHighestNewerRelease()
    {
        var list = new[] { Release("v1.0.0"), Release("v1.0.3"), Release("v1.0.2"), Release("v1.0.1") };
        var c = ReleaseSelector.SelectUpdate(list, Current, includePreReleases: false);
        Assert.NotNull(c);
        Assert.Equal("1.0.3", c!.Version.ToString());
        Assert.Equal("SqlExpert-1.0.3-win-x64.zip", c.Package.Name);
    }

    [Fact]
    public void NothingNewer_ReturnsNull()
    {
        var list = new[] { Release("v1.0.0"), Release("v1.0.1") };
        Assert.Null(ReleaseSelector.SelectUpdate(list, Current, includePreReleases: true));
    }

    [Fact]
    public void PreReleases_OnlyWhenOptedIn()
    {
        var list = new[] { Release("v1.0.2"), Release("v1.0.3", pre: true), Release("v1.1.0-beta.1") };
        Assert.Equal("1.0.2", ReleaseSelector.SelectUpdate(list, Current, false)!.Version.ToString());
        Assert.Equal("1.1.0-beta.1", ReleaseSelector.SelectUpdate(list, Current, true)!.Version.ToString());
    }

    [Fact]
    public void Drafts_AreIgnored()
    {
        var list = new[] { Release("v1.0.2"), Release("v1.0.9", draft: true) };
        Assert.Equal("1.0.2", ReleaseSelector.SelectUpdate(list, Current, true)!.Version.ToString());
    }

    [Fact]
    public void SkippedVersion_IsNotOffered_ButANewerOneIs()
    {
        var list = new[] { Release("v1.0.2") };
        Assert.Null(ReleaseSelector.SelectUpdate(list, Current, false, skippedVersion: "1.0.2"));
        Assert.Null(ReleaseSelector.SelectUpdate(list, Current, false, skippedVersion: "v1.0.2"));
        var more = new[] { Release("v1.0.2"), Release("v1.0.3") };
        Assert.Equal("1.0.3", ReleaseSelector.SelectUpdate(more, Current, false, skippedVersion: "1.0.2")!.Version.ToString());
        Assert.Equal("1.0.2", ReleaseSelector.SelectUpdate(list, Current, false, skippedVersion: null)!.Version.ToString());
    }

    [Fact]
    public void InvalidTags_AndReleasesWithoutPackage_AreIgnored()
    {
        var list = new[]
        {
            Release("latest"),
            Release("v1.0.5", assets: new[] { "source.zip", "SqlExpert-1.0.5-win-arm64.zip" }),
            Release("v1.0.4")
        };
        Assert.Equal("1.0.4", ReleaseSelector.SelectUpdate(list, Current, true)!.Version.ToString());
    }

    [Fact]
    public void Package_PrefersNameWithTheTagVersion()
    {
        var r = Release("v1.0.2", assets: new[] { "SqlExpert-1.0.1-win-x64.zip", "SqlExpert-1.0.2-win-x64.zip", "SqlExpert-1.0.2-win-x64.zip.sha256" });
        Assert.Equal("SqlExpert-1.0.2-win-x64.zip", ReleaseSelector.SelectPackage(r)!.Name);
        var only = Release("v1.0.2", assets: new[] { "sqlexpert-1.0.2-WIN-X64.ZIP" });
        Assert.NotNull(ReleaseSelector.SelectPackage(only));
        var c = ReleaseSelector.SelectUpdate(new[] { r }, Current, false)!;
        Assert.Equal("SqlExpert-1.0.2-win-x64.zip.sha256", c.HashAsset!.Name);
    }

    [Fact]
    public void HashAsset_SumsFileIsAccepted()
    {
        var r = Release("v1.0.2", assets: new[] { "SqlExpert-1.0.2-win-x64.zip", "SHA256SUMS.txt" });
        Assert.Equal("SHA256SUMS.txt", ReleaseSelector.SelectHashAsset(r, r.Assets[0])!.Name);
        Assert.Null(ReleaseSelector.SelectHashAsset(Release("v1.0.2"), Release("v1.0.2").Assets[0]));
    }

    private const string Hex = "3A7BD3E2360A3D29EEA436FCFB7E44C735D117C42D1C1835420B6B9942DD4F1B";

    [Theory]
    [InlineData("Notas\n\nSHA256: " + Hex)]
    [InlineData("SHA-256 = `" + Hex + "`")]
    [InlineData("**sha256:** x\nsha256: " + Hex + "\n")]
    public void BodyHash_IsParsed(string body) => Assert.Equal(Hex.ToLowerInvariant(), ReleaseSelector.ParseBodyHash(body));

    [Theory]
    [InlineData("sem hash")]
    [InlineData("SHA256: 1234")]
    [InlineData("")]
    public void BodyHash_Missing(string body) => Assert.Null(ReleaseSelector.ParseBodyHash(body));

    [Fact]
    public void HashFile_Formats()
    {
        var lower = Hex.ToLowerInvariant();
        Assert.Equal(lower, ReleaseSelector.ParseHashFile(Hex, "SqlExpert-1.0.2-win-x64.zip"));
        Assert.Equal(lower, ReleaseSelector.ParseHashFile($"{lower}  SqlExpert-1.0.2-win-x64.zip\n", "SqlExpert-1.0.2-win-x64.zip"));
        var other = new string('a', 64);
        Assert.Equal(lower, ReleaseSelector.ParseHashFile($"{other} *other.zip\n{lower} *SqlExpert-1.0.2-win-x64.zip\n", "SqlExpert-1.0.2-win-x64.zip"));
        Assert.Equal(lower, ReleaseSelector.ParseHashFile($"Algorithm Hash Path\n--------- ---- ----\nSHA256    {Hex} C:\\x\\SqlExpert-1.0.2-win-x64.zip", "SqlExpert-1.0.2-win-x64.zip"));
        Assert.Null(ReleaseSelector.ParseHashFile("nada aqui", "x.zip"));
    }

    [Fact]
    public void Notes_MarkdownLite()
    {
        var text = ReleaseSelector.NotesToPlainText("## Novidades\n- **Atualização** automática\n* veja [o README](https://x/readme)\n```\ncode\n```\nUse `F9`.");
        Assert.Contains("Novidades", text);
        Assert.Contains("• Atualização automática", text);
        Assert.Contains("• veja o README (https://x/readme)", text);
        Assert.Contains("Use F9.", text);
        Assert.DoesNotContain("##", text);
        Assert.DoesNotContain("**", text);
        Assert.Equal("(sem notas de versão)", ReleaseSelector.NotesToPlainText("  "));
    }

    [Fact]
    public void ParseList_ReadsGitHubJson()
    {
        const string json = """
            [
              { "tag_name": "v1.0.2", "name": "SqlExpert 1.0.2", "body": "notas\r\nSHA256: 3a7bd3e2360a3d29eea436fcfb7e44c735d117c42d1c1835420b6b9942dd4f1b",
                "draft": false, "prerelease": true, "html_url": "https://github.com/o/r/releases/tag/v1.0.2",
                "published_at": "2026-10-22T12:00:00Z",
                "assets": [ { "name": "SqlExpert-1.0.2-win-x64.zip", "size": 71420497, "browser_download_url": "https://github.com/o/r/releases/download/v1.0.2/SqlExpert-1.0.2-win-x64.zip" },
                            { "name": "broken" } ] },
              { "name": "no tag" },
              { "tag_name": "v1.0.3", "draft": true, "assets": [] }
            ]
            """;
        var list = ReleaseInfo.ParseList(json);
        Assert.Equal(2, list.Count);
        var r = list[0];
        Assert.True(r.IsPreRelease);
        Assert.False(r.IsDraft);
        Assert.Equal("1.0.2", r.Version!.ToString());
        Assert.Single(r.Assets);
        Assert.Equal(71420497, r.Assets[0].Size);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero), r.PublishedAt);
        Assert.True(list[1].IsDraft);
        var c = ReleaseSelector.SelectUpdate(list, SemVersion.Parse("1.0.1"), includePreReleases: true)!;
        Assert.Equal("3a7bd3e2360a3d29eea436fcfb7e44c735d117c42d1c1835420b6b9942dd4f1b", c.BodyHash);
    }
}

/// <summary>Fake HTTP: URL → (status, body).</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    public readonly Dictionary<string, (HttpStatusCode Status, byte[] Body)> Routes = new();
    public readonly List<HttpRequestMessage> Requests = new();
    public TimeSpan Delay { get; set; }

    public void Text(string url, string body, HttpStatusCode status = HttpStatusCode.OK) => Routes[url] = (status, Encoding.UTF8.GetBytes(body));
    public void Bytes(string url, byte[] body) => Routes[url] = (HttpStatusCode.OK, body);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
        if (!Routes.TryGetValue(url, out var r)) return new HttpResponseMessage(HttpStatusCode.NotFound);
        return new HttpResponseMessage(r.Status) { Content = new ByteArrayContent(r.Body) };
    }
}

public sealed class UpdateClientTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sqlexpert-upd-" + Guid.NewGuid().ToString("N"));

    public UpdateClientTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private const string Api = "https://api.example.invalid/releases";

    private static string ReleasesJson(long size, string extraAsset = "", string body = "") => $$"""
        [ { "tag_name": "v1.0.2", "name": "1.0.2", "body": "{{body}}", "draft": false, "prerelease": false,
            "html_url": "https://example.invalid/r/v1.0.2",
            "assets": [ { "name": "SqlExpert-1.0.2-win-x64.zip", "size": {{size}}, "browser_download_url": "https://dl.example.invalid/SqlExpert-1.0.2-win-x64.zip" } {{extraAsset}} ] } ]
        """;

    [Fact]
    public async Task Check_SendsUserAgent_AndFindsCandidate()
    {
        var http = new FakeHttp();
        http.Text(Api, ReleasesJson(10));
        using var client = new UpdateClient(http, "1.0.1") { ReleasesUrl = Api };
        var r = await UpdateChecker.CheckAsync(client, SemVersion.Parse("1.0.1"), false, null, TimeSpan.FromSeconds(5));
        Assert.Null(r.Error);
        Assert.Equal("1.0.2", r.Candidate!.Version.ToString());
        Assert.Contains(http.Requests[0].Headers.UserAgent, u => u.Product?.Name == "SqlExpert");
        Assert.Contains("per_page=30", http.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task Check_UpToDate()
    {
        var http = new FakeHttp();
        http.Text(Api, ReleasesJson(10));
        using var client = new UpdateClient(http) { ReleasesUrl = Api };
        var r = await UpdateChecker.CheckAsync(client, SemVersion.Parse("1.0.2"), true, null, TimeSpan.FromSeconds(5));
        Assert.True(r.IsUpToDate);
    }

    [Fact]
    public async Task Check_HttpErrorAndTimeout_ComeBackAsError()
    {
        var http = new FakeHttp();
        http.Text(Api, "{\"message\":\"rate limit\"}", HttpStatusCode.Forbidden);
        using var client = new UpdateClient(http) { ReleasesUrl = Api };
        var r = await UpdateChecker.CheckAsync(client, SemVersion.Parse("1.0.1"), true, null, TimeSpan.FromSeconds(5));
        Assert.Contains("403", r.Error);
        Assert.Null(r.Candidate);

        var slow = new FakeHttp { Delay = TimeSpan.FromSeconds(5) };
        slow.Text(Api, ReleasesJson(10));
        using var slowClient = new UpdateClient(slow) { ReleasesUrl = Api };
        var t = await UpdateChecker.CheckAsync(slowClient, SemVersion.Parse("1.0.1"), true, null, TimeSpan.FromMilliseconds(100));
        Assert.Contains("Tempo esgotado", t.Error);
    }

    [Fact]
    public async Task Check_BadJson_IsError()
    {
        var http = new FakeHttp();
        http.Text(Api, "<html>");
        using var client = new UpdateClient(http) { ReleasesUrl = Api };
        var r = await UpdateChecker.CheckAsync(client, SemVersion.Parse("1.0.1"), true, null, TimeSpan.FromSeconds(5));
        Assert.NotNull(r.Error);
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    [Fact]
    public async Task Download_VerifiesSizeAndHash()
    {
        var payload = Encoding.UTF8.GetBytes(new string('x', 300_000));
        var http = new FakeHttp();
        http.Bytes("https://dl.example.invalid/SqlExpert-1.0.2-win-x64.zip", payload);
        using var client = new UpdateClient(http);
        var asset = new ReleaseAsset("SqlExpert-1.0.2-win-x64.zip", payload.Length, "https://dl.example.invalid/SqlExpert-1.0.2-win-x64.zip");
        var dest = Path.Combine(_dir, "a.zip");
        var reports = new List<DownloadProgress>();
        var progress = new SyncProgress(reports.Add);
        var hash = await client.DownloadAsync(asset, dest, Sha(payload).ToUpperInvariant(), progress, CancellationToken.None);
        Assert.Equal(Sha(payload), hash);
        Assert.Equal(payload.Length, new FileInfo(dest).Length);
        Assert.Equal(payload.Length, reports[^1].Received);
        Assert.Equal(payload.Length, reports[^1].Total);

        // wrong hash → error, no file left
        var dest2 = Path.Combine(_dir, "b.zip");
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(asset, dest2, new string('0', 64), null, CancellationToken.None));
        Assert.False(File.Exists(dest2));
        Assert.False(File.Exists(dest2 + ".part"));

        // size mismatch → error
        var wrongSize = asset with { Size = payload.Length + 10 };
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(wrongSize, dest2, null, null, CancellationToken.None));
        var tooSmall = asset with { Size = payload.Length - 10 };
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(tooSmall, dest2, null, null, CancellationToken.None));
        Assert.False(File.Exists(dest2));
    }

    [Fact]
    public async Task Download_Cancelled_LeavesNothing()
    {
        var http = new FakeHttp { Delay = TimeSpan.FromSeconds(5) };
        http.Bytes("https://dl.example.invalid/p.zip", new byte[10]);
        using var client = new UpdateClient(http);
        using var cts = new CancellationTokenSource(100);
        var dest = Path.Combine(_dir, "c.zip");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.DownloadAsync(new ReleaseAsset("p.zip", 10, "https://dl.example.invalid/p.zip"), dest, null, null, cts.Token));
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public async Task ExpectedHash_FromFileAndBody_MustAgree()
    {
        var hex = new string('a', 64);
        var other = new string('b', 64);
        var http = new FakeHttp();
        http.Text("https://dl.example.invalid/SqlExpert-1.0.2-win-x64.zip.sha256", $"{hex}  SqlExpert-1.0.2-win-x64.zip");
        http.Text(Api, ReleasesJson(10, ", { \"name\": \"SqlExpert-1.0.2-win-x64.zip.sha256\", \"size\": 90, \"browser_download_url\": \"https://dl.example.invalid/SqlExpert-1.0.2-win-x64.zip.sha256\" }", "SHA256: " + hex));
        using var client = new UpdateClient(http) { ReleasesUrl = Api };
        var c = (await UpdateChecker.CheckAsync(client, SemVersion.Parse("1.0.1"), false, null, TimeSpan.FromSeconds(5))).Candidate!;
        Assert.Equal(hex, await UpdateChecker.ResolveExpectedHashAsync(client, c, CancellationToken.None));

        var conflicting = c with { BodyHash = other };
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateChecker.ResolveExpectedHashAsync(client, conflicting, CancellationToken.None));

        var none = c with { HashAsset = null, BodyHash = null };
        Assert.Null(await UpdateChecker.ResolveExpectedHashAsync(client, none, CancellationToken.None));
        var bodyOnly = c with { HashAsset = null, BodyHash = other };
        Assert.Equal(other, await UpdateChecker.ResolveExpectedHashAsync(client, bodyOnly, CancellationToken.None));
    }

    private sealed class SyncProgress : IProgress<DownloadProgress>
    {
        private readonly Action<DownloadProgress> _a;
        public SyncProgress(Action<DownloadProgress> a) => _a = a;
        public void Report(DownloadProgress value) => _a(value);
    }
}

public sealed class UpdatePackageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sqlexpert-pkg-" + Guid.NewGuid().ToString("N"));

    public UpdatePackageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Zip(params (string Name, int Size)[] entries)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, size) in entries)
        {
            var e = zip.CreateEntry(name);
            using var s = e.Open();
            s.Write(new byte[size]);
        }
        return path;
    }

    [Fact]
    public void Extract_RootOrSingleFolder()
    {
        var a = UpdatePackage.ExtractAndValidate(Zip(("SqlExpert.exe", 4096), ("SqlExpert.pdb", 10)), Path.Combine(_dir, "s1"));
        Assert.Equal(Path.Combine(_dir, "s1"), a);
        var b = UpdatePackage.ExtractAndValidate(Zip(("SqlExpert-1.0.2-win-x64/SqlExpert.exe", 4096)), Path.Combine(_dir, "s2"));
        Assert.Equal(Path.Combine(_dir, "s2", "SqlExpert-1.0.2-win-x64"), b);
    }

    [Fact]
    public void Extract_RefusesMissingExe_EmptyExe_AndZipSlip()
    {
        Assert.Throws<InvalidDataException>(() => UpdatePackage.ExtractAndValidate(Zip(("readme.txt", 10)), Path.Combine(_dir, "x1")));
        Assert.Throws<InvalidDataException>(() => UpdatePackage.ExtractAndValidate(Zip(("SqlExpert.exe", 0)), Path.Combine(_dir, "x2")));
        Assert.Throws<InvalidDataException>(() => UpdatePackage.ExtractAndValidate(Zip(("../evil.exe", 10), ("SqlExpert.exe", 4096)), Path.Combine(_dir, "x3")));
        Assert.False(File.Exists(Path.Combine(_dir, "evil.exe")));
    }

    [Fact]
    public void Writable_Probe()
    {
        Assert.True(UpdatePackage.IsDirectoryWritable(_dir));
        Assert.False(UpdatePackage.IsDirectoryWritable(Path.Combine(_dir, "does-not-exist")));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Workspace_PathsAndCleanup()
    {
        var ws = UpdateWorkspace.For("1.0.2", _dir);
        Assert.Equal(Path.Combine(_dir, "1.0.2", "staging"), ws.StagingDir);
        Directory.CreateDirectory(ws.BackupDir);
        Directory.CreateDirectory(UpdateWorkspace.For("1.0.3", _dir).DownloadDir);
        Assert.Equal(1, UpdateWorkspace.CleanupOld(_dir, keepVersion: "1.0.3"));
        Assert.False(Directory.Exists(ws.Root));
        Assert.True(Directory.Exists(Path.Combine(_dir, "1.0.3")));
        Assert.Equal(0, UpdateWorkspace.CleanupOld(Path.Combine(_dir, "none")));
    }
}

public sealed class UpdateApplierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sqlexpert-apply-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "new");
    private string Target => Path.Combine(_root, "app");
    private string Backup => Path.Combine(_root, "backup");

    public UpdateApplierTests()
    {
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Source, "SqlExpert.exe"), "new exe");
        File.WriteAllText(Path.Combine(Source, "SqlExpert.pdb"), "new pdb");
        Directory.CreateDirectory(Path.Combine(Source, "sub"));
        File.WriteAllText(Path.Combine(Source, "sub", "extra.dll"), "new extra");
        File.WriteAllText(Path.Combine(Target, "SqlExpert.exe"), "old exe");
        File.WriteAllText(Path.Combine(Target, "SqlExpert.pdb"), "old pdb");
        File.WriteAllText(Path.Combine(Target, "user-notes.txt"), "keep me");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Read(params string[] parts) => File.ReadAllText(Path.Combine(parts));

    [Fact]
    public void Apply_CopiesNewFiles_BacksUpReplaced_KeepsOthers()
    {
        var log = new List<string>();
        var r = new UpdateApplier(log: log.Add).Apply(Source, Target, Backup);
        Assert.True(r.Success, r.Error);
        Assert.Equal(3, r.FilesCopied);
        Assert.Equal("new exe", Read(Target, "SqlExpert.exe"));
        Assert.Equal("new extra", Read(Target, "sub", "extra.dll"));
        Assert.Equal("keep me", Read(Target, "user-notes.txt"));
        Assert.Equal("old exe", Read(Backup, "SqlExpert.exe"));
        Assert.Equal("old pdb", Read(Backup, "SqlExpert.pdb"));
        Assert.EndsWith("SqlExpert.exe", log.Last(l => l.StartsWith("ok:")));
    }

    /// <summary>Copies through the real file system, but throws for chosen destinations.</summary>
    private sealed class FlakyFs : IUpdateFileSystem
    {
        public Func<string, string, int, Exception?> FailCopy { get; set; } = (_, _, _) => null;
        private readonly Dictionary<string, int> _calls = new(StringComparer.OrdinalIgnoreCase);
        public void Copy(string source, string destination)
        {
            _calls[destination] = _calls.GetValueOrDefault(destination) + 1;
            if (FailCopy(source, destination, _calls[destination]) is { } ex) throw ex;
            PhysicalUpdateFileSystem.Instance.Copy(source, destination);
        }
        public void Delete(string path) => PhysicalUpdateFileSystem.Instance.Delete(path);
    }

    [Fact]
    public void LockedFile_IsRetried()
    {
        var fs = new FlakyFs();
        var target = Path.Combine(Target, "SqlExpert.exe");
        fs.FailCopy = (_, dst, n) => string.Equals(dst, target, StringComparison.OrdinalIgnoreCase) && n <= 3 ? new IOException("in use") : null;
        var sleeps = 0;
        var r = new UpdateApplier(fs, attempts: 5, sleep: _ => sleeps++).Apply(Source, Target, Backup);
        Assert.True(r.Success, r.Error);
        Assert.Equal(3, sleeps);
        Assert.Equal("new exe", Read(Target, "SqlExpert.exe"));
    }

    [Fact]
    public void Failure_RollsBack_ToTheOldFiles()
    {
        var fs = new FlakyFs();
        var target = Path.Combine(Target, "SqlExpert.exe");
        fs.FailCopy = (src, dst, _) => string.Equals(dst, target, StringComparison.OrdinalIgnoreCase) && src.StartsWith(Source, StringComparison.OrdinalIgnoreCase)
            ? new UnauthorizedAccessException("denied")
            : null;
        var r = new UpdateApplier(fs, attempts: 2, sleep: _ => { }).Apply(Source, Target, Backup);
        Assert.False(r.Success);
        Assert.True(r.RolledBack);
        Assert.Contains("denied", r.Error);
        Assert.Equal("old exe", Read(Target, "SqlExpert.exe"));
        Assert.Equal("old pdb", Read(Target, "SqlExpert.pdb"));
        Assert.False(File.Exists(Path.Combine(Target, "sub", "extra.dll"))); // added file removed
        Assert.Equal("keep me", Read(Target, "user-notes.txt"));
    }

    [Fact]
    public void EmptySource_Fails_WithoutTouchingTarget()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        var r = new UpdateApplier().Apply(empty, Target, Backup);
        Assert.False(r.Success);
        Assert.Equal("old exe", Read(Target, "SqlExpert.exe"));
    }

    [Fact]
    public void Runner_WaitsThenApplies_AndRelaunchesNewExe()
    {
        var cmd = new UpdaterCommandLine(4242, Source, Target, Backup, "1.0.2");
        int? waited = null;
        var o = UpdaterRunner.Run(cmd, (pid, _) => { waited = pid; return true; }, new UpdateApplier());
        Assert.Equal(4242, waited);
        Assert.True(o.Success);
        Assert.Equal(Path.Combine(Target, "SqlExpert.exe"), o.RelaunchPath);
        Assert.Equal("new exe", Read(Target, "SqlExpert.exe"));
    }

    [Fact]
    public void Runner_OldProcessStillRunning_ChangesNothing()
    {
        var cmd = new UpdaterCommandLine(4242, Source, Target, Backup, "1.0.2");
        var o = UpdaterRunner.Run(cmd, (_, _) => false, new UpdateApplier());
        Assert.False(o.Success);
        Assert.Null(o.RelaunchPath);
        Assert.Equal("old exe", Read(Target, "SqlExpert.exe"));
    }

    [Fact]
    public void Runner_RollbackRelaunchesOldExe()
    {
        var fs = new FlakyFs { FailCopy = (src, dst, _) => dst.EndsWith("extra.dll") ? new IOException("disk full") : null };
        var cmd = new UpdaterCommandLine(1, Source, Target, Backup, "1.0.2");
        var o = UpdaterRunner.Run(cmd, (_, _) => true, new UpdateApplier(fs, attempts: 1));
        Assert.False(o.Success);
        Assert.Contains("versão anterior foi mantida", o.Message);
        Assert.Equal(Path.Combine(Target, "SqlExpert.exe"), o.RelaunchPath);
        Assert.Equal("old exe", Read(Target, "SqlExpert.exe"));
    }

    [Fact]
    public void CommandLine_RoundTrips()
    {
        var cmd = new UpdaterCommandLine(123, @"C:\a b\new", @"C:\Ferramentas\SqlExpert", @"C:\x\backup", "1.0.2");
        var args = cmd.ToArguments();
        Assert.True(UpdaterCommandLine.IsUpdaterInvocation(args));
        Assert.Equal(cmd, UpdaterCommandLine.TryParse(args));
        Assert.Null(UpdaterCommandLine.TryParse(Array.Empty<string>()));
        Assert.Null(UpdaterCommandLine.TryParse(new[] { "--apply-update", "--pid", "x" }));
        Assert.Null(UpdaterCommandLine.TryParse(new[] { "somefile.sql" }));
    }
}

public sealed class UpdateSettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "sqlexpert-set-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }

    [Fact]
    public void Defaults_AndRoundTrip()
    {
        var store = new UserSettingsStore(_path);
        var d = store.Load();
        Assert.True(d.CheckForUpdatesOnStartup);
        Assert.Null(d.IncludePreReleases);
        Assert.Null(d.SkippedUpdateVersion);

        store.Save(new UserSettings { CheckForUpdatesOnStartup = false, IncludePreReleases = false, SkippedUpdateVersion = "1.0.2" });
        var l = store.Load();
        Assert.False(l.CheckForUpdatesOnStartup);
        Assert.False(l.IncludePreReleases);
        Assert.Equal("1.0.2", l.SkippedUpdateVersion);

        store.Save(new UserSettings());
        var c = store.Load();
        Assert.True(c.CheckForUpdatesOnStartup);
        Assert.Null(c.IncludePreReleases);
        Assert.Null(c.SkippedUpdateVersion);
    }
}
