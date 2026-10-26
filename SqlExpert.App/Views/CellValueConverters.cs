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
/// Date/time columns are edited ONLY in the pt-BR mask (<see cref="DateEditMask"/>: dd/MM/yyyy, dd/MM/yyyy HH:mm:ss,
/// HH:mm:ss); after the commit the cell shows the ISO text again.
/// </summary>
public sealed class CellEditorValueConverter : IValueConverter
{
    private readonly Type _type;
    private readonly DateColumnKind _dateKind;
    private readonly int _scale;
    private readonly DateMaskKind _mask;
    private object? _original;

    public CellEditorValueConverter(Type type, DateColumnKind dateKind = DateColumnKind.None, int scale = DateValueText.DefaultScale)
    {
        _type = type;
        _dateKind = dateKind;
        _scale = scale;
        _mask = DateEditMask.KindOf(dateKind, type);
    }

    /// <summary>Value the editor started from (the date popup keeps its time part).</summary>
    public object? Original => _original;

    /// <summary>Input mask of the editor (None = free text).</summary>
    public DateMaskKind MaskKind => _mask;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        _original = value;
        if (_mask != DateMaskKind.None) return DateEditMask.Format(value, _mask);
        return _dateKind != DateColumnKind.None || CellValueConverter.IsDateOrTime(value)
            ? DateValueText.Format(value, _dateKind, _scale)
            : DataValueText.EditorText(value, CultureInfo.CurrentCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? value?.ToString() ?? "";
        if (_mask != DateMaskKind.None)
        {
            // Empty mask: NULL stays untouched, a value is cleared to NULL. Seconds unchanged: fraction/offset kept.
            if (DateEditMask.IsEmpty(text, _mask) && DataValueText.IsNull(_original)) return Binding.DoNothing;
            return DateEditMask.Parse(text, _mask, _dateKind, _type, _original);
        }
        if (DataValueText.KeepsNullWhenBlank(_original, text)) return Binding.DoNothing;
        return CellValueConverter.ParseCellText(text, _type, _dateKind);
    }
}

/// <summary>
/// Form View value text: NULL is shown empty (the form has a "null" checkbox); an empty string stays an empty string
/// for text columns, blank means NULL for the other types. Date/time fields show the ISO text and switch to the pt-BR
/// mask while <see cref="Editing"/> (the field has the keyboard focus).
/// </summary>
public sealed class FormValueConverter : IValueConverter
{
    private readonly Type _type;
    private readonly DateColumnKind _dateKind;
    private readonly int _scale;
    private readonly DateMaskKind _mask;
    private object? _original;

    public FormValueConverter(Type type, DateColumnKind dateKind = DateColumnKind.None, int scale = DateValueText.DefaultScale)
    {
        _type = type;
        _dateKind = dateKind;
        _scale = scale;
        _mask = DateEditMask.KindOf(dateKind, type);
    }

    /// <summary>Input mask of the field (None = free text).</summary>
    public DateMaskKind MaskKind => _mask;

    /// <summary>True while the field is being edited: the text is the mask instead of the ISO text.</summary>
    public bool Editing { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        _original = value;
        if (value is byte[]) return DataValueText.BinarySummary(value, CultureInfo.CurrentCulture);
        if (Editing && _mask != DateMaskKind.None) return DateEditMask.Format(value, _mask);
        if (_dateKind != DateColumnKind.None || CellValueConverter.IsDateOrTime(value))
            return DateValueText.Format(value, _dateKind, _scale);
        return DataValueText.Format(value, CultureInfo.CurrentCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? value?.ToString() ?? "";
        if (_mask != DateMaskKind.None)
        {
            if (!Editing && string.IsNullOrEmpty(text)) return DBNull.Value;
            if (DateEditMask.IsEmpty(text, _mask) && DataValueText.IsNull(_original)) return Binding.DoNothing;
            return DateEditMask.Parse(text, _mask, _dateKind, _type, _original);
        }
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
