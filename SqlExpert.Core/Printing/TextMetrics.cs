using System.Globalization;
using System.Text;

namespace SqlExpert.Core.Printing;

/// <summary>
/// Width estimates for Helvetica (PDF base-14 font) — Arial is metric-compatible, so the WPF preview uses Arial with the
/// same numbers. Bold is estimated slightly wider so trimmed text never overflows its cell.
/// </summary>
public static class TextMetrics
{
    public const string Ellipsis = "\u2026";
    private const double BoldFactor = 1.08;
    private const int DefaultWidth = 556;

    // Advance widths (1/1000 em) of U+0020..U+007E.
    private static readonly short[] Ascii =
    {
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    };

    /// <summary>Advance width of one character in 1/1000 em.</summary>
    public static int CharWidth(char ch)
    {
        if (ch >= ' ' && ch <= '~') return Ascii[ch - ' '];
        switch (ch)
        {
            case '\u2026': return 1000; // …
            case '\u00A0': return 278;
            case '\u00B0': return 400;  // °
            case '\u00AA' or '\u00BA': return 370; // ª º
            case '\u20AC': return 556;  // €
            case '\u2013': return 556;  // –
            case '\u2014': return 1000; // —
            case '\u00ED' or '\u00EC' or '\u00EE' or '\u00EF': return 278; // í ì î ï
            case '\u00CD' or '\u00CC' or '\u00CE' or '\u00CF': return 278;
        }

        if (ch < 0x2000)
        {
            // Accented Latin letters: width of the base letter (á → a, Ç → C, …).
            var d = ch.ToString().Normalize(NormalizationForm.FormD);
            if (d.Length > 0 && d[0] >= ' ' && d[0] <= '~' && d[0] != ch) return Ascii[d[0] - ' '];
        }

        return CharUnicodeInfo.GetUnicodeCategory(ch) is UnicodeCategory.OtherLetter ? 1000 : DefaultWidth;
    }

    /// <summary>Width of <paramref name="text"/> at <paramref name="fontSize"/> (same unit as the result).</summary>
    public static double Measure(string? text, double fontSize, bool bold = false)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        long units = 0;
        foreach (var ch in text) units += CharWidth(ch);
        return units / 1000.0 * fontSize * (bold ? BoldFactor : 1.0);
    }

    /// <summary>Text that fits <paramref name="width"/>: unchanged, or cut with "…" (empty when not even "…" fits).</summary>
    public static string Fit(string? text, double width, double fontSize, bool bold = false)
    {
        if (string.IsNullOrEmpty(text) || width <= 0) return "";
        if (Measure(text, fontSize, bold) <= width + 0.01) return text;
        var ell = Measure(Ellipsis, fontSize, bold);
        if (ell > width) return "";
        var scale = fontSize * (bold ? BoldFactor : 1.0) / 1000.0;
        var used = ell;
        var n = 0;
        while (n < text.Length)
        {
            var w = CharWidth(text[n]) * scale;
            if (used + w > width) break;
            used += w;
            n++;
        }

        // Do not leave half a surrogate pair.
        if (n > 0 && char.IsHighSurrogate(text[n - 1])) n--;
        return text[..n].TrimEnd() + Ellipsis;
    }
}
