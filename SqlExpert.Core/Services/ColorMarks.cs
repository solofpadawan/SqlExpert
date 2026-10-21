using System.Globalization;

namespace SqlExpert.Core.Services;

/// <summary>Color Mark of a registration: "#RRGGBB" (null = none) and the dialog palette.</summary>
public static class ColorMarks
{
    public static IReadOnlyList<(string Name, string Hex)> Palette { get; } = new[]
    {
        ("Red", "#D32F2F"), ("Orange", "#F57C00"), ("Amber", "#FFB300"), ("Yellow", "#FDD835"),
        ("Lime", "#9CCC65"), ("Green", "#388E3C"), ("Teal", "#00897B"), ("Cyan", "#00ACC1"),
        ("Blue", "#1E88E5"), ("Navy", "#283593"), ("Purple", "#8E24AA"), ("Pink", "#D81B60"),
        ("Brown", "#6D4C41"), ("Gray", "#757575"), ("Black", "#212121"), ("Silver", "#BDBDBD")
    };

    /// <summary>"#rgb", "#rrggbb", "rrggbb" → "#RRGGBB"; blank → null; anything else → null.</summary>
    public static string? Normalize(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return null;
        if (t.StartsWith('#')) t = t[1..];
        if (t.Length == 3) t = string.Concat(t.Select(c => new string(c, 2)));
        if (t.Length != 6 || !int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return null;
        return "#" + t.ToUpperInvariant();
    }

    /// <summary>True when blank (no color) or a valid color.</summary>
    public static bool IsValidOrEmpty(string? text) => string.IsNullOrWhiteSpace(text) || Normalize(text) is not null;
}
