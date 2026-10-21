using System.Globalization;
using System.Text;

namespace SqlExpert.Core.DataForms;

/// <summary>Keys of the numeric-field calculator (Data grid / Form View "▾" of number columns).</summary>
public enum CalcKey
{
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    Decimal,
    Back, ClearEntry, Clear,
    MemoryClear, MemoryRecall, MemoryStore, MemoryAdd,
    Add, Subtract, Multiply, Divide, Equals,
    Sqrt, Percent, Reciprocal, Negate
}

/// <summary>
/// Classic pocket-calculator logic on <see cref="decimal"/> (no binary floating-point surprises such as 0,1 + 0,2):
/// immediate execution (2 + 3 * 4 = 20), operator keys chain, sqrt / % / 1/x / +/- act on the shown value,
/// one memory register (MC MR MS M+). Division by zero, a negative square root or an overflow put the
/// calculator in an error state until C / CE or a new number. Text uses the given culture's decimal separator.
/// </summary>
public sealed class CalculatorEngine
{
    /// <summary>Most digits kept while typing (decimal holds 28-29 significant digits).</summary>
    public const int MaxEntryDigits = 28;

    private readonly CultureInfo _culture;
    private string? _entry;          // number being typed ("-12.5", invariant '.'), null when showing a result
    private decimal _value;          // shown value when not typing
    private decimal _accumulator;
    private CalcKey? _pending;       // pending binary operator
    private bool _operandReady;      // a new operand was typed / recalled since the operator key
    private decimal _memory;

    public CalculatorEngine(CultureInfo culture, decimal? initial = null)
    {
        _culture = culture ?? throw new ArgumentNullException(nameof(culture));
        _value = initial ?? 0m;
        _operandReady = true;
    }

    /// <summary>Error text ("Não é possível dividir por zero", …) or null.</summary>
    public string? Error { get; private set; }

    public bool HasError => Error is not null;

    /// <summary>Memory holds a non-zero value (the "M" indicator; MC/MR enabled).</summary>
    public bool HasMemory => _memory != 0m;

    public decimal Memory => _memory;

    /// <summary>Operator waiting for its second operand (for the small indicator), or null.</summary>
    public CalcKey? PendingOperator => _pending;

    /// <summary>Value on the display (what "=" would write when nothing is pending).</summary>
    public decimal Value => _entry is null ? _value : ParseEntry(_entry);

    /// <summary>Display text: the typed digits as typed, results without trailing zeros, or the error text.</summary>
    public string Display => Error ?? (_entry is null ? Format(_value, _culture) : EntryText(_entry, _culture));

    /// <summary>"1234,5" (no group separators, no exponent, no trailing zeros, "0" for zero).</summary>
    public static string Format(decimal value, CultureInfo culture)
    {
        if (value == 0m) return "0";
        return value.ToString("0.############################", culture);
    }

    private static string EntryText(string entry, CultureInfo culture) =>
        entry.Replace(".", culture.NumberFormat.NumberDecimalSeparator, StringComparison.Ordinal);

    private static decimal ParseEntry(string entry) =>
        entry is "" or "-" or "." or "-." ? 0m : decimal.Parse(entry, NumberStyles.Number, CultureInfo.InvariantCulture);

    /// <summary>Character typed in the calculator → key (digits, both '.' and ',' as decimal separator, + - * / = %, "r" = 1/x, "@" = sqrt).</summary>
    public static CalcKey? MapChar(char c) => c switch
    {
        >= '0' and <= '9' => CalcKey.D0 + (c - '0'),
        '.' or ',' => CalcKey.Decimal,
        '+' => CalcKey.Add,
        '-' => CalcKey.Subtract,
        '*' or 'x' or 'X' => CalcKey.Multiply,
        '/' or ':' => CalcKey.Divide,
        '=' => CalcKey.Equals,
        '%' => CalcKey.Percent,
        'r' or 'R' => CalcKey.Reciprocal,
        '@' => CalcKey.Sqrt,
        _ => null
    };

