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

    public CellValueConverter(Type type) => _type = type;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => DataValueText.FormatCell(value, CultureInfo.CurrentCulture);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => DataValueText.Parse(value as string ?? value?.ToString() ?? "", _type, CultureInfo.CurrentCulture);
}

/// <summary>
/// Form View value text: NULL is shown empty (the form has a "null" checkbox); an empty string stays an empty string
/// for text columns, blank means NULL for the other types.
/// </summary>
public sealed class FormValueConverter : IValueConverter
{
    private readonly Type _type;

    public FormValueConverter(Type type) => _type = type;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is byte[] ? DataValueText.BinarySummary(value, CultureInfo.CurrentCulture) : DataValueText.Format(value, CultureInfo.CurrentCulture);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => DataValueText.Parse(value as string ?? value?.ToString() ?? "", _type, CultureInfo.CurrentCulture, nullMarkerAllowed: false);
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
