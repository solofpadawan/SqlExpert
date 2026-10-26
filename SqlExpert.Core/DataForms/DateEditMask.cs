using System.Globalization;

namespace SqlExpert.Core.DataForms;

/// <summary>Input mask of a date/time editor.</summary>
public enum DateMaskKind
{
    /// <summary>Not a date/time column: plain text editor.</summary>
    None,
    /// <summary>date: dd/MM/yyyy.</summary>
    Date,
    /// <summary>smalldatetime / datetime / datetime2 / datetimeoffset: dd/MM/yyyy HH:mm:ss.</summary>
    DateTime,
    /// <summary>time: HH:mm:ss.</summary>
    Time
}

/// <summary>Text and caret position after a key in a masked editor.</summary>
public readonly record struct MaskEdit(string Text, int Caret);

/// <summary>
/// Fixed-position input mask of the date/time cell editors (Data grid, Form View): pt-BR dd/MM/yyyy HH:mm:ss, digits
/// only, separators fixed, "_" for an empty position. Grids show dates in ISO (<see cref="DateValueText"/>); only the
/// editor uses this format, and it accepts nothing else.
/// <para>Fractional seconds and the offset are not part of the mask: when the edited value is the same as the
/// original value up to the second, the original value is kept unchanged (its fraction and offset included);
/// otherwise the fraction is zero. datetimeoffset keeps the original offset (the local offset for a NULL value).</para>
/// Pure logic, no WPF.
/// </summary>
public static class DateEditMask
{
    public const char Blank = '_';
    public const string DateTemplate = "__/__/____";
    public const string DateTimeTemplate = "__/__/____ __:__:__";
    public const string TimeTemplate = "__:__:__";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Mask of a column: date kinds from <see cref="DateValueText.KindOf(string?)"/>, time from the CLR type.</summary>
    public static DateMaskKind KindOf(DateColumnKind kind, Type? clrType) => kind switch
    {
        DateColumnKind.Date => DateMaskKind.Date,
        DateColumnKind.None => clrType == typeof(TimeSpan) ? DateMaskKind.Time : DateMaskKind.None,
        _ => DateMaskKind.DateTime
    };

    public static string Template(DateMaskKind kind) => kind switch
    {
        DateMaskKind.Date => DateTemplate,
        DateMaskKind.DateTime => DateTimeTemplate,
        DateMaskKind.Time => TimeTemplate,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a masked column.")
    };

    /// <summary>Format shown in messages ("dd/mm/aaaa hh:mm:ss").</summary>
    public static string Hint(DateMaskKind kind) => kind switch
    {
        DateMaskKind.Date => "dd/mm/aaaa",
        DateMaskKind.Time => "hh:mm:ss",
        _ => "dd/mm/aaaa hh:mm:ss"
    };

    // ===== value ⇄ text =====

    /// <summary>Editor text of a value; NULL → the empty mask.</summary>
    public static string Format(object? value, DateMaskKind kind)
    {
        var template = Template(kind);
        switch (value)
        {
            case System.DateTimeOffset o:
                return FormatDate(o.DateTime, kind);
            case System.DateTime d:
                return FormatDate(d, kind);
            case TimeSpan t when kind == DateMaskKind.Time:
                var day = t.Ticks < 0 ? TimeSpan.Zero : new TimeSpan(t.Ticks % TimeSpan.TicksPerDay);
                return day.ToString(@"hh\:mm\:ss", Inv);
            default:
                return template;
        }
    }

    private static string FormatDate(System.DateTime d, DateMaskKind kind) => kind switch
    {
        DateMaskKind.Date => d.ToString("dd/MM/yyyy", Inv),
        DateMaskKind.Time => d.ToString("HH:mm:ss", Inv),
        _ => d.ToString("dd/MM/yyyy HH:mm:ss", Inv)
    };

    /// <summary>
    /// Text in mask shape: already shaped text is kept; anything else (a typed first key, a value set by code) has its
    /// digits placed into the positions in order, the rest blank.
    /// </summary>
    public static string Normalize(string? text, DateMaskKind kind)
    {
        var template = Template(kind);
        text ??= "";
        if (Fits(text, template)) return text;
        var chars = template.ToCharArray();
        var pos = 0;
        foreach (var ch in text)
        {
            if (!char.IsAsciiDigit(ch)) continue;
            pos = NextSlot(template, pos);
            if (pos < 0) break;
            chars[pos++] = ch;
        }
        return new string(chars);
    }

    private static bool Fits(string text, string template)
    {
        if (text.Length != template.Length) return false;
        for (var i = 0; i < text.Length; i++)
        {
            if (template[i] == Blank ? !(text[i] == Blank || char.IsAsciiDigit(text[i])) : text[i] != template[i])
                return false;
        }
        return true;
    }

