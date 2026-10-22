using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SqlExpert.App.Commands;
using SqlExpert.App.ViewModels;
using SqlExpert.Core.DataForms;

namespace SqlExpert.App.Views;

/// <summary>
/// Where the memo window reads and writes one column: the current record of a Data tab (editable, with the
/// navigator) or a SQL Editor result grid (read-only viewer).
/// </summary>
internal abstract class MemoSource
{
    public abstract string ColumnName { get; }
    public abstract bool IsReadOnly { get; }
    /// <summary>Character limit of sized columns (0 = none).</summary>
    public virtual int MaxLength => 0;
    /// <summary>The record shown now (null when there is none).</summary>
    public abstract object? CurrentRow { get; }
    public abstract object? ValueOf(object row);
    /// <summary>Writes the edited text into <paramref name="row"/> (pending until Post/Cancel).</summary>
    public virtual void Apply(object row, string text) { }
    public virtual ICommand? First => null;
    public virtual ICommand? Prior => null;
    public virtual ICommand? Next => null;
    public virtual ICommand? Last => null;
    public virtual ICommand? Delete => null;
    public virtual ICommand? Post => null;
    public virtual ICommand? Cancel => null;
    public virtual bool HasPendingChanges => false;

    /// <summary>The current record changed; true = the data was reloaded (typed text is discarded).</summary>
    public event Action<bool>? CurrentChanged;
    protected void RaiseCurrentChanged(bool discardPending) => CurrentChanged?.Invoke(discardPending);
    public virtual void Detach() { }
}

/// <summary>Current record of a Data tab (grid or Form View): edits go through the normal Post/Cancel.</summary>
internal sealed class TableMemoSource : MemoSource
{
    private readonly TableDataViewModel _vm;
    private readonly GridColumnSpec _spec;
    private readonly Action _beforeCommand;

    public TableMemoSource(TableDataViewModel vm, GridColumnSpec spec, Action beforeCommand)
    {
        _vm = vm;
        _spec = spec;
        _beforeCommand = beforeCommand;
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.DataReloaded += OnReloaded;
    }

    public string Field => _spec.Field;
    public override string ColumnName => _spec.Header;
    public override bool IsReadOnly => _vm.IsReadOnly || _spec.IsReadOnly;
    public override int MaxLength => MemoEditRules.MaxChars(_spec.Info);
    public override object? CurrentRow => _vm.CurrentRowView;
    public override object? ValueOf(object row) => row is DataRowView rv && rv.Row.RowState != DataRowState.Detached ? rv[_spec.Field] : null;
    public override void Apply(object row, string text)
    {
        if (row is DataRowView rv) _vm.SetCellValue(rv, _spec, text);
    }

    private ICommand Wrap(ICommand inner) => new RelayCommand(_ => { _beforeCommand(); inner.Execute(null); }, _ => inner.CanExecute(null));
    public override ICommand First => Wrap(_vm.FirstCommand);
    public override ICommand Prior => Wrap(_vm.PriorCommand);
    public override ICommand Next => Wrap(_vm.NextCommand);
    public override ICommand Last => Wrap(_vm.LastCommand);
    public override ICommand? Delete => IsReadOnly ? null : _vm.DeleteCommand;
    public override ICommand? Post => IsReadOnly ? null : Wrap(_vm.PostCommand);
    public override ICommand? Cancel => IsReadOnly ? null : _vm.CancelCommand;
    public override bool HasPendingChanges => _vm.HasPendingChanges;

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TableDataViewModel.CurrentRowView)) RaiseCurrentChanged(false);
    }

    private void OnReloaded() => RaiseCurrentChanged(true);

    public override void Detach()
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.DataReloaded -= OnReloaded;
    }
}

/// <summary>Read-only viewer of one column of a SQL Editor result grid; the navigator moves the grid's current row.</summary>
internal sealed class ResultGridMemoSource : MemoSource
{
    private readonly DataGrid _grid;
    private readonly DataGridColumn _column;
    private readonly string _field;
    private readonly string _name;

    public ResultGridMemoSource(DataGrid grid, DataGridColumn column, string field, string name)
    {
        _grid = grid;
        _column = column;
        _field = field;
        _name = name;
        First = new RelayCommand(_ => MoveTo(0), _ => Index > 0);
        Prior = new RelayCommand(_ => MoveTo(Index - 1), _ => Index > 0);
        Next = new RelayCommand(_ => MoveTo(Index + 1), _ => Index >= 0 && Index < _grid.Items.Count - 1);
        Last = new RelayCommand(_ => MoveTo(_grid.Items.Count - 1), _ => Index >= 0 && Index < _grid.Items.Count - 1);
        _grid.CurrentCellChanged += OnCurrentCellChanged;
    }

