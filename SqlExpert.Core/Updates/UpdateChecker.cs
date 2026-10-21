namespace SqlExpert.Core.Updates;

/// <summary>Result of an update check: a newer release, "up to date" (no candidate, no error) or a failure.</summary>
public sealed record UpdateCheckResult(UpdateCandidate? Candidate, string? Error, int ReleaseCount)
{
    public bool IsUpToDate => Candidate is null && Error is null;
}

/// <summary>Release list → candidate, and the expected SHA-256 of the package.</summary>
public static class UpdateChecker
{
    public static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ManualTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Never throws (except on cancellation by <paramref name="ct"/>): network or format errors come back in <see cref="UpdateCheckResult.Error"/>.</summary>
    public static async Task<UpdateCheckResult> CheckAsync(UpdateClient client, SemVersion current, bool includePreReleases,
        string? skippedVersion, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            var releases = await client.GetReleasesAsync(cts.Token).ConfigureAwait(false);
            var candidate = ReleaseSelector.SelectUpdate(releases, current, includePreReleases, skippedVersion);
            return new UpdateCheckResult(candidate, null, releases.Count);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new UpdateCheckResult(null, "Tempo esgotado ao consultar o GitHub.", 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new UpdateCheckResult(null, ex.Message, 0);
        }
    }

    /// <summary>
    /// Expected SHA-256 of the package: from the .sha256 asset and/or the "SHA256:" line of the notes. Null when the
    /// release publishes none (then only the size is checked). Throws when both exist and disagree.
    /// </summary>
    public static async Task<string?> ResolveExpectedHashAsync(UpdateClient client, UpdateCandidate candidate, CancellationToken ct)
    {
        string? fromFile = null;
        if (candidate.HashAsset is { } h)
        {
            var text = await client.GetTextAsync(h.DownloadUrl, ct).ConfigureAwait(false);
            fromFile = ReleaseSelector.ParseHashFile(text, candidate.Package.Name)
                ?? throw new InvalidDataException($"{h.Name} não contém um SHA-256 válido.");
        }
        var fromBody = candidate.BodyHash;
        if (fromFile is not null && fromBody is not null && !string.Equals(fromFile, fromBody, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O SHA-256 das notas da versão é diferente do arquivo .sha256 publicado.");
        return fromFile ?? fromBody;
    }
}

/// <summary>Plain-text log of update checks and installs (%AppData%\SqlExpert\update.log); never throws.</summary>
public static class UpdateLog
{
    private static readonly object Gate = new();

    public static string PathOverride { get; set; } = "";

    public static string FilePath => !string.IsNullOrEmpty(PathOverride)
        ? PathOverride
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SqlExpert", "update.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 512 * 1024) File.Delete(path);
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