    public static bool IsEmpty(string? text, DateMaskKind kind) => Normalize(text, kind) == Template(kind);

    public static bool IsComplete(string? text, DateMaskKind kind) => !Normalize(text, kind).Contains(Blank);

    /// <summary>
    /// Typed text → value. Empty mask → DBNull; incomplete or invalid text throws <see cref="FormatException"/> with a
    /// pt-BR message. The original value is returned unchanged when it is equal up to the second (fraction/offset kept).
    /// </summary>
    public static object Parse(string? text, DateMaskKind kind, DateColumnKind dateKind, Type clrType, object? original = null)
    {
        var t = Normalize(text, kind);
        if (t == Template(kind)) return DBNull.Value;
        var what = kind == DateMaskKind.Time ? "Hora" : "Data";
        if (t.Contains(Blank)) throw new FormatException($"{what} incompleta: \"{t}\". Use {Hint(kind)}.");

        if (kind == DateMaskKind.Time)
        {
            int h = Num(t, 0, 2), m = Num(t, 3, 2), s = Num(t, 6, 2);
            if (h > 23 || m > 59 || s > 59) throw new FormatException($"Hora inválida: \"{t}\" (00:00:00 a 23:59:59).");
            var time = new TimeSpan(h, m, s);
            return original is TimeSpan ot && TruncateToSecond(ot) == time ? ot : time;
        }

        int day = Num(t, 0, 2), month = Num(t, 3, 2), year = Num(t, 6, 4);
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > System.DateTime.DaysInMonth(year, month))
            throw new FormatException($"Data inválida: \"{t}\" (dia, mês ou ano fora do calendário).");
        int hour = 0, minute = 0, second = 0;
        if (kind == DateMaskKind.DateTime)
        {
            hour = Num(t, 11, 2);
            minute = Num(t, 14, 2);
            second = Num(t, 17, 2);
            if (hour > 23 || minute > 59 || second > 59)
                throw new FormatException($"Hora inválida: \"{t}\" (00:00:00 a 23:59:59).");
        }
        var d = new System.DateTime(year, month, day, hour, minute, second);
        if (dateKind == DateColumnKind.SmallDateTime && (d < new System.DateTime(1900, 1, 1) || d > new System.DateTime(2079, 6, 6, 23, 59, 0)))
            throw new FormatException("smalldatetime aceita de 01/01/1900 00:00:00 a 06/06/2079 23:59:00.");
        if (dateKind == DateColumnKind.DateTime && d.Year < 1753)
            throw new FormatException("datetime aceita datas a partir de 01/01/1753.");

