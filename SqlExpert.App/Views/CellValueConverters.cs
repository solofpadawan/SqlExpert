using System.Globalization;
using System.Windows.Data;
using SqlExpert.Core.DataForms;

namespace SqlExpert.App.Views;

/// <summary>
/// Grid cell text ⇄ typed DataTable value. NULL is shown as "&lt;null&gt;" (classic style);
/// typing "&lt;null&gt;" (or clearing a non-text cell) stores NULL. Numbers use the Windows (current) culture; dates
/// and times are shown as the SQL Server results grid shows them (<see cref="DateValueText"/>).
/// </summary>
public sealed class CellValueConverter : IValueConverter
{
    public const string NullText = DataValueText.NullText;
    private readonly Type _type;
    private readonly DateColumnKind _dateKind;
    private readonly int _scale;

    /// <param name="dateKind">Date columns: yyyy-MM-dd (+ time) text, see <see cref="DateValueText"/>.</param>
    /// <param name="scale">Fractional-second digits of datetime2 / time / datetimeoffset columns.</param>
    public CellValueConverter(Type type, DateColumnKind dateKind = DateColumnKind.None, int scale = DateValueText.DefaultScale)
    {
        _type = type;
        _dateKind = dateKind;
        _scale = scale;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => IsDateOrTime(value)
            ? DateValueText.Format(value, _dateKind, _scale)
            : DataValueText.FormatCell(value, CultureInfo.CurrentCulture);

    internal static bool IsDateOrTime(object? value) => value is DateTime or DateTimeOffset or TimeSpan;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ParseCellText(value as string ?? value?.ToString() ?? "", _type, _dateKind);

    /// <summary>Typed grid text → value ("&lt;null&gt;" = NULL; date columns use the pt-BR date rules).</summary>
    internal static object ParseCellText(string text, Type type, DateColumnKind dateKind)
    {
        if (dateKind == DateColumnKind.None) return DataValueText.Parse(text, type, CultureInfo.CurrentCulture);
        return text == NullText ? DBNull.Value : DateValueText.Parse(text, dateKind, type);
    }
}

/// <summary>
/// Text box of a Data grid cell in edit mode (one instance per editor, see <see cref="NullAwareTextColumn"/>):
/// NULL starts EMPTY instead of "&lt;null&gt;". Leaving it blank keeps the NULL untouched (no empty string, the row
/// is not modified); typed text is parsed as in <see cref="CellValueConverter"/> (typing "&lt;null&gt;" still means NULL).
/// </summary>
public sealed class CellEditorValueConverter : IValueConverter
{
    private readonly Type _type;
    private readonly DateColumnKind _dateKind;
    private readonly int _scale;
    private object? _original;

    public CellEditorValueConverter(Type type, DateColumnKind dateKind = DateColumnKind.None, int scale = DateValueText.DefaultScale)
    {
        _type = type;
        _dateKind = dateKind;
        _scale = scale;
    }

    /// <summary>Value the editor started from (the date popup keeps its time part).</summary>
    public object? Original => _original;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        _original = value;
        return _dateKind != DateColumnKind.None || CellValueConverter.IsDateOrTime(value)
            ? DateValueText.Format(value, _dateKind, _scale) // the column precision: nothing is lost when the text is committed
            : DataValueText.EditorText(value, CultureInfo.CurrentCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? value?.ToString() ?? "";
        if (DataValueText.KeepsNullWhenBlank(_original, text)) return Binding.DoNothing;
        return CellValueConverter.ParseCellText(text, _type, _dateKind);
    }
}

/// <summary>
/// Form View value text: NULL is shown empty (the form has a "null" checkbox); an empty string stays an empty string
/// for text columns, blank means NULL for the other types.
/// </summary>
public sealed class FormValueConverter : IValueConverter
{
    private readonly Type _type;
    private readonly DateColumnKind _dateKind;
    private readonly int _scale;

    public FormValueConverter(Type type, DateColumnKind dateKind = DateColumnKind.None, int scale = DateValueText.DefaultScale)
    {
        _type = type;
        _dateKind = dateKind;
        _scale = scale;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is byte[]) return DataValueText.BinarySummary(value, CultureInfo.CurrentCulture);
        if (_dateKind != DateColumnKind.None || CellValueConverter.IsDateOrTime(value)) return DateValueText.Format(value, _dateKind, _scale);
        return DataValueText.Format(value, CultureInfo.CurrentCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? value?.ToString() ?? "";
        if (_dateKind != DateColumnKind.None) return DateValueText.Parse(text, _dateKind, _type);
        return DataValueText.Parse(text, _type, CultureInfo.CurrentCulture, nullMarkerAllowed: false);
    }
}

/// <summary>DBNull/null → true (the Form View "null" checkbox).</summary>
public sealed class IsDbNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => DataValueText.IsNull(value);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>bit column ⇄ CheckBox.IsChecked (NULL = indeterminate).</summary>
public sealed class NullableBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? b : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? b : DBNull.Value;
}
