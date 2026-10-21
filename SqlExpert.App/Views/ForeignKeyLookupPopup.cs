using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SqlExpert.Core.DataForms;
using SqlExpert.Core.Models;

namespace SqlExpert.App.Views;

/// <summary>
/// classic-style foreign-key dropdown ("Data of [schema.table]") opened from an FK cell of the Data grid or an FK
/// field of the Form View: navigator, "N records fetched", read-only grid of the referenced rows (current value
/// preselected), filter box, "Fetch all", "(null)" for nullable FKs. Enter / double-click picks; Esc / X closes.
/// The Popup has no logical parent, so its keys and clicks never reach the edited grid.
/// </summary>
internal sealed class ForeignKeyLookupPopup
{
    /// <summary>Last size the user dragged to (kept for the session).</summary>
    private static Size _lastSize = new(500, 220);

    private readonly Func<bool, bool, CancellationToken, Task<LookupData>> _load;
    private readonly ForeignKeyLookupTarget _target;
    private readonly IReadOnlyList<object?> _currentKey;
    private readonly Action<IReadOnlyList<object?>?> _onPick;
    private readonly Popup _popup;
    private readonly Border _root;
    private readonly DataGrid _grid;
    private readonly TextBox _search;
    private readonly TextBlock _fetched;
    private readonly TextBlock _status;
    private readonly Button _fetchAll;
    private readonly List<Button> _navButtons = new();
    private CancellationTokenSource? _cts;
    private LookupData? _data;
    private List<LookupRowItem> _allItems = new();
    private bool _fetchAllRequested;
    private bool _picked;

    /// <summary>One line of the popup grid. <see cref="Key"/> is null for the "(null)" entry.</summary>
    public sealed class LookupRowItem
    {
        public LookupRowItem(object?[]? key, string[] texts) { Key = key; Texts = texts; }
        public object?[]? Key { get; }
        public string[] Texts { get; }
        public bool IsNullEntry => Key is null;
    }