    public override string ColumnName => _name;
    public override bool IsReadOnly => true;
    public override object? CurrentRow => _grid.CurrentItem as DataRowView;
    public override object? ValueOf(object row) => row is DataRowView rv ? rv[_field] : null;
    public override ICommand First { get; }
    public override ICommand Prior { get; }
    public override ICommand Next { get; }
    public override ICommand Last { get; }

    private int Index => _grid.CurrentItem is { } item ? _grid.Items.IndexOf(item) : -1;

    private void MoveTo(int index)
    {
        if (index < 0 || index >= _grid.Items.Count) return;
        var item = _grid.Items[index];
        _grid.SelectedCells.Clear();
        var info = new DataGridCellInfo(item, _column);
        _grid.CurrentCell = info;
        _grid.SelectedCells.Add(info);
        _grid.ScrollIntoView(item, _column);
    }

    private void OnCurrentCellChanged(object? sender, EventArgs e) => RaiseCurrentChanged(false);

    public override void Detach() => _grid.CurrentCellChanged -= OnCurrentCellChanged;
}

/// <summary>
/// "..." memo window of a text column (classic style): navigator, "Plain text editor" with a live "N chars"
/// counter, [Copy to clipboard] and [Close]. Owned, not modal: the grid stays usable and moving to another record
/// shows that record's value. A NULL value opens empty and stays NULL unless edited; edits are written to the record
/// (pending until Post/Cancel) when leaving the window, moving, posting or closing. No separate "Unicode editor"
/// pane: .NET strings are already Unicode, so the plain editor shows and edits every character.
/// </summary>
internal sealed class MemoEditorWindow : Window
{
    private readonly MemoSource _source;
    private readonly TextBox _text;
    private readonly TextBlock _counter;
    private object? _row;
    private object? _original;
    private bool _edited;
    private bool _loading;

