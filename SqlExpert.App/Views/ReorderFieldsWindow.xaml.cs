using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.App.Views;

/// <summary>
/// Lets the user reorder the columns and shows the table-rebuild script.
/// SQL Server cannot reorder columns in place, so this only GENERATES the script; it never runs it.
/// </summary>
public partial class ReorderFieldsWindow : Window
{
    private readonly TableStructure _table;
    private readonly List<string> _original;
    private bool _ready;

    public ReorderFieldsWindow(TableStructure table, string? selectedField = null)
    {
        InitializeComponent();
        _table = table;
        _original = table.Columns.OrderBy(c => c.Ordinal).Select(c => c.Name).ToList();
        foreach (var n in _original) FieldsList.Items.Add(n);
        HeaderText.Title = $"Reorder fields de {SqlText.Qualified(table.Schema, table.Name)}";
        HeaderText.Subtitle = "O SQL Server não muda a ordem das colunas no lugar. " +
                              "Ordene os campos e copie o script, que recria a tabela com os dados. Revise e execute você mesmo (com backup).";
        FieldsList.SelectedItem = selectedField is not null && _original.Contains(selectedField) ? selectedField : _original.FirstOrDefault();
        PreviewKeyDown += OnPreviewKeyDown;
        _ready = true;
        Refresh();
    }

    public IReadOnlyList<string> CurrentOrder => FieldsList.Items.Cast<string>().ToList();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Up) { Move(-1); e.Handled = true; }
        else if (key == Key.Down) { Move(1); e.Handled = true; }
    }

    private void Move(int delta)
    {
        var i = FieldsList.SelectedIndex;
        if (i < 0) return;
        var j = Math.Clamp(i + delta, 0, FieldsList.Items.Count - 1);
        if (i == j) return;
        var item = FieldsList.Items[i];
        FieldsList.Items.RemoveAt(i);
        FieldsList.Items.Insert(j, item);
        FieldsList.SelectedIndex = j;
        FieldsList.ScrollIntoView(item);
        Refresh();
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);
    private void Top_Click(object sender, RoutedEventArgs e) => Move(-FieldsList.Items.Count);
    private void Bottom_Click(object sender, RoutedEventArgs e) => Move(FieldsList.Items.Count);

    private void FieldsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var i = FieldsList.SelectedIndex;
        var n = FieldsList.Items.Count;
        TopButton.IsEnabled = UpButton.IsEnabled = i > 0;
        DownButton.IsEnabled = BottomButton.IsEnabled = i >= 0 && i < n - 1;
    }

    private void Refresh()
    {
        UpdateButtons();
        var order = CurrentOrder;
        var changed = !order.SequenceEqual(_original);
        ScriptBox.Text = changed
            ? TableRebuildScriptBuilder.Build(_table, order)
            : "-- Use Up/Down para mudar a ordem dos campos; o script aparece aqui.";
        CopyButton.IsEnabled = changed;
        StatusLine.Text = changed ? "Ordem alterada — script gerado (não executado)." : "Ordem atual.";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(ScriptBox.Text);
            StatusLine.Text = "Script copiado para a área de transferência.";
        }
        catch (Exception ex)
        {
            App.ReportError(ex);
        }
    }

}
