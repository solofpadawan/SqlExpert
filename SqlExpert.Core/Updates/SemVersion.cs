using System.Globalization;

namespace SqlExpert.Core.Updates;

/// <summary>
/// Semantic version (MAJOR.MINOR.PATCH[-prerelease][+build]) as used by release tags ("v1.0.1", "v1.1.0-beta.2").
/// Ordering follows semver 2.0 precedence: a pre-release sorts before the release with the same numbers,
/// pre-release identifiers compare numerically when both are numbers, otherwise ordinally; build metadata is ignored.
/// </summary>
public sealed class SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
{
    public SemVersion(int major, int minor, int patch, string? preRelease = null)
    {
        if (major < 0 || minor < 0 || patch < 0) throw new ArgumentOutOfRangeException(nameof(major));
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = string.IsNullOrWhiteSpace(preRelease) ? null : preRelease.Trim();
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    /// <summary>Text after '-' ("beta.2"), or null for a normal release.</summary>
    public string? PreRelease { get; }
    public bool IsPreRelease => PreRelease is not null;

    /// <summary>Accepts an optional leading 'v'/'V', 2 or 3 numeric parts, an optional "-suffix" and "+build".</summary>
    public static bool TryParse(string? text, out SemVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.Length > 1 && (s[0] == 'v' || s[0] == 'V')) s = s[1..];
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        string? pre = null;
        var dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..];
            s = s[..dash];
            if (pre.Length == 0 || pre.Split('.').Any(p => p.Length == 0 || !p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
                return false;
        }
        var parts = s.Split('.');
        if (parts.Length is < 2 or > 3) return false;
        var nums = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit)) return false;
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out nums[i])) return false;
        }
        version = new SemVersion(nums[0], nums[1], nums[2], pre);
        return true;
    }

    public static SemVersion Parse(string text) =>
        TryParse(text, out var v) ? v! : throw new FormatException($"Versão inválida: '{text}'.");

    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (PreRelease is null) return other.PreRelease is null ? 0 : 1;
        if (other.PreRelease is null) return -1;
        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    private static int ComparePreRelease(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            var na = pa[i].All(char.IsAsciiDigit);
            var nb = pb[i].All(char.IsAsciiDigit);
            int c;
            if (na && nb)
            {
                var ta = pa[i].TrimStart('0');
                var tb = pb[i].TrimStart('0');
                c = ta.Length != tb.Length ? ta.Length.CompareTo(tb.Length) : string.CompareOrdinal(ta, tb);
            }
            else if (na) c = -1; // numeric identifiers have lower precedence
            else if (nb) c = 1;
            else c = string.CompareOrdinal(pa[i].ToLowerInvariant(), pb[i].ToLowerInvariant());
            if (c != 0) return c;
        }
        return pa.Length.CompareTo(pb.Length);
    }

    public bool Equals(SemVersion? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemVersion v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease?.ToLowerInvariant());

    public static bool operator >(SemVersion a, SemVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(SemVersion a, SemVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(SemVersion a, SemVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(SemVersion a, SemVersion b) => a.CompareTo(b) <= 0;

    /// <summary>"1.0.1" or "1.1.0-beta.2" (no 'v').</summary>
    public override string ToString() =>
        PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";
}
