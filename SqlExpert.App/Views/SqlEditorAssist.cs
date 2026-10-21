using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using SqlExpert.App.ViewModels;
using SqlExpert.Core.SqlEditor;

namespace SqlExpert.App.Views;

/// <summary>What an editor is connected to: the shell (catalog cache, open object), registration and database.</summary>
public readonly record struct SqlAssistTarget(MainViewModel Shell, string ProfileId, string Database);

/// <summary>
/// Adds code completion (Ctrl+Space) and object links (database object names underlined in light blue;
/// double-click opens the object) to an AvalonEdit editor. The catalog comes from <see cref="MainViewModel"/>'s
/// per connection/database cache; links are computed in the background, debounced after typing.
/// </summary>
public sealed class SqlEditorAssist
{
    private const int MaxLinkedTextLength = 2_000_000;
    private readonly TextEditor _editor;
    private readonly Func<SqlAssistTarget?> _target;
    private readonly Action<string>? _status;
    private readonly DispatcherTimer _linkTimer;
    private readonly ObjectLinkColorizer _colorizer = new();
    private CompletionCatalog? _catalog;
    private (string Profile, string Database)? _catalogKey;
    private Task<CompletionCatalog>? _loading;
    private MainViewModel? _subscribed;
    private SqlCompletionPopup? _popup;
    private int _linkRun;

    public SqlEditorAssist(TextEditor editor, Func<SqlAssistTarget?> target, Action<string>? status = null)
    {
        _editor = editor;
        _target = target;
        _status = status;
        _linkTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _linkTimer.Tick += (_, _) => { _linkTimer.Stop(); _ = RefreshLinksAsync(); };

        editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        editor.TextChanged += (_, _) => _linkTimer.Start();
        editor.DocumentChanged += (_, _) => { _colorizer.Reset(editor.Document); editor.TextArea.TextView.Redraw(); _linkTimer.Start(); };
        _colorizer.Reset(editor.Document);
        editor.TextArea.PreviewKeyDown += TextArea_OnPreviewKeyDown;
        editor.TextArea.TextEntering += TextArea_OnTextEntering;
        editor.TextArea.PreviewMouseLeftButtonDown += TextArea_OnPreviewMouseLeftButtonDown;
        editor.TextArea.AddHandler(UIElement.QueryCursorEvent, new QueryCursorEventHandler(TextArea_OnQueryCursor), handledEventsToo: true);
        editor.TextArea.LostKeyboardFocus += (_, _) => ClosePopup();
        editor.Unloaded += (_, _) => ClosePopup();
    }

    /// <summary>Call when the editor's registration/database may have changed (DataContext change, USE).</summary>
    public void TargetChanged()
    {
        var t = _target();
        if (!ReferenceEquals(_subscribed, t?.Shell))
        {
            if (_subscribed is not null) _subscribed.CompletionCatalogInvalidated -= OnCatalogInvalidated;
            _subscribed = t?.Shell;
            if (_subscribed is not null) _subscribed.CompletionCatalogInvalidated += OnCatalogInvalidated;
        }
        var key = t is { } v ? (v.ProfileId, v.Database.ToUpperInvariant()) : ((string, string)?)null;
        if (key == _catalogKey) return;
        _catalogKey = key;
        _catalog = null;
        _loading = null;
        _colorizer.Clear();
        _editor.TextArea.TextView.Redraw();
        if (t is not null) _ = EnsureCatalogAsync();
    }

    private void OnCatalogInvalidated(string profileId, string? database)
    {
        if (_catalogKey is not { } key || key.Profile != profileId) return;
        if (database is not null && key.Database != database.ToUpperInvariant()) return;
        _catalog = null;
        _loading = null;
        _ = EnsureCatalogAsync();
    }