    private MemoEditorWindow(MemoSource source)
    {
        _source = source;
        Title = source.ColumnName;
        Width = 640;
        Height = 420;
        MinWidth = 320;
        MinHeight = 200;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        _text = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            IsReadOnly = source.IsReadOnly,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2),
            MaxLength = Math.Max(0, source.MaxLength)
        };
        _counter = new TextBlock { FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        _text.TextChanged += (_, _) =>
        {
            if (!_loading) _edited = true;
            _counter.Text = MemoEditRules.CharCounter(_text.Text);
        };

        // Navigator: First / Prior / Next / Last, Delete, Post / Cancel (enabled from the record state); flat vector icons.
        var bar = new ToolBar();
        bar.SetResourceReference(StyleProperty, "InnerTrayToolBar");
        ToolBarTray.SetIsLocked(bar, true);
        bar.Items.Add(NavButton("Icon.First", "First", source.First, apply: true));
        bar.Items.Add(NavButton("Icon.Prior", "Prior", source.Prior, apply: true));
        bar.Items.Add(NavButton("Icon.Next", "Next", source.Next, apply: true));
        bar.Items.Add(NavButton("Icon.Last", "Last", source.Last, apply: true));
        bar.Items.Add(new Separator());
        bar.Items.Add(NavButton("Icon.DeleteRecord", "Delete record", source.Delete, apply: false));
        bar.Items.Add(NavButton("Icon.Post", "Post (gravar alterações pendentes)", source.Post, apply: true, needsChanges: true));
        bar.Items.Add(NavButton("Icon.CancelEdit", "Cancel (descartar alterações pendentes)", source.Cancel, apply: false, needsChanges: true));
        bar.Items.Add(new Separator());
        var wrap = new CheckBox { Content = "Wrap", IsChecked = true, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Quebrar linhas" };
        wrap.Click += (_, _) => _text.TextWrapping = wrap.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
        bar.Items.Add(wrap);
        var mono = new CheckBox { Content = "Monospace", Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Fonte de largura fixa (Consolas)" };
        var proportional = _text.FontFamily;
        mono.Click += (_, _) => _text.FontFamily = mono.IsChecked == true ? new FontFamily("Consolas") : proportional;
        bar.Items.Add(mono);
        var tray = new ToolBarTray { IsLocked = true, Background = Brushes.Transparent };
        tray.ToolBars.Add(bar);
        var band = new Border { Child = tray };
        band.SetResourceReference(StyleProperty, "InnerToolBarBand");

        // "Plain text editor" header bar with the live "N chars" counter at the right.
        var headerRow = new DockPanel();
        DockPanel.SetDock(_counter, Dock.Right);
        _counter.Margin = new Thickness(4, 0, 2, 0);
        _counter.WithTone(DialogTone.Muted);
        headerRow.Children.Add(_counter);
        headerRow.Children.Add(new TextBlock
        {
            Text = source.IsReadOnly ? "Plain text editor (somente leitura)" : "Plain text editor",
            FontWeight = FontWeights.SemiBold
        }.WithTone(DialogTone.Normal));
        var header = new Border { Child = headerRow, BorderThickness = new Thickness(0, 1, 0, 1), Padding = new Thickness(8, 4, 8, 4) };
        header.SetResourceReference(Border.BackgroundProperty, "DialogHeader.Background");
        header.SetResourceReference(Border.BorderBrushProperty, "DialogHeader.Border");

        var copy = new Button { Content = "Copy to clipboard", MinWidth = 130, Padding = new Thickness(8, 2, 8, 2) }.WithDialogStyle();
        copy.Click += (_, _) => CopyToClipboard();
        var close = new Button { Content = "Close", MinWidth = 80, Padding = new Thickness(8, 2, 8, 2), IsCancel = true }.WithDialogStyle();
        close.Click += (_, _) => Close();
        var bottomRow = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        bottomRow.Children.Add(close);
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        bottomRow.Children.Add(copy);
        var bottom = new Border { Child = bottomRow };
        bottom.SetResourceReference(StyleProperty, "DialogButtonBar");

        var root = new DockPanel();
        DockPanel.SetDock(band, Dock.Top);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(band);
        root.Children.Add(header);
        root.Children.Add(bottom);
        root.Children.Add(_text);
        Content = root;

        source.CurrentChanged += OnCurrentChanged;
        Deactivated += (_, _) => ApplyPending();
        Closing += (_, _) => ApplyPending();
        Closed += (_, _) =>
        {
            source.CurrentChanged -= OnCurrentChanged;
            source.Detach();
        };
        Loaded += (_, _) => { _text.Focus(); _text.CaretIndex = 0; };
        Load();
    }

    /// <summary>Column the window edits (Data tab), so a second "..." on the same column reuses it.</summary>
    public MemoSource Source => _source;

    /// <summary>Opens the window owned by <paramref name="owner"/> (not modal).</summary>
    public static MemoEditorWindow Open(Window? owner, MemoSource source)
    {
        var w = new MemoEditorWindow(source);
        if (owner is not null) w.Owner = owner;
        w.Show();
        return w;
    }

    private Button NavButton(string iconKey, string tip, ICommand? command, bool apply, bool needsChanges = false)
    {
        var button = new Button
        {
            Content = new IconImage { Icon = TryFindResource(iconKey) as ImageSource },
            ToolTip = tip,
            Focusable = false
        };
        button.SetResourceReference(StyleProperty, "FlatIconButton");
        ToolTipService.SetShowOnDisabled(button, true);
        if (command is null)
        {
            button.IsEnabled = false;
            return button;
        }
        button.Command = new RelayCommand(_ =>
        {
            if (apply) ApplyPending();
            else _edited = false; // Delete / Cancel drop the text typed here
            command.Execute(null);
            Load();
        }, _ => command.CanExecute(null) && (!needsChanges || _edited || _source.HasPendingChanges));
        return button;
    }

    private void OnCurrentChanged(bool discardPending)
    {
        if (!discardPending) ApplyPending();
        Load();
    }

    /// <summary>Shows the value of the current record (NULL → empty).</summary>
    private void Load()
    {
        _loading = true;
        try
        {
            _row = _source.CurrentRow;
            _original = _row is null ? null : _source.ValueOf(_row);
            _text.Text = MemoEditRules.EditorText(_original);
            _text.IsEnabled = _row is not null;
            _edited = false;
            _counter.Text = MemoEditRules.CharCounter(_text.Text);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Writes the edited text to the record it was loaded from (nothing when unchanged or NULL left blank).</summary>
    private void ApplyPending()
    {
        if (_row is null || _source.IsReadOnly || !_edited) return;
        try
        {
            if (MemoEditRules.TryResolve(_original, _text.Text, _edited, out var value))
            {
                _source.Apply(_row, (string)value);
                _original = value;
            }
            _edited = false;
        }
        catch (Exception ex)
        {
            App.ReportError(ex);
        }
    }

    private void CopyToClipboard()
    {
        try
        {
            Clipboard.SetText(_text.SelectionLength > 0 ? _text.SelectedText : _text.Text);
        }
        catch (Exception ex)
        {
            App.ReportError(ex);
        }
    }
}