    public ForeignKeyLookupPopup(
        ForeignKeyLookupTarget target,
        IReadOnlyList<object?> currentKey,
        Func<bool, bool, CancellationToken, Task<LookupData>> load,
        Action<IReadOnlyList<object?>?> onPick)
    {
        _target = target;
        _currentKey = currentKey;
        _load = load;
        _onPick = onPick;

        _search = new TextBox
        {
            Width = 120,
            Margin = new Thickness(6, 1, 2, 1),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Filtrar as linhas (digite parte do código ou da descrição). ↑/↓ escolhem, Enter aplica."
        };
        _search.TextChanged += (_, _) => ApplyFilter(keepKey: SelectedItem?.Key);

        _fetched = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0), FontWeight = FontWeights.SemiBold };
        _status = new TextBlock { Margin = new Thickness(4, 1, 4, 1), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray };

        _grid = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserSortColumns = false,
            CanUserReorderColumns = false,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.All,
            RowHeaderWidth = 12,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            EnableRowVirtualization = true,
            IsTabStop = true
        };
        // Same light gray lines as every grid (Resources/Branding.xaml).
        _grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "GridLineBrush");
        _grid.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty, "GridLineBrush");
        _grid.SetResourceReference(Control.BorderBrushProperty, "GridBorderBrush");
        _grid.MouseDoubleClick += (_, e) =>
        {
            if (FindRow(e.OriginalSource as DependencyObject) is not null) { Pick(); e.Handled = true; }
        };
        _grid.PreviewTextInput += (_, e) =>
        {
            // Typing on the grid goes to the filter box.
            if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
            _search.Focus();
            _search.AppendText(e.Text);
            _search.CaretIndex = _search.Text.Length;
            e.Handled = true;
        };

        // ---- title bar ----
        var close = new Button
        {
            Content = "✕",
            Width = 20,
            Height = 18,
            Padding = new Thickness(0),
            Focusable = false,
            ToolTip = "Fechar sem alterar (Esc)",
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent
        };
        close.Click += (_, _) => Close();
        var title = new TextBlock
        {
            Text = $"Data of [{target.ForeignKey.ReferencedTitle}]",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = $"{target.ForeignKey.Name}: ({string.Join(", ", target.LocalColumns)}) → {target.ForeignKey.ReferencedTitle} ({string.Join(", ", target.ForeignKey.ReferencedColumns)})"
        };
        var titleBar = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0xE4, 0xE8, 0xEE)), Height = 22 };
        DockPanel.SetDock(close, Dock.Right);
        titleBar.Children.Add(close);
        titleBar.Children.Add(title);

        // ---- navigator / tools ----
        var tools = new DockPanel { Margin = new Thickness(2, 2, 2, 2), LastChildFill = false };
        var nav = new StackPanel { Orientation = Orientation.Horizontal };
        nav.Children.Add(NavButton("|◀", "Primeiro (Ctrl+Home)", () => MoveTo(0)));
        nav.Children.Add(NavButton("◀", "Anterior (↑)", () => MoveBy(-1)));
        nav.Children.Add(NavButton("▶", "Próximo (↓)", () => MoveBy(1)));
        nav.Children.Add(NavButton("▶|", "Último (Ctrl+End)", () => MoveTo(int.MaxValue)));
        var refresh = NavButton("↻", "Ler de novo do servidor", () => Reload(refresh: true));
        nav.Children.Add(refresh);
        _fetchAll = new Button
        {
            Content = "Fetch all",
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(6, 0, 6, 0),
            Height = 22,
            Focusable = false,
            ToolTip = "Ler todas as linhas da tabela referenciada (sem TOP)",
            IsEnabled = false
        };
        _fetchAll.Click += (_, _) => { _fetchAllRequested = true; Reload(refresh: false); };
        nav.Children.Add(_fetchAll);
        nav.Children.Add(new TextBlock { Text = "Filtro:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        nav.Children.Add(_search);
        DockPanel.SetDock(nav, Dock.Left);
        DockPanel.SetDock(_fetched, Dock.Right);
        tools.Children.Add(nav);
        tools.Children.Add(_fetched);

        // ---- resize grip + status ----
        var grip = new Thumb
        {
            Width = 14,
            Height = 14,
            Cursor = Cursors.SizeNWSE,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            ToolTip = "Arraste para redimensionar",
            Template = GripTemplate()
        };
        var bottom = new DockPanel();
        DockPanel.SetDock(grip, Dock.Right);
        bottom.Children.Add(grip);
        bottom.Children.Add(_status);

        var layout = new DockPanel();
        DockPanel.SetDock(titleBar, Dock.Top);
        DockPanel.SetDock(tools, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(titleBar);
        layout.Children.Add(tools);
        layout.Children.Add(bottom);
        layout.Children.Add(_grid);

        _root = new Border
        {
            Width = _lastSize.Width,
            Height = _lastSize.Height,
            MinWidth = 320,
            MinHeight = 150,
            Background = SystemColors.WindowBrush,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
            Child = layout
        };
        // Same font size as the Data grid (shared "Font size" setting).
        _root.SetBinding(TextBlock.FontSizeProperty, new Binding(nameof(Services.UiSettings.DataGridFontSizePx))
        {
            Source = Services.UiSettings.Instance,
            Mode = BindingMode.OneWay
        });
        _root.PreviewKeyDown += OnPreviewKeyDown;

        grip.DragDelta += (_, e) =>
        {
            _root.Width = Math.Max(_root.MinWidth, _root.Width + e.HorizontalChange);
            _root.Height = Math.Max(_root.MinHeight, _root.Height + e.VerticalChange);
            _lastSize = new Size(_root.Width, _root.Height);
        };

        _popup = new Popup
        {
            Child = _root,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = false,
            PopupAnimation = PopupAnimation.None,
            Focusable = false
        };
        _popup.Opened += (_, _) => _root.Dispatcher.BeginInvoke(() =>
        {
            _search.Focus();
            Keyboard.Focus(_search);
        }, System.Windows.Threading.DispatcherPriority.Input);
        _popup.Closed += (_, _) =>
        {
            _cts?.Cancel();
            Closed?.Invoke(_picked);
        };
    }

    /// <summary>Raised after the popup closed; the argument is true when a value was picked.</summary>
    public event Action<bool>? Closed;

    public bool IsOpen => _popup.IsOpen;

    /// <summary>Opens under <paramref name="anchor"/> and starts reading the referenced rows.</summary>
    public void Open(UIElement anchor)
    {
        _popup.PlacementTarget = anchor;
        _popup.IsOpen = true;
        Reload(refresh: false);
    }

    public void Close() => _popup.IsOpen = false;

    private static ControlTemplate GripTemplate()
    {
        var t = new ControlTemplate(typeof(Thumb));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, "◢");
        text.SetValue(TextBlock.FontSizeProperty, 10.0);
        text.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);
        text.SetValue(TextBlock.BackgroundProperty, Brushes.Transparent);
        text.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
        t.VisualTree = text;
        return t;
    }

    private Button NavButton(string text, string tip, Action action)
    {
        var b = new Button
        {
            Content = text,
            MinWidth = 24,
            Height = 22,
            Padding = new Thickness(3, 0, 3, 0),
            Margin = new Thickness(1, 0, 1, 0),
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Focusable = false,
            ToolTip = tip
        };
        b.Click += (_, _) => action();
        _navButtons.Add(b);
        return b;
    }

    private LookupRowItem? SelectedItem => _grid.SelectedItem as LookupRowItem;

    private static DataGridRow? FindRow(DependencyObject? d)
    {
        while (d is not null and not DataGridRow)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as DataGridRow;
    }

    private async void Reload(bool refresh)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        SetStatus("Lendo os registros de " + _target.ForeignKey.ReferencedTitle + "...", error: false);
        _fetchAll.IsEnabled = false;
        try
        {
            var data = await _load(_fetchAllRequested, refresh, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested || !_popup.IsOpen) return;
            _data = data;
            BuildColumns(data);
            _allItems = BuildItems(data);
            ApplyFilter(keepKey: null, preselectCurrent: true);
            _fetchAll.IsEnabled = !data.IsComplete;
            SetStatus(data.CurrentRowAppended
                ? "O valor atual estava fora dos primeiros registros e foi incluído no fim da lista."
                : data.IsComplete ? "" : $"Mostrando os primeiros {data.Top} registros (ordem da chave). Use Fetch all para ler todos.", error: false);
        }
        catch (OperationCanceledException) { /* closed or reloaded */ }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested) return;
            SetStatus("Erro ao ler " + _target.ForeignKey.ReferencedTitle + ": " + ex.Message, error: true);
            _fetched.Text = "";
        }
    }

    private void SetStatus(string text, bool error)
    {
        _status.Text = text;
        _status.Foreground = error ? Brushes.Firebrick : Brushes.DimGray;
        _status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BuildColumns(LookupData data)
    {
        _grid.Columns.Clear();
        for (var i = 0; i < data.Columns.Count; i++)
        {
            var c = data.Columns[i];
            var numeric = IsNumeric(c);
            // "<null>" in red like the Data grid (NullAwareCellText / NullValueBrush, Resources/Branding.xaml).
            var style = new Style(typeof(TextBlock), _grid.TryFindResource("NullAwareCellText") as Style);
            style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(2, 0, 2, 0)));
            if (numeric) style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = c.Name,
                Binding = new Binding($"Texts[{i}]") { Mode = BindingMode.OneWay },
                ElementStyle = style,
                MinWidth = 40,
                MaxWidth = 360,
                FontWeight = i < data.KeyCount ? FontWeights.SemiBold : FontWeights.Normal
            });
        }
    }

    private static bool IsNumeric(DataColumnInfo c) =>
        (string.IsNullOrEmpty(c.BaseTypeName) ? c.TypeName : c.BaseTypeName).ToLowerInvariant()
            is "int" or "bigint" or "smallint" or "tinyint" or "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real";

    private List<LookupRowItem> BuildItems(LookupData data)
    {
        var culture = CultureInfo.CurrentCulture;
        var items = new List<LookupRowItem>(data.Rows.Count + 1);
        if (_target.AllowsNull)
        {
            var texts = new string[data.Columns.Count];
            Array.Fill(texts, "");
            texts[0] = ForeignKeyLookup.NullEntryText;
            items.Add(new LookupRowItem(null, texts));
        }
        foreach (var row in data.Rows)
        {
            var texts = new string[data.Columns.Count];
            for (var i = 0; i < texts.Length && i < row.Length; i++)
                texts[i] = DataValueText.OneLine(DataValueText.FormatCell(row[i], culture));
            items.Add(new LookupRowItem(row.Take(data.KeyCount).ToArray(), texts));
        }
        return items;
    }

    private void ApplyFilter(IReadOnlyList<object?>? keepKey, bool preselectCurrent = false)
    {
        if (_data is null) return;
        var term = _search.Text;
        var dataItems = _allItems.Where(i => !i.IsNullEntry).ToList();
        var matches = ForeignKeyLookup.FilterRows(dataItems.Select(i => (IReadOnlyList<string>)i.Texts).ToList(), term);
        var shown = new List<LookupRowItem>();
        var nullEntry = _allItems.FirstOrDefault(i => i.IsNullEntry);
        if (nullEntry is not null && string.IsNullOrWhiteSpace(term)) shown.Add(nullEntry);
        shown.AddRange(matches.Select(m => dataItems[m]));
        _grid.ItemsSource = shown;
        _fetched.Text = ForeignKeyLookup.FetchedText(_data, matches.Count);

        // Selection: the current value (on open), else the row that was selected, else the first match.
        var key = preselectCurrent ? _currentKey : keepKey;
        var index = -1;
        if (key is not null)
        {
            if (key.Count > 0 && key.All(DataValueText.IsNull))
                index = nullEntry is not null ? shown.IndexOf(nullEntry) : -1;
            else
            {
                var keys = shown.Select(i => (object[])(i.Key ?? Array.Empty<object>())).ToList();
                index = ForeignKeyLookup.FindRowIndex(keys, key);
            }
        }
        if (index < 0 && shown.Count > 0) index = 0;
        Select(index);
    }

    private void Select(int index)
    {
        if (_grid.Items.Count == 0 || index < 0) { _grid.SelectedIndex = -1; return; }
        index = Math.Clamp(index, 0, _grid.Items.Count - 1);
        _grid.SelectedIndex = index;
        var item = _grid.Items[index];
        _grid.Dispatcher.BeginInvoke(() => _grid.ScrollIntoView(item), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void MoveTo(int index) => Select(index);

    private void MoveBy(int delta) => Select(Math.Max(0, _grid.SelectedIndex) + delta);

    private int PageSize()
    {
        var rowHeight = _grid.RowHeight > 0 && !double.IsNaN(_grid.RowHeight) ? _grid.RowHeight : (double)_root.GetValue(TextBlock.FontSizeProperty) * 1.6;
        return Math.Max(1, (int)(Math.Max(60, _grid.ActualHeight - 24) / rowHeight) - 1);
    }

    private void Pick()
    {
        if (SelectedItem is not { } item) return;
        _picked = true;
        Close();
        _onPick(item.Key);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var inSearch = _search.IsKeyboardFocusWithin;
        switch (key)
        {
            case Key.Escape:
            case Key.F4:
            case Key.Up when alt:
                Close();
                break;
            case Key.Enter:
                Pick();
                break;
            case Key.Tab:
                if (inSearch) _grid.Focus(); else _search.Focus();
                break;
            case Key.Up when inSearch:
                MoveBy(-1);
                break;
            case Key.Down when inSearch:
                MoveBy(1);
                break;
            case Key.PageUp when inSearch:
                MoveBy(-PageSize());
                break;
            case Key.PageDown when inSearch:
                MoveBy(PageSize());
                break;
            case Key.Home when ctrl:
                MoveTo(0);
                break;
            case Key.End when ctrl:
                MoveTo(int.MaxValue);
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
