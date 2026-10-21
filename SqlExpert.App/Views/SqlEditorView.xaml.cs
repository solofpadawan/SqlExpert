using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Search;
using SqlExpert.App.Services;
using SqlExpert.App.ViewModels;
using SqlExpert.Core.DataForms;
using SqlExpert.Core.SqlEditor;

namespace SqlExpert.App.Views;

/// <summary>SQL Editor view: AvalonEdit + result tabs (built per execution) + Messages + History.</summary>
public partial class SqlEditorView : UserControl, ISqlEditorHost
{
    private SqlEditorDocumentViewModel? _vm;
    private readonly SearchPanel? _search;
    private readonly ErrorLineRenderer _errorLine = new();
    private readonly ListBox _messages;
    private ReplaceWindow? _replace;

    public SqlEditorView()
    {
        InitializeComponent();

        var highlighting = TSqlHighlighting.TryGet();
        if (highlighting is not null) Editor.SyntaxHighlighting = highlighting;
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.ConvertTabsToSpaces = false;
        Editor.Options.IndentationSize = 4;
        Editor.TextArea.TextView.BackgroundRenderers.Add(_errorLine);
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaret();
        Editor.TextChanged += (_, _) => ClearErrorLine();
        try { _search = SearchPanel.Install(Editor); }
        catch (Exception ex) { App.ReportError(ex); }

        _messages = new ListBox
        {
            ItemTemplate = (DataTemplate)FindResource("SqlMessageTemplate"),
            BorderThickness = new Thickness(0),
            SelectionMode = SelectionMode.Extended,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_messages, ScrollBarVisibility.Disabled);
        _messages.MouseDoubleClick += Messages_OnMouseDoubleClick;
        _messages.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) => CopyMessages(), (_, e) => e.CanExecute = _messages.SelectedItems.Count > 0));

        ShowFontSize();
        UiSettings.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UiSettings.SqlEditorFontSize) && !FontSizeBox.IsKeyboardFocusWithin) ShowFontSize();
        };
        DataContextChanged += OnDataContextChanged;
        // Activating the document (Windows bar, F12, Ctrl+Tab) puts the caret in the editor.
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) FocusEditor(); };
        RebuildResultTabs();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.ResultsChanged -= RebuildResultTabs;
            if (ReferenceEquals(_vm.Host, this)) _vm.Host = null;
        }
        _vm = DataContext as SqlEditorDocumentViewModel;
        _messages.ItemsSource = _vm?.Messages;
        if (_vm is not null)
        {
            _vm.Host = this;
            _vm.ResultsChanged += RebuildResultTabs;
        }
        RebuildResultTabs();
        UpdateCaret();
    }

    // ===== Keyboard =====

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if ((key == Key.F9 && mods == ModifierKeys.None) || (key == Key.Enter && mods == ModifierKeys.Control))
        {
            if (FetchBox.IsKeyboardFocusWithin) FetchBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            _vm.Execute();
            e.Handled = true;
        }
        else if (key == Key.Cancel || (key == Key.Pause && mods == ModifierKeys.Control))
        {
            _vm.Cancel();
            e.Handled = true;
        }
        else if (key == Key.O && mods == ModifierKeys.Control)
        {
            if (_vm.OpenCommand.CanExecute(null)) _vm.OpenCommand.Execute(null);
            e.Handled = true;
        }
        else if (key == Key.S && mods == ModifierKeys.Control)
        {
            _vm.Save(saveAs: false);
            e.Handled = true;
        }
        else if ((key == Key.R || key == Key.H) && mods == ModifierKeys.Control)
        {
            ShowReplace();
            e.Handled = true;
        }
    }

    private void Editor_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        UiSettings.Instance.SqlEditorFontSize += e.Delta > 0 ? 1 : -1;
        ShowFontSize();
        e.Handled = true;
    }

    private void FetchBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        FetchBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        FocusEditor();
        e.Handled = true;
    }

    // ===== Font size =====

    private void ShowFontSize() => FontSizeBox.Text = UiSettings.Instance.SqlEditorFontSize.ToString();

    private void ApplyTypedFontSize()
    {
        if (int.TryParse(FontSizeBox.Text.Trim(), out var v)) UiSettings.Instance.SqlEditorFontSize = v;
        ShowFontSize();
    }

    private void FontSizeBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ApplyTypedFontSize(); FontSizeBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Up) { UiSettings.Instance.SqlEditorFontSize++; ShowFontSize(); e.Handled = true; }
        else if (e.Key == Key.Down) { UiSettings.Instance.SqlEditorFontSize--; ShowFontSize(); e.Handled = true; }
    }

    private void FontSizeBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ApplyTypedFontSize();
    private void FontUp_OnClick(object sender, RoutedEventArgs e) { UiSettings.Instance.SqlEditorFontSize++; ShowFontSize(); }
    private void FontDown_OnClick(object sender, RoutedEventArgs e) { UiSettings.Instance.SqlEditorFontSize--; ShowFontSize(); }

    // ===== Results =====

    private void RebuildResultTabs()
    {
        ResultTabs.Items.Clear();
        if (_vm is not null)
        {
            foreach (var tab in _vm.Results)
                ResultTabs.Items.Add(new TabItem { Header = new TextBlock { Text = tab.Header }, Content = BuildGrid(tab), Tag = tab });
        }
        var hasErrors = _vm?.Messages.Any(m => m.Kind == SqlMessageKind.Error) == true;
        var header = new TextBlock { Text = "Messages" };
        if (hasErrors) { header.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0, 0)); header.FontWeight = FontWeights.Bold; }
        // The Messages list is reused: detach it from the previous tab first.
        if (_messages.Parent is TabItem old) old.Content = null;
        ResultTabs.Items.Add(new TabItem { Header = header, Content = _messages });
        var index = _vm?.SelectedResultTab ?? 0;
        ResultTabs.SelectedIndex = Math.Clamp(index, 0, ResultTabs.Items.Count - 1);
        if (_messages.Items.Count > 0) _messages.ScrollIntoView(_messages.Items[^1]);
    }

    private void ResultTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, ResultTabs) || _vm is null || ResultTabs.SelectedIndex < 0) return;
        _vm.SelectedResultTab = ResultTabs.SelectedIndex;
    }

    private static bool IsNumeric(Type t) =>
        t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
        || t == typeof(decimal) || t == typeof(double) || t == typeof(float);

    /// <summary>DataView sorting needs IComparable values (byte[] / UDT / sql_variant columns are not sortable).</summary>
    private static bool IsSortable(Type t) => t != typeof(object) && t != typeof(byte[]) && typeof(IComparable).IsAssignableFrom(t);

    private DataGrid BuildGrid(SqlResultTab tab)
    {
        var grid = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserSortColumns = true,
            CanUserReorderColumns = true,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
            ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader,
            HeadersVisibility = DataGridHeadersVisibility.All,
            RowHeaderWidth = 16,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true,
            BorderThickness = new Thickness(0)
        };
        // Same light gray lines as every XAML grid (Resources/Branding.xaml, GridLineBrush).
        grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "GridLineBrush");
        grid.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty, "GridLineBrush");
        grid.SetBinding(FontSizeProperty, new Binding(nameof(UiSettings.DataGridFontSizePx)) { Source = UiSettings.Instance, Mode = BindingMode.OneWay });
        var plain = (Style)FindResource("SqlCellText");
        var right = (Style)FindResource("SqlCellTextRight");
        for (var i = 0; i < tab.Columns.Count; i++)
        {
            var col = tab.Columns[i];
            var field = "c" + i;
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = new TextBlock { Text = col.Name, ToolTip = col.DataTypeName },
                Binding = new Binding(field) { Mode = BindingMode.OneWay, Converter = new CellValueConverter(col.ClrType) },
                ElementStyle = IsNumeric(col.ClrType) ? right : plain,
                SortMemberPath = field,
                CanUserSort = IsSortable(col.ClrType),
                MinWidth = 30,
                MaxWidth = 600,
                IsReadOnly = true
            });
        }
        grid.ItemsSource = tab.View;
        // Text cells: double-click or F2 opens the read-only "..." memo viewer.
        grid.MouseDoubleClick += (_, e) =>
        {
            if (FindCell(e.OriginalSource as DependencyObject) is { } cell && OpenResultMemo(grid, tab, cell.Column)) e.Handled = true;
        };
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None && OpenResultMemo(grid, tab, grid.CurrentCell.Column)) e.Handled = true;
        };
        return grid;
    }

    private MemoEditorWindow? _resultMemo;

    private static DataGridCell? FindCell(DependencyObject? d)
    {
        while (d is not null and not DataGridCell)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as DataGridCell;
    }

    /// <summary>Read-only memo viewer of a text column of a result grid (char/varchar/nchar/nvarchar/text/ntext).</summary>
    private bool OpenResultMemo(DataGrid grid, SqlResultTab tab, DataGridColumn? column)
    {
        if (column is null || grid.CurrentItem is null) return false;
        var i = grid.Columns.IndexOf(column);
        if (i < 0 || i >= tab.Columns.Count) return false;
        var col = tab.Columns[i];
        var isText = string.IsNullOrEmpty(col.DataTypeName) ? col.ClrType == typeof(string) : MemoEditRules.IsTextTypeName(col.DataTypeName);
        if (!isText) return false;
        _resultMemo?.Close();
        try
        {
            _resultMemo = MemoEditorWindow.Open(Window.GetWindow(this), new ResultGridMemoSource(grid, column, "c" + i, col.Name));
            _resultMemo.Closed += (_, _) => _resultMemo = null;
        }
        catch (Exception ex)
        {
            App.ReportError(ex);
        }
        return true;
    }

    private void Messages_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_messages.SelectedItem is SqlMessage { EditorLine: { } line }) GoToLine(line, markError: true);
    }

    private void CopyMessages()
    {
        var text = string.Join(Environment.NewLine, _messages.SelectedItems.OfType<SqlMessage>().Select(m => m.Text));
        if (text.Length > 0) Clipboard.SetText(text);
    }

    // ===== History =====

    private void HistoryGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _vm?.SelectedHistory is null) return;
        if (_vm.InsertHistoryCommand.CanExecute(null)) _vm.InsertHistoryCommand.Execute(null);
        e.Handled = true;
    }

    private void HistoryGrid_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm?.SelectedHistory is null) return;
        if (_vm.InsertHistoryCommand.CanExecute(null)) _vm.InsertHistoryCommand.Execute(null);
        e.Handled = true;
    }

    // ===== Find / Replace =====

    private void Find_OnClick(object sender, RoutedEventArgs e) => ShowFind();
    private void Replace_OnClick(object sender, RoutedEventArgs e) => ShowReplace();

    public void ShowFind()
    {
        if (_vm is not null) _vm.SelectedPage = 0;
        if (_search is null) return;
        var sel = Editor.SelectedText;
        if (!string.IsNullOrEmpty(sel) && !sel.Contains('\n')) _search.SearchPattern = sel;
        _search.Open();
        Dispatcher.BeginInvoke(() => _search.Reactivate(), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void FindNext(bool backward)
    {
        if (_search is null) return;
        if (string.IsNullOrEmpty(_search.SearchPattern)) { ShowFind(); return; }
        if (backward) _search.FindPrevious(); else _search.FindNext();
    }

    public void ShowReplace()
    {
        if (_vm is not null) _vm.SelectedPage = 0;
        var sel = Editor.SelectedText;
        var initial = !string.IsNullOrEmpty(sel) && !sel.Contains('\n') ? sel : _search?.SearchPattern;
        if (_replace is null || !_replace.IsLoaded)
        {
            _replace = new ReplaceWindow(Editor) { Owner = Window.GetWindow(this) };
            _replace.Closed += (_, _) => _replace = null;
            _replace.Show();
        }
        _replace.Prepare(initial);
    }

    // ===== ISqlEditorHost =====

    public (string Text, int StartLine) GetExecutionText()
    {
        if (Editor.SelectionLength > 0 && !string.IsNullOrWhiteSpace(Editor.SelectedText))
        {
            var start = Editor.Document.GetLineByOffset(Editor.SelectionStart).LineNumber;
            return (Editor.SelectedText, start);
        }
        return (Editor.Text, 1);
    }

    public void GoToLine(int line, bool markError)
    {
        var doc = Editor.Document;
        if (doc is null || doc.LineCount == 0) return;
        line = Math.Clamp(line, 1, doc.LineCount);
        if (_vm is not null) _vm.SelectedPage = 0;
        var l = doc.GetLineByNumber(line);
        // Caret only (no selection), so F9 afterwards still runs the whole text.
        Editor.Select(l.Offset, 0);
        Editor.TextArea.Caret.Offset = l.Offset;
        Editor.ScrollToLine(line);
        if (markError)
        {
            _errorLine.Line = line;
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
        FocusEditor();
    }

    public void InsertAtCaret(string text)
    {
        if (Editor.Document is null) return;
        Editor.SelectedText = text;
        Editor.SelectionLength = 0;
    }

    public void FocusEditor() =>
        Dispatcher.BeginInvoke(() => { Editor.Focus(); Editor.TextArea.Focus(); }, System.Windows.Threading.DispatcherPriority.Input);

    private void UpdateCaret()
    {
        if (_vm is null) return;
        var c = Editor.TextArea.Caret;
        _vm.CaretText = $"{c.Line} : {c.Column}";
    }

    private void ClearErrorLine()
    {
        if (_errorLine.Line == 0) return;
        _errorLine.Line = 0;
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }
}

/// <summary>Paints the line of the last error in light red until the text changes.</summary>
internal sealed class ErrorLineRenderer : IBackgroundRenderer
{
    private static readonly Brush Fill = CreateFill();

    private static Brush CreateFill()
    {
        var b = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0xD9));
        b.Freeze();
        return b;
    }

    public int Line { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var doc = textView.Document;
        if (Line <= 0 || doc is null || Line > doc.LineCount || !textView.VisualLinesValid) return;
        var line = doc.GetLineByNumber(Line);
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))
            drawingContext.DrawRectangle(Fill, null, new Rect(0, rect.Top, Math.Max(textView.ActualWidth, rect.Right), rect.Height));
    }
}

/// <summary>Loads the original T-SQL .xshd embedded in the assembly (once).</summary>
internal static class TSqlHighlighting
{
    private static IHighlightingDefinition? _definition;
    private static bool _failed;

    public static IHighlightingDefinition? TryGet()
    {
        if (_definition is not null || _failed) return _definition;
        try
        {
            using var stream = typeof(TSqlHighlighting).Assembly.GetManifestResourceStream("SqlExpert.App.Resources.TSql.xshd")
                               ?? throw new InvalidOperationException("Recurso TSql.xshd não encontrado.");
            using var reader = XmlReader.Create(stream);
            _definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch (Exception ex)
        {
            _failed = true;
            App.ReportError(ex);
        }
        return _definition;
    }
}
