using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace SqlExpert.Core.Updates;

/// <summary>Download progress: bytes received so far and the expected total (-1 when unknown).</summary>
public readonly record struct DownloadProgress(long Received, long Total);

/// <summary>
/// Unauthenticated access to the public GitHub releases of the project: release list and package download with
/// size and SHA-256 verification. The HTTP handler is injectable so tests run without network.
/// </summary>
public sealed class UpdateClient : IDisposable
{
    private readonly HttpClient _http;

    public UpdateClient(HttpMessageHandler? handler = null, string? userAgentVersion = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan; // per-call timeouts come from the caller's token
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SqlExpert", string.IsNullOrWhiteSpace(userAgentVersion) ? "1.0" : userAgentVersion));
    }

    public string ReleasesUrl { get; init; } = ReleaseSelector.ReleasesApiUrl;

    public void Dispose() => _http.Dispose();

    /// <summary>GET the releases list (first page, newest first). Throws on HTTP or JSON errors.</summary>
    public async Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl + (ReleasesUrl.Contains('?') ? "&" : "?") + "per_page=30");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"GitHub respondeu {(int)resp.StatusCode} {resp.ReasonPhrase}.");
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ReleaseInfo.ParseList(json);
    }

    /// <summary>Small text asset (the .sha256 file).</summary>
    public async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads <paramref name="asset"/> to <paramref name="destination"/> (via a .part file), checks the size
    /// against the asset size and, when given, the SHA-256. Returns the SHA-256 (lower-case hex) of the file.
    /// A failed or cancelled download leaves no file behind.
    /// </summary>
    public async Task<string> DownloadAsync(ReleaseAsset asset, string destination, string? expectedSha256,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(destination));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var part = destination + ".part";
        try
        {
            using var resp = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Download falhou: {(int)resp.StatusCode} {resp.ReasonPhrase}.");
            var total = asset.Size > 0 ? asset.Size : resp.Content.Headers.ContentLength ?? -1;
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int n;
                var lastReport = 0L;
                progress?.Report(new DownloadProgress(0, total));
                while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    sha.AppendData(buffer, 0, n);
                    received += n;
                    if (asset.Size > 0 && received > asset.Size)
                        throw new InvalidDataException("O arquivo baixado é maior que o anunciado pelo GitHub.");
                    if (received - lastReport >= 256 * 1024) { lastReport = received; progress?.Report(new DownloadProgress(received, total)); }
                }
            }
            progress?.Report(new DownloadProgress(received, total));
            if (asset.Size > 0 && received != asset.Size)
                throw new InvalidDataException($"Tamanho do download não confere ({received} de {asset.Size} bytes).");
            var hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(expectedSha256) && !string.Equals(hash, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA-256 do download não confere.\nEsperado: {expectedSha256.Trim().ToLowerInvariant()}\nObtido:   {hash}");
            File.Move(part, destination, overwrite: true);
            return hash;
        }
        catch
        {
            TryDelete(part);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
