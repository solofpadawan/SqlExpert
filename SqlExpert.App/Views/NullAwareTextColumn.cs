using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SqlExpert.App.Views;

/// <summary>
/// Text column of the Data grid. Display mode keeps the column binding (NULL = red "&lt;null&gt;"); in edit mode the
/// text box gets the same binding with its own <see cref="CellEditorValueConverter"/>, so a NULL cell starts EMPTY
/// and stays NULL when left blank. Ctrl+0 sets NULL explicitly (TableDataGrid).
/// </summary>
internal class NullAwareTextColumn : DataGridTextColumn
{
    public NullAwareTextColumn(Type dataType) => DataType = dataType;

    public Type DataType { get; }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        var editor = base.GenerateEditingElement(cell, dataItem);
        if (editor is TextBox tb && Binding is Binding source)
            BindingOperations.SetBinding(tb, TextBox.TextProperty, EditorBinding(source, DataType));
        return editor;
    }

    /// <summary>Copy of the column binding with a per-editor converter (it remembers the value being edited).</summary>
    internal static Binding EditorBinding(Binding source, Type dataType) => new()
    {
        Path = source.Path,
        Mode = source.Mode,
        UpdateSourceTrigger = source.UpdateSourceTrigger,
        ValidatesOnExceptions = source.ValidatesOnExceptions,
        ValidatesOnDataErrors = source.ValidatesOnDataErrors,
        ValidatesOnNotifyDataErrors = source.ValidatesOnNotifyDataErrors,
        NotifyOnValidationError = source.NotifyOnValidationError,
        NotifyOnSourceUpdated = source.NotifyOnSourceUpdated,
        NotifyOnTargetUpdated = source.NotifyOnTargetUpdated,
        BindingGroupName = source.BindingGroupName,
        ConverterCulture = source.ConverterCulture,
        Converter = new CellEditorValueConverter(dataType)
    };
}