    /// <summary>Catalog of the current target (null when not connected / failed). Links are refreshed when it arrives.</summary>
    private async Task<CompletionCatalog?> EnsureCatalogAsync()
    {
        var t = _target();
        if (t is not { } target) return null;
        var key = (target.ProfileId, target.Database.ToUpperInvariant());
        if (_catalogKey != key) { _catalogKey = key; _catalog = null; _loading = null; }
        if (_catalog is not null) return _catalog;
        var loading = _loading ??= target.Shell.GetCompletionCatalogAsync(target.ProfileId, target.Database);
        try
        {
            var catalog = await loading.ConfigureAwait(true);
            if (_catalogKey != key || !ReferenceEquals(_loading, loading)) return _catalogKey == key ? _catalog : null;
            _catalog = catalog;
            _ = RefreshLinksAsync();
            return catalog;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_loading, loading)) _loading = null;
            _status?.Invoke("Code completion indisponível (catálogo não lido): " + ex.Message);
            return null;
        }
    }

    // ===== Object links =====

    private async Task RefreshLinksAsync()
    {
        var catalog = _catalog;
        var doc = _editor.Document;
        if (doc is null) return;
        if (catalog is null || doc.TextLength > MaxLinkedTextLength)
        {
            if (_colorizer.Count > 0) { _colorizer.Clear(); _editor.TextArea.TextView.Redraw(); }
            return;
        }
        var run = ++_linkRun;
        var version = doc.Version;
        var text = doc.Text;
        List<SqlObjectLink> links;
        try { links = await Task.Run(() => SqlObjectLinks.Find(text, catalog)).ConfigureAwait(true); }
        catch (Exception) { return; }
        if (run != _linkRun || !ReferenceEquals(doc, _editor.Document)) return;
        if (version is not null && doc.Version is { } now && (!now.BelongsToSameDocumentAs(version) || now.CompareAge(version) != 0))
            return; // typed meanwhile: the timer runs again
        _colorizer.Set(doc, links);
        _editor.TextArea.TextView.Redraw();
    }

    private SqlObjectLink? LinkAtMouse(MouseEventArgs e)
    {
        var pos = _editor.GetPositionFromPoint(e.GetPosition(_editor));
        if (pos is null || _editor.Document is null) return null;
        var offset = _editor.Document.GetOffset(pos.Value.Location);
        return _colorizer.At(offset);
    }

    private void TextArea_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || Keyboard.Modifiers != ModifierKeys.None) return;
        if (LinkAtMouse(e) is not { } link || _target() is not { } t) return;
        e.Handled = true;
        _status?.Invoke($"Abrindo {link.Target.Kind} [{link.Target.Schema}].[{link.Target.Name}]...");
        _editor.Dispatcher.BeginInvoke(async () =>
        {
            try { await t.Shell.OpenObjectFromEditorAsync(t.ProfileId, t.Database, link.Target).ConfigureAwait(true); }
            catch (Exception ex) { App.ReportError(ex); }
        }, DispatcherPriority.Input);
    }

    private void TextArea_OnQueryCursor(object sender, QueryCursorEventArgs e)
    {
        if (_colorizer.Count == 0) return;
        if (LinkAtMouse(e) is null) return;
        e.Cursor = Cursors.Hand;
        e.Handled = true;
    }

    // ===== Code completion =====

    private void TextArea_OnTextEntering(object sender, TextCompositionEventArgs e)
    {
        // Ctrl+Space opens the popup; it must not type a space.
        if (e.Text == " " && (Keyboard.Modifiers & ModifierKeys.Control) != 0) e.Handled = true;
    }

    private void TextArea_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_popup is { IsOpen: true } popup && popup.HandleKey(e.Key))
        {
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            _ = ShowCompletionAsync();
        }
    }

    /// <summary>Ctrl+Space: popup with the objects / columns / keywords starting with the word at the caret.</summary>
    public async Task ShowCompletionAsync()
    {
        var catalog = _catalog ?? await EnsureCatalogAsync().ConfigureAwait(true);
        if (catalog is null)
        {
            _status?.Invoke("Code completion: conecte ao database deste editor.");
            return;
        }
        if (!RefreshPopup(catalog, opening: true))
            _status?.Invoke("Code completion: nada começa com o texto digitado.");
    }

    /// <summary>Recomputes the items at the caret; false (and closes) when there is nothing to show.</summary>
    private bool RefreshPopup(CompletionCatalog catalog, bool opening)
    {
        var doc = _editor.Document;
        if (doc is null) return false;
        var (ctx, items) = SqlCompletion.Complete(doc.Text, _editor.CaretOffset, catalog);
        // A new word (not just more letters of the same one) closes the list, except right after a dot ("dbo." / "c.").
        var newWord = !opening && _popup is { } p && p.Context.WordStart != ctx?.WordStart && !(ctx is { Prefix.Length: 0, Qualifier.Count: > 0 });
        if (ctx is null || items.Count == 0 || newWord)
        {
            ClosePopup();
            return false;
        }
        _popup ??= new SqlCompletionPopup(this);
        _popup.Show(_editor, ctx, items);
        return true;
    }

    internal void Commit(SqlCompletionContext ctx, SqlCompletionItem item)
    {
        var doc = _editor.Document;
        ClosePopup();
        if (doc is null) return;
        var start = Math.Clamp(ctx.WordStart, 0, doc.TextLength);
        var length = Math.Clamp(_editor.CaretOffset - start, 0, doc.TextLength - start);
        doc.Replace(start, length, item.InsertText);
        _editor.CaretOffset = start + item.InsertText.Length;
        _editor.TextArea.Focus();
    }

    internal void ClosePopup()
    {
        if (_popup is null) return;
        _popup.Hide();
    }

    /// <summary>Text changed / caret moved while the popup is open: keep filtering or close.</summary>
    internal void OnEditorChangedWhileOpen()
    {
        if (_popup is not { IsOpen: true }) return;
        if (_catalog is null) { ClosePopup(); return; }
        RefreshPopup(_catalog, opening: false);
    }

    internal TextEditor Editor => _editor;
}

