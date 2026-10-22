using System.Windows;
using System.Windows.Controls;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.App.Views;

/// <summary>
/// Constraints tab "New constraint": creates a PRIMARY KEY (name, fields in the chosen order, clustered).
/// UNIQUE / FOREIGN KEY / CHECK are listed but disabled (not implemented yet).
/// </summary>
public sealed class PrimaryKeyWindow : Window
{
    private readonly TableStructure _table;
    private readonly Func<IReadOnlyList<string>, Task> _execute;
    private readonly TextBox _name = new() { Margin = new Thickness(0, 3, 0, 3) };
    private readonly ListBox _available = new() { Height = 170, SelectionMode = SelectionMode.Extended };
    private readonly ListBox _selected = new() { Height = 170 };
    private readonly CheckBox _clustered = new() { Content = "CLUSTERED", Margin = new Thickness(0, 6, 0, 0) };
    private readonly SqlPreviewBox _preview = new() { MinHeight = 70 };
    private readonly TextBlock _error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) }.WithTone(DialogTone.Error);
    private readonly Button _ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) }.WithPrimaryStyle();
    private readonly Button _cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true }.WithDialogStyle();
    private IReadOnlyList<string> _statements = Array.Empty<string>();
    private bool _busy;

    public string ConstraintName => _name.Text.Trim();

    public PrimaryKeyWindow(TableStructure table, Func<IReadOnlyList<string>, Task> execute)
    {
        _table = table;
        _execute = execute;
        Title = "New constraint";
        Width = 600;
        Height = 600;
        MinWidth = 480;
        MinHeight = 460;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid();
        for (var i = 0; i < 4; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new DialogHeaderBar($"Nova constraint em {SqlText.Qualified(table.Schema, table.Name)}", "Icon.Constraint",
            "PRIMARY KEY: nome, campos da chave (em ordem) e CLUSTERED.");

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var type = new ComboBox { Margin = new Thickness(0, 3, 0, 3) };
        type.Items.Add(new ComboBoxItem { Content = "PRIMARY KEY", IsSelected = true });
        foreach (var other in new[] { "UNIQUE", "FOREIGN KEY", "CHECK" })
            type.Items.Add(new ComboBoxItem { Content = other, IsEnabled = false, ToolTip = "Ainda não implementado" });
        AddCell(head, DialogText.Label("Type"), 0, 0);
        AddCell(head, type, 0, 1);
        AddCell(head, DialogText.Label("Name"), 1, 0);
        AddCell(head, _name, 1, 1);
        root.Children.Add(At(head, 1));

        var lists = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lists.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        lists.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddCell(lists, DialogText.Section("Campos da tabela"), 0, 0);
        AddCell(lists, DialogText.Section("Campos da chave (em ordem)"), 0, 2);
        AddCell(lists, _available, 1, 0);
        AddCell(lists, _selected, 1, 2);
        var moves = new StackPanel { Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var add = new Button { Content = ">", Width = 34, Margin = new Thickness(0, 2, 0, 2), ToolTip = "Incluir na chave" }.WithDialogStyle();
        var remove = new Button { Content = "<", Width = 34, Margin = new Thickness(0, 2, 0, 2), ToolTip = "Tirar da chave" }.WithDialogStyle();
        var up = new Button { Content = "▲", Width = 34, Margin = new Thickness(0, 10, 0, 2), ToolTip = "Subir" }.WithDialogStyle();
        var down = new Button { Content = "▼", Width = 34, Margin = new Thickness(0, 2, 0, 2), ToolTip = "Descer" }.WithDialogStyle();
        moves.Children.Add(add); moves.Children.Add(remove); moves.Children.Add(up); moves.Children.Add(down);
        AddCell(lists, moves, 1, 1);
        root.Children.Add(At(lists, 2));
        root.Children.Add(At(_clustered, 3));

        var preview = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var lbl = DialogText.Section("SQL que será executado (em uma transação):");
        DockPanel.SetDock(lbl, Dock.Top);
        preview.Children.Add(lbl);
        preview.Children.Add(_preview);
        root.Children.Add(At(preview, 4));
        root.Children.Add(At(_error, 5));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_ok);
        buttons.Children.Add(_cancel);
        Content = DialogText.Compose(header, root, buttons);

        foreach (var c in table.Columns.OrderBy(c => c.Ordinal)) _available.Items.Add(c.Name);
        _name.Text = PrimaryKeyDdlBuilder.NewPrimaryKeyName(table);
        var canCluster = PrimaryKeyDdlBuilder.CanBeClustered(table);
        _clustered.IsChecked = canCluster;
        _clustered.IsEnabled = canCluster;
        if (!canCluster) _clustered.ToolTip = "A tabela já tem um índice CLUSTERED.";

        add.Click += (_, _) => Move(_available, _selected);
        remove.Click += (_, _) => Move(_selected, _available, restoreOrder: true);
        _available.MouseDoubleClick += (_, _) => Move(_available, _selected);
        _selected.MouseDoubleClick += (_, _) => Move(_selected, _available, restoreOrder: true);
        up.Click += (_, _) => Shift(-1);
        down.Click += (_, _) => Shift(1);
        _name.TextChanged += (_, _) => Rebuild();
        _clustered.Checked += (_, _) => Rebuild();
        _clustered.Unchecked += (_, _) => Rebuild();
        _ok.Click += async (_, _) => await OkAsync();
        Loaded += (_, _) => _available.Focus();
        Rebuild();
    }

    private static UIElement At(UIElement e, int row) { Grid.SetRow(e, row); return e; }

    private static void AddCell(Grid g, UIElement e, int row, int col)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, col);
        g.Children.Add(e);
    }

    private void Move(ListBox from, ListBox to, bool restoreOrder = false)
    {
        var items = from.SelectedItems.Cast<string>().ToList();
        if (items.Count == 0) return;
        foreach (var i in items) { from.Items.Remove(i); to.Items.Add(i); }
        if (restoreOrder)
        {
            var ordered = _table.Columns.OrderBy(c => c.Ordinal).Select(c => c.Name).Where(n => to.Items.Contains(n)).ToList();
            to.Items.Clear();
            foreach (var n in ordered) to.Items.Add(n);
        }
        Rebuild();
    }

    private void Shift(int delta)
    {
        if (_selected.SelectedItem is not string item) return;
        var i = _selected.Items.IndexOf(item);
        var j = i + delta;
        if (j < 0 || j >= _selected.Items.Count) return;
        _selected.Items.RemoveAt(i);
        _selected.Items.Insert(j, item);
        _selected.SelectedIndex = j;
        Rebuild();
    }

    private IReadOnlyList<string> KeyColumns => _selected.Items.Cast<string>().ToList();

    private void Rebuild()
    {
        var cols = KeyColumns;
        var error = PrimaryKeyDdlBuilder.ValidateNewPrimaryKey(_table, cols, ConstraintName);
        _statements = error is null
            ? PrimaryKeyDdlBuilder.BuildCreatePrimaryKey(_table, cols, ConstraintName, _clustered.IsChecked == true)
            : Array.Empty<string>();
        _preview.Text = _statements.Count == 0 ? "-- Escolha os campos da chave primária." : FieldDdlBuilder.ToScript(_statements);
        _error.WithTone(DialogTone.Error);
        _error.Text = error ?? (cols.Any(c => _table.FindColumn(c)?.IsNullable == true)
            ? "Campos que aceitam NULL passam a ser NOT NULL (falha se algum registro tiver NULL)."
            : "");
        if (error is null && _error.Text.Length > 0) _error.WithTone(DialogTone.Info);
        _ok.IsEnabled = !_busy && _statements.Count > 0;
    }

    private async Task OkAsync()
    {
        if (_busy || _statements.Count == 0) return;
        _busy = true;
        _ok.IsEnabled = _cancel.IsEnabled = false;
        _error.WithTone(DialogTone.Info);
        _error.Text = "Executando...";
        try
        {
            await _execute(_statements);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            _error.WithTone(DialogTone.Error);
            _error.Text = "Falhou (nada foi alterado, transação desfeita): " + ex.Message;
        }
        finally
        {
            _busy = false;
            if (IsLoaded && DialogResult != true)
            {
                _cancel.IsEnabled = true;
                _ok.IsEnabled = _statements.Count > 0;
            }
        }
    }
}
