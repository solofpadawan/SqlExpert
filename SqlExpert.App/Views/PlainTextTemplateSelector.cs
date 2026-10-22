using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SqlExpert.App.Views;

/// <summary>
/// Shows string content as plain text. Templates whose ContentPresenter has RecognizesAccessKey="True"
/// (the default DataGrid column header, for one) turn the first '_' of a string into an access key and hide it,
/// so FK_ORDER would read FKORDER. Data-driven text (column names) goes through this selector instead;
/// anything that is not a string keeps the normal template choice.
/// </summary>
public sealed class PlainTextTemplateSelector : DataTemplateSelector
{
    public static readonly PlainTextTemplateSelector Instance = new();

    private static readonly DataTemplate PlainText = Build();

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
        item is string ? PlainText : null;

    private static DataTemplate Build()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding { Mode = BindingMode.OneWay });
        var template = new DataTemplate(typeof(string)) { VisualTree = text };
        template.Seal();
        return template;
    }
}