        if (dateKind == DateColumnKind.DateTimeOffset || clrType == typeof(System.DateTimeOffset))
        {
            if (original is System.DateTimeOffset o)
                return TruncateToSecond(o.DateTime) == d ? o : new System.DateTimeOffset(d, o.Offset);
            return new System.DateTimeOffset(d, TimeZoneInfo.Local.GetUtcOffset(d));
        }
        if (kind == DateMaskKind.Date) return original is System.DateTime od0 && od0 == d ? od0 : d;
        return original is System.DateTime od && TruncateToSecond(od) == d ? od : d;
    }

    /// <summary><see cref="Parse"/> without exceptions.</summary>
    public static bool TryParse(string? text, DateMaskKind kind, DateColumnKind dateKind, Type clrType, object? original,
        out object value, out string? error)
    {
        try
        {
            value = Parse(text, kind, dateKind, clrType, original);
            error = null;
            return true;
        }
        catch (FormatException ex)
        {
            value = DBNull.Value;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Error of a complete text (red border while typing); null when valid, empty or still incomplete.</summary>
    public static string? LiveError(string? text, DateMaskKind kind, DateColumnKind dateKind, Type clrType)
    {
        if (!IsComplete(text, kind)) return null;
        return TryParse(text, kind, dateKind, clrType, null, out _, out var error) ? null : error;
    }

    private static int Num(string t, int start, int length) => int.Parse(t.AsSpan(start, length), NumberStyles.None, Inv);

    private static System.DateTime TruncateToSecond(System.DateTime d) => new(d.Ticks - d.Ticks % TimeSpan.TicksPerSecond, d.Kind);

    private static TimeSpan TruncateToSecond(TimeSpan t) => new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond);

    // ===== keys =====

    /// <summary>
    /// A typed character. Digit: replaces the selection with blanks, goes into the next position (separators are
    /// skipped) and the caret moves to the position after it. Separator ('/', ':', ' '): jumps to the next field (a
    /// one-digit day/month/hour/minute/second gets its leading zero). Anything else is ignored.
    /// </summary>
    public static MaskEdit Type(string text, int selStart, int selLength, char ch, DateMaskKind kind)
    {
        var template = Template(kind);
        var t = Normalize(text, kind);
        (selStart, selLength) = Clamp(selStart, selLength, t.Length);
        if (char.IsAsciiDigit(ch))
        {
            var chars = Clear(t, selStart, selLength, template);
            var pos = NextSlot(template, selStart);
            if (pos < 0) return new MaskEdit(t, selStart);
            chars[pos] = ch;
            return new MaskEdit(new string(chars), NextSlotOrEnd(template, pos + 1));
        }
        if (ch is '/' or ':' or ' ' && selLength == 0) return JumpField(t, selStart, ch, template);
        return new MaskEdit(t, selStart);
    }

    private static MaskEdit JumpField(string t, int caret, char ch, string template)
    {
        // field around the caret (the caret may sit right after its last digit)
        var start = caret;
        while (start > 0 && template[start - 1] == Blank) start--;
        var end = start;
        while (end < template.Length && template[end] == Blank) end++;
        if (end == start || end >= template.Length) return new MaskEdit(t, caret);
        if (template[end] != ch) return new MaskEdit(t, caret);
        var chars = t.ToCharArray();
        if (end - start == 2 && char.IsAsciiDigit(chars[start]) && chars[start + 1] == Blank)
        {
            chars[start + 1] = chars[start];
            chars[start] = '0';
        }
        return new MaskEdit(new string(chars), NextSlotOrEnd(template, end + 1));
    }

    /// <summary>Backspace: clears the selection, or the position before the caret (separators skipped).</summary>
    public static MaskEdit Backspace(string text, int selStart, int selLength, DateMaskKind kind)
    {
        var template = Template(kind);
        var t = Normalize(text, kind);
        (selStart, selLength) = Clamp(selStart, selLength, t.Length);
        if (selLength > 0) return new MaskEdit(new string(Clear(t, selStart, selLength, template)), selStart);
        var pos = selStart - 1;
        while (pos >= 0 && template[pos] != Blank) pos--;
        if (pos < 0) return new MaskEdit(t, 0);
        var chars = t.ToCharArray();
        chars[pos] = Blank;
        return new MaskEdit(new string(chars), pos);
    }

    /// <summary>Delete: clears the selection, or the position at the caret (separators skipped); the caret stays.</summary>
    public static MaskEdit Delete(string text, int selStart, int selLength, DateMaskKind kind)
    {
        var template = Template(kind);
        var t = Normalize(text, kind);
        (selStart, selLength) = Clamp(selStart, selLength, t.Length);
        if (selLength > 0) return new MaskEdit(new string(Clear(t, selStart, selLength, template)), selStart);
        var pos = NextSlot(template, selStart);
        if (pos < 0) return new MaskEdit(t, selStart);
        var chars = t.ToCharArray();
        chars[pos] = Blank;
        return new MaskEdit(new string(chars), selStart);
    }

    /// <summary>Paste: the digits of <paramref name="clip"/> fill the positions from the caret (other characters ignored).</summary>
    public static MaskEdit Paste(string text, int selStart, int selLength, string? clip, DateMaskKind kind)
    {
        var template = Template(kind);
        var t = Normalize(text, kind);
        (selStart, selLength) = Clamp(selStart, selLength, t.Length);
        var chars = Clear(t, selStart, selLength, template);
        var pos = selStart;
        var any = false;
        foreach (var ch in clip ?? "")
        {
            if (!char.IsAsciiDigit(ch)) continue;
            var slot = NextSlot(template, pos);
            if (slot < 0) break;
            chars[slot] = ch;
            pos = slot + 1;
            any = true;
        }
        return new MaskEdit(new string(chars), any ? NextSlotOrEnd(template, pos) : selStart);
    }

    /// <summary>Text after clearing the selection (Cut).</summary>
    public static MaskEdit Cut(string text, int selStart, int selLength, DateMaskKind kind) =>
        selLength > 0 ? Delete(text, selStart, selLength, kind) : new MaskEdit(Normalize(text, kind), Math.Max(0, selStart));

    /// <summary>First position (digit place) at or after <paramref name="from"/>; -1 when none.</summary>
    public static int NextSlot(string template, int from)
    {
        for (var i = Math.Max(0, from); i < template.Length; i++)
            if (template[i] == Blank) return i;
        return -1;
    }

    private static int NextSlotOrEnd(string template, int from)
    {
        var next = NextSlot(template, from);
        return next < 0 ? template.Length : next;
    }

    private static char[] Clear(string t, int start, int length, string template)
    {
        var chars = t.ToCharArray();
        for (var i = start; i < start + length && i < chars.Length; i++)
            if (template[i] == Blank) chars[i] = Blank;
        return chars;
    }

    private static (int, int) Clamp(int start, int length, int max)
    {
        start = Math.Clamp(start, 0, max);
        return (start, Math.Clamp(length, 0, max - start));
    }
}