/// <summary>Draws the object links: light-blue underline, normal syntax color.</summary>
internal sealed class ObjectLinkColorizer : DocumentColorizingTransformer
{
    /// <summary>Light blue underline (links are never green: green is reserved elsewhere).</summary>
    public static readonly Color LinkColor = Color.FromRgb(0x4A, 0xA8, 0xE8);
    private static readonly TextDecorationCollection Underline = CreateUnderline();
    private TextSegmentCollection<LinkSegment>? _segments;

    private static TextDecorationCollection CreateUnderline()
    {
        var brush = new SolidColorBrush(LinkColor);
        brush.Freeze();
        var pen = new Pen(brush, 1);
        pen.Freeze();
        var deco = new TextDecoration(TextDecorationLocation.Underline, pen, 1, TextDecorationUnit.FontRecommended, TextDecorationUnit.Pixel);
        deco.Freeze();
        var collection = new TextDecorationCollection { deco };
        collection.Freeze();
        return collection;
    }

    internal sealed class LinkSegment : TextSegment
    {
        public required CatalogObject Target { get; init; }
    }

    public int Count => _segments?.Count ?? 0;

    public void Reset(TextDocument? doc) => _segments = doc is null ? null : new TextSegmentCollection<LinkSegment>(doc);

    public void Clear() => _segments?.Clear();

    public void Set(TextDocument doc, IReadOnlyList<SqlObjectLink> links)
    {
        var segments = new TextSegmentCollection<LinkSegment>(doc);
        foreach (var l in links)
            if (l.End <= doc.TextLength)
                segments.Add(new LinkSegment { StartOffset = l.Start, Length = l.Length, Target = l.Target });
        _segments = segments;
    }

    public SqlObjectLink? At(int offset)
    {
        var s = _segments?.FindSegmentsContaining(offset).FirstOrDefault();
        return s is null ? null : new SqlObjectLink(s.StartOffset, s.Length, s.Target);
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_segments is null || _segments.Count == 0) return;
        foreach (var s in _segments.FindOverlappingSegments(line.Offset, line.Length))
        {
            var start = Math.Max(s.StartOffset, line.Offset);
            var end = Math.Min(s.EndOffset, line.EndOffset);
            if (end <= start) continue;
            ChangeLinePart(start, end, e => e.TextRunProperties.SetTextDecorations(Underline));
        }
    }
}

/// <summary>Completion list: Type | Name (bold) | detail, resizable with the grip, driven by the editor's keys.</summary>
internal sealed class SqlCompletionPopup
{
    private static double _width = 420, _height = 220;
    private readonly SqlEditorAssist _owner;
    private readonly Popup _popup;
    private readonly ListBox _list;
    private readonly Border _frame;
    private TextEditor? _editor;

