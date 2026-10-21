using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SqlExpert.Core.DataForms;

namespace SqlExpert.App.Views;

/// <summary>
/// Text column of the Data grid. Display mode keeps the column binding (NULL = red "&lt;null&gt;"); in edit mode the
/// text box gets the same binding with its own <see cref="CellEditorValueConverter"/>, so a NULL cell starts EMPTY
/// and stays NULL when left blank. Ctrl+0 sets NULL explicitly (TableDataGrid).
/// Optionally the editor gets a side button at its right edge: "..." (memo window of text columns) or "▾"
/// (calendar of date columns), also opened by <see cref="SideGesture"/>.
/// </summary>
internal class NullAwareTextColumn : DataGridTextColumn
{
    public NullAwareTextColumn(Type dataType, DateColumnKind dateKind = DateColumnKind.None)
    {
        DataType = dataType;
        DateKind = dateKind;
    }

    public Type DataType { get; }

    public DateColumnKind DateKind { get; }

    /// <summary>Text of the side button ("..." or "▾"); null = no button.</summary>
    public string? SideButtonText { get; init; }

    public string? SideButtonToolTip { get; init; }

    /// <summary>Keyboard gesture that also runs <see cref="SideAction"/> (in the editor and on a selected cell).</summary>
    public Func<KeyEventArgs, bool>? SideGesture { get; init; }

    /// <summary>Set by the grid: run the side action for the edited cell (its text box is passed along).</summary>
    public Action<NullAwareTextColumn, DataGridCell, TextBox>? SideAction { get; set; }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        var editor = base.GenerateEditingElement(cell, dataItem);
        if (editor is not TextBox tb) return editor;
        if (Binding is Binding source)
            BindingOperations.SetBinding(tb, TextBox.TextProperty, EditorBinding(source, DataType, DateKind));
        if (SideButtonText is null) return tb;

        tb.PreviewKeyDown += (_, e) =>
        {
            if (SideGesture?.Invoke(e) != true) return;
            SideAction?.Invoke(this, cell, tb);
            e.Handled = true;
        };
        var button = new Button
        {
            Content = SideButtonText,
            Width = 16,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 0, 0),
            Focusable = false,
            IsTabStop = false,
            Cursor = Cursors.Arrow,
            ToolTip = SideButtonToolTip
        };
        button.Click += (_, _) => SideAction?.Invoke(this, cell, tb);

        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(button, Dock.Right);
        panel.Children.Add(button);
        panel.Children.Add(tb);
        return panel;
    }

    /// <summary>The text box inside the editing panel (the base column logic works on it).</summary>
    internal static TextBox? EditorTextBox(FrameworkElement? element) =>
        element as TextBox ?? (element as Panel)?.Children.OfType<TextBox>().FirstOrDefault();

    /// <summary>The editor converter of <paramref name="tb"/> (it knows the value the edit started from).</summary>
    internal static CellEditorValueConverter? EditorConverter(TextBox tb) =>
        BindingOperations.GetBinding(tb, TextBox.TextProperty)?.Converter as CellEditorValueConverter;

    // Commit/Cancel keep the base behavior on the panel: the cell's binding-group helpers look at the editing
    // element whose Parent is the DataGridCell and handle every binding inside it (the text box included).
    protected override object PrepareCellForEdit(FrameworkElement editingElement, RoutedEventArgs editingEventArgs) =>
        base.PrepareCellForEdit(EditorTextBox(editingElement) ?? editingElement, editingEventArgs);

    /// <summary>Copy of the column binding with a per-editor converter (it remembers the value being edited).</summary>
    internal static Binding EditorBinding(Binding source, Type dataType, DateColumnKind dateKind = DateColumnKind.None) => new()
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
        Converter = new CellEditorValueConverter(dataType, dateKind)
    };
}