    public void Press(CalcKey key)
    {
        if (HasError)
        {
            // Error: C / CE clear it; a digit starts over; everything else is ignored.
            if (key is CalcKey.Clear or CalcKey.ClearEntry) { Reset(); return; }
            if (key is >= CalcKey.D0 and <= CalcKey.D9 or CalcKey.Decimal) Reset();
            else return;
        }

        try
        {
            switch (key)
            {
                case >= CalcKey.D0 and <= CalcKey.D9:
                    TypeDigit((char)('0' + (key - CalcKey.D0)));
                    break;
                case CalcKey.Decimal:
                    TypeDecimal();
                    break;
                case CalcKey.Back:
                    if (_entry is null) break;
                    _entry = _entry.Length <= 1 || _entry is "-0" ? "0" : _entry[..^1];
                    if (_entry is "-" or "") _entry = "0";
                    break;
                case CalcKey.ClearEntry:
                    _entry = "0";
                    _operandReady = true;
                    break;
                case CalcKey.Clear:
                    Reset();
                    break;
                case CalcKey.MemoryClear:
                    _memory = 0m;
                    break;
                case CalcKey.MemoryRecall:
                    ShowResult(_memory);
                    _operandReady = true;
                    break;
                case CalcKey.MemoryStore:
                    _memory = Value;
                    ShowResult(_memory);
                    break;
                case CalcKey.MemoryAdd:
                {
                    var v = Value;
                    _memory = checked(_memory + v);
                    ShowResult(v);
                    break;
                }
                case CalcKey.Add or CalcKey.Subtract or CalcKey.Multiply or CalcKey.Divide:
                    Operator(key);
                    break;
                case CalcKey.Equals:
                    if (_pending is { } op)
                    {
                        var result = Apply(op, _accumulator, Value);
                        _pending = null;
                        ShowResult(result);
                    }
                    else
                    {
                        ShowResult(Value);
                    }
                    _operandReady = true;
                    break;
                case CalcKey.Sqrt:
                {
                    var v = Value;
                    if (v < 0m) { Fail("Entrada inválida (raiz de número negativo)"); break; }
                    ShowResult(Sqrt(v));
                    _operandReady = true;
                    break;
                }
                case CalcKey.Percent:
                {
                    var v = Value;
                    ShowResult(_pending is null ? v / 100m : _accumulator * v / 100m);
                    _operandReady = true;
                    break;
                }
                case CalcKey.Reciprocal:
                {
                    var v = Value;
                    if (v == 0m) { Fail("Não é possível dividir por zero"); break; }
                    ShowResult(1m / v);
                    _operandReady = true;
                    break;
                }
                case CalcKey.Negate:
                    if (_entry is not null)
                        _entry = _entry.StartsWith('-') ? _entry[1..] : _entry == "0" ? "0" : "-" + _entry;
                    else
                        _value = -_value;
                    break;
            }
        }
        catch (OverflowException)
        {
            Fail("Estouro: o resultado é grande demais");
        }
        catch (DivideByZeroException)
        {
            Fail("Não é possível dividir por zero");
        }
    }

    /// <summary>Presses several keys (tests, keyboard macros).</summary>
    public void Press(params CalcKey[] keys)
    {
        foreach (var k in keys) Press(k);
    }

    private void Reset()
    {
        _entry = null;
        _value = 0m;
        _accumulator = 0m;
        _pending = null;
        _operandReady = true;
        Error = null;
    }

    private void Fail(string message)
    {
        Error = message;
        _entry = null;
        _value = 0m;
        _pending = null;
    }

    private void ShowResult(decimal value)
    {
        _entry = null;
        _value = value == 0m ? 0m : value;
    }

    private void TypeDigit(char d)
    {
        if (_entry is null) { _entry = d.ToString(); _operandReady = true; return; }
        var digits = _entry.Count(char.IsDigit);
        if (digits >= MaxEntryDigits) return;
        if (_entry == "0") _entry = d.ToString();
        else if (_entry == "-0") _entry = "-" + d;
        else _entry += d;
        _operandReady = true;
    }

    private void TypeDecimal()
    {
        if (_entry is null) { _entry = "0."; _operandReady = true; return; }
        if (!_entry.Contains('.')) _entry += ".";
        _operandReady = true;
    }

    private void Operator(CalcKey op)
    {
        if (_pending is { } prev && _operandReady)
        {
            var result = Apply(prev, _accumulator, Value);
            ShowResult(result);
            _accumulator = result;
        }
        else if (_pending is null)
        {
            _accumulator = Value;
            ShowResult(_accumulator);
        }
        // else: operator pressed twice → just switch it
        _pending = op;
        _operandReady = false;
    }

    private static decimal Apply(CalcKey op, decimal a, decimal b) => op switch
    {
        CalcKey.Add => checked(a + b),
        CalcKey.Subtract => checked(a - b),
        CalcKey.Multiply => checked(a * b),
        CalcKey.Divide => b == 0m ? throw new DivideByZeroException() : a / b,
        _ => b
    };

    /// <summary>Square root in decimal precision (Newton iterations from the double estimate).</summary>
    internal static decimal Sqrt(decimal v)
    {
        if (v == 0m) return 0m;
        var x = (decimal)Math.Sqrt((double)v);
        for (var i = 0; i < 8; i++)
        {
            if (x == 0m) break;
            var next = (x + v / x) / 2m;
            if (next == x) break;
            x = next;
        }
        // Exact squares come out exact (sqrt(2,25) = 1,5, not 1,4999…).
        var r = decimal.Round(x, 20);
        return r * r == v ? r : x;
    }

    /// <summary>Text for the history-like hint under the display, e.g. "12 +", or "".</summary>
    public string PendingText
    {
        get
        {
            if (_pending is not { } op || HasError) return "";
            var sb = new StringBuilder(Format(_accumulator, _culture)).Append(' ');
            sb.Append(op switch { CalcKey.Add => "+", CalcKey.Subtract => "−", CalcKey.Multiply => "×", _ => "÷" });
            return sb.ToString();
        }
    }
}