    public SqlCompletionPopup(SqlEditorAssist owner)
    {
        _owner = owner;
        _list = new ListBox
        {
            Focusable = false,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = BuildTemplate(),
            FontSize = 12
        };
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 0, 2, 0)));
        _list.ItemContainerStyle = itemStyle;
        _list.MouseDoubleClick += (_, e) =>
        {
            if (_list.SelectedItem is SqlCompletionItem item) { e.Handled = true; _owner.Commit(Context, item); }
        };

        var grip = new Thumb { Width = 14, Height = 14, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        grip.Template = BuildGripTemplate();
        grip.DragDelta += (_, e) =>
        {
            _width = Math.Clamp(_frame!.Width + e.HorizontalChange, 220, 1200);
            _height = Math.Clamp(_frame.Height + e.VerticalChange, 90, 900);
            _frame.Width = _width;
            _frame.Height = _height;
        };
        var grid = new Grid();
        grid.Children.Add(_list);
        grid.Children.Add(grip);
        _frame = new Border
        {
            Child = grid,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
            Width = _width,
            Height = _height
        };
        _popup = new Popup
        {
            Child = _frame,
            StaysOpen = true,
            AllowsTransparency = false,
            Placement = PlacementMode.Relative,
            Focusable = false
        };
    }

    public SqlCompletionContext Context { get; private set; } = new(0, 0, "", Array.Empty<string>(), false);

    public bool IsOpen => _popup.IsOpen;

    private static DataTemplate BuildTemplate()
    {
        var grid = new FrameworkElementFactory(typeof(Grid));
        var c0 = new FrameworkElementFactory(typeof(ColumnDefinition));
        c0.SetValue(ColumnDefinition.WidthProperty, new GridLength(72));
        var c1 = new FrameworkElementFactory(typeof(ColumnDefinition));
        c1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
        var c2 = new FrameworkElementFactory(typeof(ColumnDefinition));
        c2.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
        grid.AppendChild(c0);
        grid.AppendChild(c1);
        grid.AppendChild(c2);
        var type = new FrameworkElementFactory(typeof(TextBlock));
        type.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SqlCompletionItem.TypeText)) { Mode = System.Windows.Data.BindingMode.OneTime });
        type.SetValue(Grid.ColumnProperty, 0);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SqlCompletionItem.DisplayName)) { Mode = System.Windows.Data.BindingMode.OneTime });
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetValue(Grid.ColumnProperty, 1);
        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SqlCompletionItem.Detail)) { Mode = System.Windows.Data.BindingMode.OneTime });
        detail.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)));
        detail.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 4, 0));
        detail.SetValue(Grid.ColumnProperty, 2);
        grid.AppendChild(type);
        grid.AppendChild(name);
        grid.AppendChild(detail);
        return new DataTemplate { VisualTree = grid };
    }

    private static ControlTemplate BuildGripTemplate()
    {
        var grip = new FrameworkElementFactory(typeof(ResizeGrip));
        return new ControlTemplate(typeof(Thumb)) { VisualTree = grip };
    }

    public void Show(TextEditor editor, SqlCompletionContext ctx, IReadOnlyList<SqlCompletionItem> items)
    {
        if (!ReferenceEquals(_editor, editor))
        {
            if (_editor is not null) Detach(_editor);
            _editor = editor;
            editor.Document.Changed += OnDocumentChanged;
            editor.TextArea.Caret.PositionChanged += OnCaretChanged;
        }
        var previous = _list.SelectedItem as SqlCompletionItem;
        Context = ctx;
        _list.ItemsSource = items;
        var keep = previous is null ? -1 : items.ToList().FindIndex(i => i.DisplayName == previous.DisplayName && i.Kind == previous.Kind);
        _list.SelectedIndex = keep >= 0 ? keep : 0;
        if (_list.SelectedItem is not null) _list.ScrollIntoView(_list.SelectedItem);

        var view = editor.TextArea.TextView;
        var loc = editor.Document.GetLocation(ctx.WordStart);
        var p = view.GetVisualPosition(new TextViewPosition(loc), VisualYPosition.LineBottom) - view.ScrollOffset;
        _popup.PlacementTarget = view;
        _popup.HorizontalOffset = Math.Max(0, p.X);
        _popup.VerticalOffset = p.Y + 1;
        _popup.IsOpen = true;
    }

    private void Detach(TextEditor editor)
    {
        editor.Document.Changed -= OnDocumentChanged;
        editor.TextArea.Caret.PositionChanged -= OnCaretChanged;
    }

    public void Hide()
    {
        _popup.IsOpen = false;
        if (_editor is not null) { Detach(_editor); _editor = null; }
    }

    private bool _refreshQueued;

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e) => QueueRefresh();

    private void OnCaretChanged(object? sender, EventArgs e)
    {
        if (_editor is null) return;
        var caret = _editor.CaretOffset;
        if (caret < Context.WordStart) { _owner.ClosePopup(); return; }
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued || _editor is null) return;
        _refreshQueued = true;
        _editor.Dispatcher.BeginInvoke(() => { _refreshQueued = false; _owner.OnEditorChangedWhileOpen(); }, DispatcherPriority.Input);
    }

    /// <summary>Keys while open: arrows / PageUp / PageDown move, Enter / Tab insert, Esc closes. True = handled.</summary>
    public bool HandleKey(Key key)
    {
        var count = _list.Items.Count;
        if (count == 0) return false;
        var page = Math.Max(1, (int)(_frame.Height / 18) - 1);
        switch (key)
        {
            case Key.Escape:
                _owner.ClosePopup();
                return true;
            case Key.Enter:
            case Key.Tab:
                if (_list.SelectedItem is SqlCompletionItem item) _owner.Commit(Context, item);
                else _owner.ClosePopup();
                return true;
            case Key.Down: Move(1, count); return true;
            case Key.Up: Move(-1, count); return true;
            case Key.PageDown: Move(page, count); return true;
            case Key.PageUp: Move(-page, count); return true;
            default:
                return false;
        }
    }

    private void Move(int delta, int count)
    {
        var i = Math.Clamp((_list.SelectedIndex < 0 ? 0 : _list.SelectedIndex) + delta, 0, count - 1);
        _list.SelectedIndex = i;
        _list.ScrollIntoView(_list.SelectedItem);
    }
}
