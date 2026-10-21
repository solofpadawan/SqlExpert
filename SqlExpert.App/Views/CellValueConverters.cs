using System.Globalization;
using System.Windows.Data;
using SqlExpert.Core.DataForms;

namespace SqlExpert.App.Views;

/// <summary>
/// Grid cell text ⇄ typed DataTable value. NULL is shown as "&lt;null&gt;" (classic style);
/// typing "&lt;null&gt;" (or clearing a non-text cell) stores NULL. Uses the Windows (current) culture.
/// </summary>
public sealed class CellValueConverter : IValueConverter
{
    public const string NullText = DataValueText.NullText;
    private readonly Type _type;
    private readonly DateColumnKind _dateKind;

    /// <param name="dateKind">Date columns: pt-BR dd/MM/yyyy (+ time) text, see <see cref="DateValueText"/>.</param>
    public CellValueConverter(Type type, DateColumnKind dateKind = DateColumnKind.None)
    {
        _type = type;
        _dateKind = dateKind;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => _dateKind != DateColumnKind.None && !DataValueText.IsNull(value)
            ? DateValueText.Format(value, _dateKind)
            : DataValueText.FormatCell(value, CultureInfo.CurrentCulture);

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
    private object? _original;

    public CellEditorValueConverter(Type type, DateColumnKind dateKind = DateColumnKind.None)
    {
        _type = type;
        _dateKind = dateKind;
    }

    /// <summary>Value the editor started from (the date popup keeps its time part).</summary>
    public object? Original => _original;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        _original = value;
        return _dateKind != DateColumnKind.None
            ? DateValueText.Format(value, _dateKind) // full precision: nothing is lost when the text is committed
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

    public FormValueConverter(Type type, DateColumnKind dateKind = DateColumnKind.None)
    {
        _type = type;
        _dateKind = dateKind;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is byte[]) return DataValueText.BinarySummary(value, CultureInfo.CurrentCulture);
        if (_dateKind != DateColumnKind.None) return DateValueText.Format(value, _dateKind);
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
