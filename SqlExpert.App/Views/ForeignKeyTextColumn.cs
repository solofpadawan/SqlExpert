using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SqlExpert.Core.Models;

namespace SqlExpert.App.Views;

/// <summary>
/// Text column of a foreign-key field in the Data grid: in edit mode the cell shows the text box plus a "▾" button
/// (lookup); the button, Alt+↓ or F4 open the "Data of [table]" popup. Typing a value by hand still works.
/// </summary>
internal sealed class ForeignKeyTextColumn : DataGridTextColumn
{
    public ForeignKeyTextColumn(ForeignKeyLookupTarget target) => Target = target;

    public ForeignKeyLookupTarget Target { get; }

    /// <summary>Set by the grid: open the lookup for the edited cell.</summary>
    public Action<ForeignKeyTextColumn, DataGridCell>? OpenLookup { get; set; }

    public static bool IsLookupGesture(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return (key == Key.F4 && Keyboard.Modifiers == ModifierKeys.None)
               || (key == Key.Down && (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
    }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        var editor = base.GenerateEditingElement(cell, dataItem);
        if (editor is not TextBox tb) return editor;

        tb.PreviewKeyDown += (_, e) =>
        {
            if (!IsLookupGesture(e)) return;
            OpenLookup?.Invoke(this, cell);
            e.Handled = true;
        };
        var button = new Button
        {
            Content = "▾",
            Width = 16,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 0, 0),
            Focusable = false,
            IsTabStop = false,
            Cursor = Cursors.Arrow,
            ToolTip = $"Escolher em {Target.ForeignKey.ReferencedTitle} (Alt+↓ / F4)"
        };
        button.Click += (_, _) => OpenLookup?.Invoke(this, cell);

        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(button, Dock.Right);
        panel.Children.Add(button);
        panel.Children.Add(tb);
        return panel;
    }

    /// <summary>The text box inside the editing panel (the base column logic works on it).</summary>
    internal static TextBox? EditorTextBox(FrameworkElement? element) =>
        element as TextBox ?? (element as Panel)?.Children.OfType<TextBox>().FirstOrDefault();

    // Commit/Cancel keep the base behavior on the panel: the cell's binding-group helpers look at the editing
    // element whose Parent is the DataGridCell and handle every binding inside it (the text box included).
    protected override object PrepareCellForEdit(FrameworkElement editingElement, RoutedEventArgs editingEventArgs) =>
        base.PrepareCellForEdit(EditorTextBox(editingElement) ?? editingElement, editingEventArgs);
}
