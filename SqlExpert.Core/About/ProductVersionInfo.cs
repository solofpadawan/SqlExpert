using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SqlExpert.Core.About;

/// <summary>
/// Version facts shown on the splash screen and in Help &gt; About, read from the real build:
/// InformationalVersion/FileVersion of the entry assembly, the BuildDate assembly metadata stamped by MSBuild
/// (see SqlExpert.App.csproj), the process architecture and the .NET runtime version.
/// </summary>
public sealed record ProductVersionInfo(
    string ProductName,
    string Version,
    DateTimeOffset? BuildDate,
    string Architecture,
    string RuntimeVersion)
{
    /// <summary>Name of the <see cref="AssemblyMetadataAttribute"/> that carries the build timestamp (ISO 8601, UTC).</summary>
    public const string BuildDateMetadataKey = "BuildDate";

    /// <summary>Short line used on the splash, e.g. "SqlExpert 1.0.0 (build 2026-10-20) - x64 - .NET 8.0.21".</summary>
    public string DisplayText => Format(this, includeTime: false, TimeZoneInfo.Local);

    /// <summary>Same line with the build time, e.g. "... (build 2026-10-20 10:58) ...".</summary>
    public string DetailedText => Format(this, includeTime: true, TimeZoneInfo.Local);

    public static string Format(ProductVersionInfo info, bool includeTime, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(zone);
        var parts = new List<string>();
        var head = string.IsNullOrWhiteSpace(info.Version) ? info.ProductName : $"{info.ProductName} {info.Version}";
        if (info.BuildDate is { } built)
        {
            var local = TimeZoneInfo.ConvertTime(built, zone);
            var stamp = local.ToString(includeTime ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd", CultureInfo.InvariantCulture);
            head += $" (build {stamp})";
        }
        parts.Add(head);
        if (!string.IsNullOrWhiteSpace(info.Architecture)) parts.Add(info.Architecture);
        if (!string.IsNullOrWhiteSpace(info.RuntimeVersion)) parts.Add($".NET {info.RuntimeVersion}");
        return string.Join(" - ", parts);
    }

    /// <summary>
    /// "1.0.0+abc123" -&gt; "1.0.0" (SDK source-link suffix removed); falls back to the file version,
    /// then the assembly version (trailing ".0" revision trimmed to 3 parts).
    /// </summary>
    public static string NormalizeVersion(string? informationalVersion, string? fileVersion, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var v = informationalVersion.Trim();
            var plus = v.IndexOf('+');
            if (plus > 0) v = v[..plus];
            if (v.Length > 0) return v;
        }
        if (!string.IsNullOrWhiteSpace(fileVersion) && System.Version.TryParse(fileVersion.Trim(), out var fv))
            return TrimRevision(fv);
        if (assemblyVersion is not null)
            return TrimRevision(assemblyVersion);
        return "";
    }

    private static string TrimRevision(Version v) =>
        v.Revision > 0 ? v.ToString(4) : v.ToString(Math.Max(2, v.Build >= 0 ? 3 : 2));

    /// <summary>Parses the BuildDate metadata (round-trip "o" format); null when missing or invalid.</summary>
    public static DateTimeOffset? ParseBuildDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
    }

    public static string ArchitectureName(Architecture arch) => arch switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
        System.Runtime.InteropServices.Architecture.Arm => "ARM",
        _ => arch.ToString().ToLowerInvariant(),
    };

    /// <summary>Reads the version facts of <paramref name="assembly"/> (default: entry assembly) and the running process.</summary>
    public static ProductVersionInfo FromAssembly(Assembly? assembly = null, string? productName = null)
    {
        assembly ??= Assembly.GetEntryAssembly() ?? typeof(ProductVersionInfo).Assembly;
        var name = productName
                   ?? assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
                   ?? assembly.GetName().Name
                   ?? "SqlExpert";
        var version = NormalizeVersion(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version,
            assembly.GetName().Version);
        var buildDate = ParseBuildDate(assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, BuildDateMetadataKey, StringComparison.Ordinal))?.Value);
        return new ProductVersionInfo(
            name,
            version,
            buildDate,
            ArchitectureName(RuntimeInformation.ProcessArchitecture),
            Environment.Version.ToString());
    }
}
