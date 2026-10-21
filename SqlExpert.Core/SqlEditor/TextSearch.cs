using System.Text.RegularExpressions;

namespace SqlExpert.Core.SqlEditor;

/// <summary>Find / replace over plain text (used by the SQL Editor Replace dialog). Literal search, never regex.</summary>
public static class TextSearch
{
    private static Regex Build(string term, bool matchCase, bool wholeWord)
    {
        var pattern = Regex.Escape(term);
        if (wholeWord) pattern = @"(?<![\w@#$])" + pattern + @"(?![\w@#$])";
        var options = RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        return new Regex(pattern, options);
    }

    /// <summary>
    /// Next occurrence at or after <paramref name="start"/> (or before it when <paramref name="backward"/>), wrapping
    /// around the text. Returns (index, length) or (-1, 0).
    /// </summary>
    public static (int Index, int Length) FindNext(string text, string term, int start, bool matchCase, bool wholeWord, bool backward = false)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term)) return (-1, 0);
        start = Math.Clamp(start, 0, text.Length);
        var matches = Build(term, matchCase, wholeWord).Matches(text).Cast<Match>().ToList();
        if (matches.Count == 0) return (-1, 0);
        Match? m = backward
            ? matches.LastOrDefault(x => x.Index + x.Length <= start) ?? matches[^1]
            : matches.FirstOrDefault(x => x.Index >= start) ?? matches[0];
        return (m.Index, m.Length);
    }

    public static int Count(string text, string term, bool matchCase, bool wholeWord) =>
        string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term) ? 0 : Build(term, matchCase, wholeWord).Matches(text).Count;

    /// <summary>True when the selected text is exactly one occurrence of the term (Replace uses it before moving on).</summary>
    public static bool IsMatch(string selected, string term, bool matchCase, bool wholeWord) =>
        !string.IsNullOrEmpty(term) && Build(term, matchCase, false).Match(selected) is { Success: true } m
        && m.Index == 0 && m.Length == selected.Length;

    public static (string Text, int Count) ReplaceAll(string text, string term, string replacement, bool matchCase, bool wholeWord)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term)) return (text ?? "", 0);
        var count = 0;
        var result = Build(term, matchCase, wholeWord).Replace(text, _ => { count++; return replacement ?? ""; });
        return (result, count);
    }
}
