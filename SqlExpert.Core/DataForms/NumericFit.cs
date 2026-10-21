using System.Globalization;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.DataForms;

public enum NumericFitStatus
{
    /// <summary>The value fits the column as it is.</summary>
    Exact,
    /// <summary>Rounded to the column's scale (or to an integer); <see cref="NumericFitResult.Message"/> says so.</summary>
    Rounded,
    /// <summary>Does not fit: nothing is written, the message explains why.</summary>
    Overflow
}

/// <param name="Value">Typed value for the DataTable column (int, decimal, double, …); null on overflow.</param>
public sealed record NumericFitResult(NumericFitStatus Status, object? Value, string Message);

/// <summary>
/// Puts a calculator result into a numeric column: integers are rounded (half away from zero) and range-checked,
/// decimal/numeric are rounded to the declared scale and checked against the declared precision, money and
/// smallmoney keep 4 decimals, float/real take the value as is.
/// </summary>
public static class NumericFit
{
    /// <summary>Column types that get the calculator ("▾").</summary>
    public static bool IsCalculatorType(Type t) => FormFieldLayout.IsNumericType(t);

    public static NumericFitResult Fit(decimal value, Type clrType, DataColumnInfo? info, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(clrType);
        culture ??= CultureInfo.CurrentCulture;
        var typeName = TypeText(clrType, info);

        if (FormFieldLayout.IsIntegerType(clrType))
        {
            var rounded = decimal.Round(value, 0, MidpointRounding.AwayFromZero);
            var (min, max) = IntegerRange(clrType);
            if (rounded < min || rounded > max)
                return Overflow(value, typeName, culture, $"faixa {min.ToString("N0", culture)} a {max.ToString("N0", culture)}");
            // Box each branch on its own: a ?: chain would widen every result to long.
            object typed = clrType == typeof(byte) ? (object)(byte)rounded
                : clrType == typeof(short) ? (object)(short)rounded
                : clrType == typeof(int) ? (object)(int)rounded
                : (object)(long)rounded;
            return rounded == value
                ? new NumericFitResult(NumericFitStatus.Exact, typed, "")
                : new NumericFitResult(NumericFitStatus.Rounded, typed,
                    $"{typeName} é inteiro: {CalculatorEngine.Format(value, culture)} foi arredondado para {CalculatorEngine.Format(rounded, culture)}.");
        }

        if (clrType == typeof(decimal))
        {
            var baseType = BaseType(info);
            int precision, scale;
            decimal? min = null, max = null;
            switch (baseType)
            {
                case "money":
                    precision = 19; scale = 4; min = -922_337_203_685_477.5808m; max = 922_337_203_685_477.5807m;
                    break;
                case "smallmoney":
                    precision = 10; scale = 4; min = -214_748.3648m; max = 214_748.3647m;
                    break;
                default:
                    precision = info is { Precision: > 0 } ? Math.Min(info.Precision, 38) : 38;
                    scale = info is { Scale: >= 0 } ? Math.Min(info.Scale, precision) : 0;
                    if (info is null) scale = 28;
                    break;
            }
            scale = Math.Min(scale, 28);
            var rounded = decimal.Round(value, scale, MidpointRounding.AwayFromZero);
            var intDigits = precision - scale;
            var tooBig = min is not null
                ? rounded < min || rounded > max
                : intDigits < 29 && Math.Abs(decimal.Truncate(rounded)) >= Pow10(intDigits);
            if (tooBig)
                return Overflow(value, typeName, culture,
                    min is not null ? $"faixa {min.Value.ToString("N4", culture)} a {max!.Value.ToString("N4", culture)}"
                                    : $"no máximo {intDigits} dígito(s) antes da vírgula");
            return rounded == value
                ? new NumericFitResult(NumericFitStatus.Exact, rounded, "")
                : new NumericFitResult(NumericFitStatus.Rounded, rounded,
                    $"{typeName} tem {scale} casa(s) decimal(is): {CalculatorEngine.Format(value, culture)} foi arredondado para {CalculatorEngine.Format(rounded, culture)}.");
        }

        if (clrType == typeof(double))
            return new NumericFitResult(NumericFitStatus.Exact, (double)value, "");
        if (clrType == typeof(float))
        {
            var f = (float)value;
            return float.IsInfinity(f)
                ? Overflow(value, typeName, culture, "fora da faixa de real")
                : new NumericFitResult(NumericFitStatus.Exact, f, "");
        }

        return new NumericFitResult(NumericFitStatus.Overflow, null, $"A coluna ({typeName}) não é numérica.");
    }

    private static NumericFitResult Overflow(decimal value, string typeName, CultureInfo culture, string detail) =>
        new(NumericFitStatus.Overflow, null,
            $"O resultado {CalculatorEngine.Format(value, culture)} não cabe em {typeName} ({detail}).");

    private static (decimal Min, decimal Max) IntegerRange(Type t) =>
        t == typeof(byte) ? (byte.MinValue, byte.MaxValue)
        : t == typeof(short) ? (short.MinValue, short.MaxValue)
        : t == typeof(int) ? (int.MinValue, int.MaxValue)
        : (long.MinValue, long.MaxValue);

    private static decimal Pow10(int n)
    {
        var r = 1m;
        for (var i = 0; i < n; i++) r *= 10m;
        return r;
    }

    private static string BaseType(DataColumnInfo? info) =>
        info is null ? "" : (string.IsNullOrEmpty(info.BaseTypeName) ? info.TypeName : info.BaseTypeName).ToLowerInvariant();

    /// <summary>"INT", "NUMERIC(15,2)", "MONEY", "FLOAT" for messages.</summary>
    public static string TypeText(Type clrType, DataColumnInfo? info)
    {
        var b = BaseType(info);
        if (b.Length == 0)
            return clrType == typeof(byte) ? "TINYINT" : clrType == typeof(short) ? "SMALLINT" : clrType == typeof(int) ? "INT"
                : clrType == typeof(long) ? "BIGINT" : clrType == typeof(decimal) ? "DECIMAL" : clrType == typeof(float) ? "REAL" : "FLOAT";
        return b is "decimal" or "numeric" ? $"{b.ToUpperInvariant()}({info!.Precision},{info.Scale})" : b.ToUpperInvariant();
    }
}
