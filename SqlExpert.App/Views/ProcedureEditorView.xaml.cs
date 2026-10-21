using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Search;
using SqlExpert.App.Services;
using SqlExpert.App.ViewModels;
using SqlExpert.Core.Procedures;
using SqlExpert.Core.SqlEditor;

namespace SqlExpert.App.Views;

/// <summary>Procedure editor view: parameter grid (columns built here, editable), body/full-text editor, results, object tabs.</summary>
public partial class ProcedureEditorView : UserControl, IProcedureEditorHost
{
    /// <summary>Suggestions of the Type cell (any type name may be typed).</summary>
    private static readonly string[] CommonTypes =
    {
        "int", "bigint", "smallint", "tinyint", "bit", "decimal", "numeric", "money", "float", "real",
        "varchar", "nvarchar", "char", "nchar", "varbinary", "binary",
        "date", "datetime", "datetime2", "smalldatetime", "datetimeoffset", "time",
        "uniqueidentifier", "xml", "sql_variant"
    };

    private ProcedureDocumentViewModel? _vm;
    private readonly ErrorLineRenderer _errorLine = new();
    private readonly ListBox _messages;
    private readonly SearchPanel? _search;
    private readonly SqlEditorAssist _assist;

    public ProcedureEditorView()
    {
        InitializeComponent();

        var highlighting = TSqlHighlighting.TryGet();
        foreach (var editor in new[] { Editor, DdlEditor, HistoryEditor })
        {
            if (highlighting is not null) editor.SyntaxHighlighting = highlighting;
            editor.Options.EnableHyperlinks = false;
            editor.Options.EnableEmailHyperlinks = false;
        }
        Editor.Options.HighlightCurrentLine = true;
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
        _messages.MouseDoubleClick += (_, _) =>
        {
            if (_messages.SelectedItem is SqlMessage { EditorLine: { } line }) GoToLine(line, markError: true);
        };
        _messages.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) =>
        {
            var text = string.Join(Environment.NewLine, _messages.SelectedItems.OfType<SqlMessage>().Select(m => m.Text));
            if (text.Length > 0) Clipboard.SetText(text);
        }, (_, e) => e.CanExecute = _messages.SelectedItems.Count > 0));

        // Body editor: Ctrl+Space code completion and object links, like the SQL Editor.
        _assist = new SqlEditorAssist(Editor,
            () => _vm is null ? null : new SqlAssistTarget(_vm.Shell, _vm.ProfileId, _vm.Database),
            s => { if (_vm is not null) _vm.StatusLine = s; });

        BuildParameterColumns();
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) FocusEditor(); };
        RebuildResultTabs();
    }

    // ===== Parameter grid (editable: columns created here with explicit TwoWay bindings) =====

    private static Binding Edit(string path, bool immediate = false) => new(path)
    {
        Mode = BindingMode.TwoWay,
        UpdateSourceTrigger = immediate ? UpdateSourceTrigger.PropertyChanged : UpdateSourceTrigger.LostFocus
    };

    private void BuildParameterColumns()
    {
        var mono = new FontFamily("Consolas");
        DataGridTextColumn Text(string header, string path, double width, bool star = false) => new()
        {
            Header = header,
            Binding = Edit(path),
            Width = star ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
            FontFamily = mono
        };

        ParamGrid.Columns.Add(Text("Name", nameof(ProcedureParameter.Name), 170));

        // Type: editable combo (suggestions + free text such as dbo.TipoTabela).
        var display = new FrameworkElementFactory(typeof(TextBlock));
        display.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProcedureParameter.TypeName)) { Mode = BindingMode.OneWay });
        display.SetValue(TextBlock.FontFamilyProperty, mono);
        display.SetValue(TextBlock.PaddingProperty, new Thickness(2, 0, 2, 0));
        var combo = new FrameworkElementFactory(typeof(ComboBox));
        combo.SetValue(ComboBox.IsEditableProperty, true);
        combo.SetValue(ItemsControl.ItemsSourceProperty, CommonTypes);
        combo.SetValue(ComboBox.FontFamilyProperty, mono);
        combo.SetBinding(ComboBox.TextProperty, Edit(nameof(ProcedureParameter.TypeName), immediate: true));
        ParamGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Type",
            Width = new DataGridLength(150),
            CellTemplate = new DataTemplate { VisualTree = display },
            CellEditingTemplate = new DataTemplate { VisualTree = combo }
        });

        ParamGrid.Columns.Add(Text("Size", nameof(ProcedureParameter.Size), 60));
        ParamGrid.Columns.Add(Text("Scale", nameof(ProcedureParameter.Scale), 50));
        ParamGrid.Columns.Add(Text("Default", nameof(ProcedureParameter.Default), 120));
        ParamGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Output", Binding = Edit(nameof(ProcedureParameter.IsOutput), immediate: true), Width = new DataGridLength(55) });
        ParamGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "ReadOnly", Binding = Edit(nameof(ProcedureParameter.IsReadOnly), immediate: true), Width = new DataGridLength(65) });
        ParamGrid.Columns.Add(Text("Description", nameof(ProcedureParameter.Description), 0, star: true));
        ParamGrid.LoadingRow += (_, e) => e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        // A check box toggles on the first click (no separate "enter edit mode" click).
        ParamGrid.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (FindParent<DataGridCell>(e.OriginalSource as DependencyObject) is { IsEditing: false, IsReadOnly: false } cell && cell.Column is DataGridCheckBoxColumn)
            {
                if (!cell.IsFocused) cell.Focus();
                ParamGrid.BeginEdit();
            }
        };
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null and not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    // ===== View model wiring =====

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.ResultsChanged -= RebuildResultTabs;
            _vm.PropertyChanged -= OnVmPropertyChanged;
            if (ReferenceEquals(_vm.Host, this)) _vm.Host = null;
        }
        _vm = DataContext as ProcedureDocumentViewModel;
        _messages.ItemsSource = _vm?.Messages;
        if (_vm is not null)
        {
            _vm.Host = this;
            _vm.ResultsChanged += RebuildResultTabs;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
        _assist.TargetChanged();
        ShowDocument();
        DdlEditor.Text = _vm?.Ddl ?? "";
        HistoryEditor.Text = _vm?.HistoryText ?? "";
        RebuildResultTabs();
        UpdateCaret();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null) return;
        switch (e.PropertyName)
        {
            case nameof(ProcedureDocumentViewModel.IsFullTextMode):
                ShowDocument();
                break;
            case nameof(ProcedureDocumentViewModel.Ddl):
                DdlEditor.Text = _vm.Ddl;
                break;
            case nameof(ProcedureDocumentViewModel.HistoryText):
                HistoryEditor.Text = _vm.HistoryText;
                break;
        }
    }

    private void ShowDocument()
    {
        ClearErrorLine();
        if (_vm is null) return;
        var doc = _vm.IsFullTextMode ? _vm.FullTextDocument : _vm.BodyDocument;
        if (!ReferenceEquals(Editor.Document, doc)) Editor.Document = doc;
        UpdateCaret();
    }

    // ===== Keyboard =====

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.F9 && mods == ModifierKeys.Control)
        {
            CommitNameBox();
            Execute(_vm.CompileCommand);
            e.Handled = true;
        }
        else if (key == Key.F9 && mods == ModifierKeys.None)
        {
            CommitNameBox();
            Execute(_vm.ExecuteCommand);
            e.Handled = true;
        }
        else if (key == Key.Cancel || (key == Key.Pause && mods == ModifierKeys.Control))
        {
            Execute(_vm.CancelCommand);
            e.Handled = true;
        }
        else if (key == Key.F && mods == ModifierKeys.Control && Editor.IsKeyboardFocusWithin)
        {
            ShowFind();
            e.Handled = true;
        }
    }

    private static void Execute(ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
    }

    private void CommitNameBox()
    {
        if (NameBox.IsKeyboardFocusWithin) NameBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        CommitParameterEdit();
    }

    private void NameBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        NameBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        FocusEditor();
        e.Handled = true;
    }

    private void Editor_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        UiSettings.Instance.SqlEditorFontSize += e.Delta > 0 ? 1 : -1;
        e.Handled = true;
    }

    private void ProcedureMenuButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ProcedureMenuButton.ContextMenu is not { } menu) return;
        menu.DataContext = DataContext;
        menu.PlacementTarget = ProcedureMenuButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void Find_OnClick(object sender, RoutedEventArgs e) => ShowFind();

    private void ShowFind()
    {
        if (_vm is not null) _vm.SelectedTabIndex = 0;
        if (_search is null) return;
        var sel = Editor.SelectedText;
        if (!string.IsNullOrEmpty(sel) && !sel.Contains('\n')) _search.SearchPattern = sel;
        _search.Open();
        Dispatcher.BeginInvoke(() => _search.Reactivate(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Declarations_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: DeclaredItem item } && item.Line > 0) GoToLine(item.Line, markError: false);
    }

    // ===== Results =====

    private void RebuildResultTabs()
    {
        ResultTabs.Items.Clear();
        if (_vm is not null)
        {
            foreach (var tab in _vm.Results)
                ResultTabs.Items.Add(new TabItem { Header = new TextBlock { Text = tab.Header }, Content = SqlEditorView.BuildResultGrid(this, tab), Tag = tab });
        }
        var hasErrors = _vm?.Messages.Any(m => m.Kind == SqlMessageKind.Error) == true;
        var header = new TextBlock { Text = "Messages" };
        if (hasErrors) { header.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0, 0)); header.FontWeight = FontWeights.Bold; }
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

    // ===== IProcedureEditorHost =====

    public void GoToLine(int line, bool markError)
    {
        var doc = Editor.Document;
        if (doc is null || doc.LineCount == 0) return;
        line = Math.Clamp(line, 1, doc.LineCount);
        if (_vm is not null) _vm.SelectedTabIndex = 0;
        var l = doc.GetLineByNumber(line);
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

    public void FocusEditor() =>
        Dispatcher.BeginInvoke(() => { Editor.Focus(); Editor.TextArea.Focus(); }, System.Windows.Threading.DispatcherPriority.Input);

    public void CommitParameterEdit()
    {
        try
        {
            ParamGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ParamGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }
        catch (InvalidOperationException)
        {
            // grid not in a state that can commit (e.g. being rebuilt): nothing pending
        }
    }

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
