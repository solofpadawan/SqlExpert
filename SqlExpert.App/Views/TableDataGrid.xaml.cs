using System.Data;
using System.Windows.Controls.Primitives;
using SqlExpert.Core.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SqlExpert.App.Services;
using SqlExpert.App.ViewModels;
using SqlExpert.Core.DataForms;

namespace SqlExpert.App.Views;

/// <summary>Data tab: navigator + editable grid bound to <see cref="TableDataViewModel"/>.</summary>
public partial class TableDataGrid : UserControl
{
    private TableDataViewModel? _vm;

    private enum DataViewMode { Grid, Form, Print }
    private DataViewMode _mode = DataViewMode.Grid;

    public TableDataGrid()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Grid.LoadingRow += Grid_OnLoadingRow;
        FormGrid.DataContextChanged += (_, _) => FormGrid.IsEnabled = FormGrid.DataContext is System.Data.DataRowView;
        FormGrid.IsEnabled = false;
        IsVisibleChanged += (_, _) =>
        {
            if (_vm is not null) _vm.IsViewActive = IsVisible;
            if (!IsVisible) { CancelPrintWork(); CloseLookup(); CloseMemo(); }
            else if (_mode == DataViewMode.Print) EnsurePrintPreview();
        };

        // Font size spin box mirrors the shared setting (changed here or in another grid).
        ShowFontSize();
        UiSettings.Instance.PropertyChanged += OnUiSettingsChanged;
        Unloaded += (_, _) => UiSettings.Instance.PropertyChanged -= OnUiSettingsChanged;
        Loaded += (_, _) =>
        {
            UiSettings.Instance.PropertyChanged -= OnUiSettingsChanged;
            UiSettings.Instance.PropertyChanged += OnUiSettingsChanged;
            ShowFontSize();
        };
    }

    // ===== Font size spin box =====

    private void OnUiSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UiSettings.DataGridFontSize) && !FontSizeBox.IsKeyboardFocusWithin)
            ShowFontSize();
    }

    private void ShowFontSize() => FontSizeBox.Text = UiSettings.Instance.DataGridFontSize.ToString();

    private void StepFontSize(int delta)
    {
        UiSettings.Instance.DataGridFontSize += delta;
        ShowFontSize();
    }

    /// <summary>Applies the typed value; invalid or out-of-range input reverts to the current size.</summary>
    private void ApplyTypedFontSize()
    {
        var text = FontSizeBox.Text.Trim();
        if (int.TryParse(text, out var v)
            && v >= Core.Models.UserSettings.MinDataGridFontSize
            && v <= Core.Models.UserSettings.MaxDataGridFontSize)
        {
            UiSettings.Instance.DataGridFontSize = v;
        }

        ShowFontSize();
    }

    private void FontSizeBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                ApplyTypedFontSize();
                FontSizeBox.SelectAll();
                e.Handled = true;
                break;
            case Key.Escape:
                ShowFontSize();
                e.Handled = true;
                break;
            case Key.Up:
                StepFontSize(1);
                e.Handled = true;
                break;
            case Key.Down:
                StepFontSize(-1);
                e.Handled = true;
                break;
        }
    }

    private void FontSizeBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ApplyTypedFontSize();

    private void FontSize_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        StepFontSize(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void FontSizeUp_OnClick(object sender, RoutedEventArgs e) => StepFontSize(1);

    private void FontSizeDown_OnClick(object sender, RoutedEventArgs e) => StepFontSize(-1);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        CancelPrintWork();
        if (_vm is not null)
        {
            _vm.DataReloaded -= OnDataReloaded;
            _vm.DataChanged -= OnDataChanged;
            _vm.CommitEditRequested -= CommitEdit;
            _vm.CancelEditRequested -= CancelEdit;
            _vm.EditRequested -= BeginEditCurrent;
        }

        _vm = DataContext as TableDataViewModel;
        if (_vm is null) return;
        _vm.DataReloaded += OnDataReloaded;
        _vm.DataChanged += OnDataChanged;
        _vm.CommitEditRequested += CommitEdit;
        _vm.CancelEditRequested += CancelEdit;
        _vm.EditRequested += BeginEditCurrent;
        OnDataReloaded();
        _vm.IsViewActive = IsVisible;
    }

    private static bool IsNumeric(Type t) => Core.DataForms.FormFieldLayout.IsNumericType(t);

    private void OnDataReloaded()
    {
        CloseLookup();
        RebuildColumns();
        RebuildForm();
        MarkPrintStale();
    }

    private void OnDataChanged()
    {
        MarkPrintStale();
        QueueRowHeaderRefresh();
    }

    // ===== Bottom view tabs =====

    private void ViewTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, ViewTabs)) return;
        var mode = ViewTabs.SelectedIndex switch { 1 => DataViewMode.Form, 2 => DataViewMode.Print, _ => DataViewMode.Grid };
        if (mode == _mode) return;

        // Leaving a view: push any in-progress edit into the DataTable (stays pending until Post).
        CloseLookup();
        CommitGridEdit();
        CommitFormEdit();
        _mode = mode;

        Grid.Visibility = mode == DataViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
        FormHost.Visibility = mode == DataViewMode.Form ? Visibility.Visible : Visibility.Collapsed;
        PrintHost.Visibility = mode == DataViewMode.Print ? Visibility.Visible : Visibility.Collapsed;
        if (mode != DataViewMode.Print) CancelPrintWork();

        switch (mode)
        {
            case DataViewMode.Grid:
                Dispatcher.BeginInvoke(() =>
                {
                    if (Grid.SelectedItem is not null) Grid.ScrollIntoView(Grid.SelectedItem);
                }, System.Windows.Threading.DispatcherPriority.Background);
                break;
            case DataViewMode.Form:
                Dispatcher.BeginInvoke(() => FocusFirstFormEditor(editableOnly: false), System.Windows.Threading.DispatcherPriority.Background);
                break;
            case DataViewMode.Print:
                EnsurePrintPreview();
                break;
        }
    }

    private void RebuildColumns()
    {
        Grid.Columns.Clear();
        if (_vm is null) return;
        Grid.IsReadOnly = _vm.IsReadOnly;

        foreach (var spec in _vm.Columns)
        {
            var header = spec.Info.IsPrimaryKey ? "🔑 " + spec.Header : spec.Header;
            DataGridColumn column;
            if (spec.DataType == typeof(bool))
            {
                column = new DataGridCheckBoxColumn
                {
                    Binding = new Binding(spec.Field)
                    {
                        Converter = new NullableBoolConverter(),
                        Mode = spec.IsReadOnly ? BindingMode.OneWay : BindingMode.TwoWay
                    },
                    IsThreeState = spec.IsNullable
                };
            }
            else if (spec.HasEditableLookup && !_vm.IsReadOnly && !spec.IsReadOnly)
            {
                // Foreign key: "▾" button in edit mode opens the "Data of [table]" lookup (Alt+↓ / F4).
                var fkColumn = new ForeignKeyTextColumn(spec.Lookup!, spec.DataType)
                {
                    Binding = new Binding(spec.Field)
                    {
                        Converter = new CellValueConverter(spec.DataType),
                        Mode = BindingMode.TwoWay,
                        ValidatesOnExceptions = true
                    },
                    ElementStyle = (Style)FindResource(IsNumeric(spec.DataType) ? "CellTextRight" : "CellText"),
                    OpenLookup = OpenGridLookup
                };
                column = fkColumn;
            }
            else
            {
                // NULL shows the red "<null>"; the editor starts empty and leaves NULL alone when left blank.
                // Date columns: pt-BR text + "▾" calendar (Alt+↓ / F4); text columns: "..." memo window (Ctrl+Enter).
                // Numeric columns: "▾" calculator (Alt+↓ / F4); "=" writes the result respecting precision/scale.
                var dateKind = DateValueText.KindOf(spec.Info);
                var isMemo = dateKind == DateColumnKind.None && IsMemoColumn(spec);
                var isCalc = dateKind == DateColumnKind.None && !isMemo && IsCalculatorColumn(spec);
                column = new NullAwareTextColumn(spec.DataType, dateKind)
                {
                    IsCalculator = isCalc,
                    SideButtonText = dateKind != DateColumnKind.None || isCalc ? "▾" : isMemo ? "..." : null,
                    SideButtonToolTip = dateKind != DateColumnKind.None ? "Calendário (Alt+↓ / F4)"
                        : isCalc ? "Calculadora (Alt+↓ / F4)" : isMemo ? "Editar o texto (Ctrl+Enter)" : null,
                    SideGesture = dateKind != DateColumnKind.None || isCalc ? ForeignKeyTextColumn.IsLookupGesture : isMemo ? IsMemoGesture : null,
                    SideAction = dateKind != DateColumnKind.None ? OpenGridCalendar : isCalc ? OpenGridCalculator : isMemo ? OpenGridMemo : null,
                    Binding = new Binding(spec.Field)
                    {
                        Converter = new CellValueConverter(spec.DataType, dateKind),
                        Mode = spec.IsReadOnly ? BindingMode.OneWay : BindingMode.TwoWay,
                        ValidatesOnExceptions = true
                    },
                    ElementStyle = (Style)FindResource(IsNumeric(spec.DataType) ? "CellTextRight" : "CellText")
                };
            }

            if (spec.Lookup is { } lookup)
            {
                if (lookup.IsEnabled && !_vm.IsReadOnly) header += " ▾";
                column.HeaderStyle = HeaderWithToolTip(lookup.ToolTip);
            }
            column.Header = header;
            column.IsReadOnly = spec.IsReadOnly || _vm.IsReadOnly;
            column.SortMemberPath = spec.Field;
            column.MinWidth = 40;
            column.MaxWidth = 420;
            Grid.Columns.Add(column);
        }
    }

    private Style HeaderWithToolTip(string tip)
    {
        var style = new Style(typeof(DataGridColumnHeader), TryFindResource(typeof(DataGridColumnHeader)) as Style);
        style.Setters.Add(new Setter(ToolTipProperty, tip));
        return style;
    }

    // ===== Foreign-key lookup (grid + form) =====

    private ForeignKeyLookupPopup? _lookup;

    private void CloseLookup()
    {
        var popup = _lookup;
        _lookup = null;
        popup?.Close();
        var calendar = _calendar;
        _calendar = null;
        calendar?.Close();
        var calculator = _calculator;
        _calculator = null;
        calculator?.Close();
    }

    // ===== Numeric columns: "▾" calculator =====

    private CalculatorPopup? _calculator;

    private bool IsCalculatorColumn(GridColumnSpec spec) =>
        _vm is { IsReadOnly: false } && !spec.IsReadOnly && NumericFit.IsCalculatorType(spec.DataType);

    /// <summary>Value the calculator starts from: the typed text if it parses, else the cell value (NULL = 0).</summary>
    internal static decimal? CalculatorStart(object? value)
    {
        if (DataValueText.IsNull(value)) return null;
        try { return Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture); }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Fits a calculator result to the column (rounds to its scale / integer, rejects overflow). Returns the typed value
    /// or sets <paramref name="error"/>; rounding is reported on the status bar.
    /// </summary>
    private object? FitCalculatorResult(GridColumnSpec spec, decimal result, out string? error)
    {
        var fit = NumericFit.Fit(result, spec.DataType, spec.Info, System.Globalization.CultureInfo.CurrentCulture);
        error = fit.Status == NumericFitStatus.Overflow ? fit.Message : null;
        if (fit.Status == NumericFitStatus.Rounded) _vm?.ShowStatus(fit.Message);
        return fit.Value;
    }

    /// <summary>"▾" / Alt+↓ / F4 in a numeric cell editor: calculator under the cell; "=" puts the result in the cell.</summary>
    private void OpenGridCalculator(NullAwareTextColumn column, DataGridCell cell, TextBox tb)
    {
        if (_vm is null || SpecOf(column) is not { } spec) return;
        if (_calculator is { IsOpen: true })
        {
            CloseLookup();
            return;
        }
        object? current;
        try { current = CellValueConverter.ParseCellText(tb.Text, column.DataType, column.DateKind); }
        catch (Exception) { current = NullAwareTextColumn.EditorConverter(tb)?.Original; }

        CalculatorPopup? popup = null;
        popup = new CalculatorPopup(CalculatorStart(current), System.Globalization.CultureInfo.CurrentCulture, result =>
        {
            var value = FitCalculatorResult(spec, result, out var error);
            if (error is not null) return error;
            if (ReferenceEquals(_calculator, popup)) _calculator = null;
            tb.Text = DataValueText.EditorText(value, System.Globalization.CultureInfo.CurrentCulture);
            try { Grid.CommitEdit(DataGridEditingUnit.Cell, true); } catch (InvalidOperationException) { }
            Grid.Focus();
            cell.Focus();
            return null;
        });
        popup.Cancelled += () =>
        {
            if (ReferenceEquals(_calculator, popup)) _calculator = null;
            tb.Focus();
        };
        _calculator = popup;
        popup.Open(cell);
    }

    // ===== Date columns: "▾" calendar =====

    private DateCalendarPopup? _calendar;

    private GridColumnSpec? SpecOf(DataGridColumn? column) =>
        column is null || _vm is null ? null : _vm.Columns.FirstOrDefault(c => c.Field == column.SortMemberPath);

    /// <summary>"▾" / Alt+↓ / F4 in a date cell editor: calendar under the cell; the pick goes into the editor and is committed.</summary>
    private void OpenGridCalendar(NullAwareTextColumn column, DataGridCell cell, TextBox tb)
    {
        if (_vm is null || SpecOf(column) is not { } spec) return;
        if (_calendar is { IsOpen: true })
        {
            CloseLookup();
            return;
        }
        var original = NullAwareTextColumn.EditorConverter(tb)?.Original;
        object? current;
        try { current = CellValueConverter.ParseCellText(tb.Text, column.DataType, column.DateKind); }
        catch (Exception) { current = original; }

        var popup = new DateCalendarPopup(DateValueText.SelectedDate(current),
            DateValueText.CanClear(spec.IsNullable, column.IsReadOnly || Grid.IsReadOnly));
        popup.Picked += result =>
        {
            if (ReferenceEquals(_calendar, popup)) _calendar = null;
            var value = CalendarValue(result, current, column.DateKind);
            tb.Text = DataValueText.IsNull(value) ? "" : DateValueText.Format(value, column.DateKind);
            try { Grid.CommitEdit(DataGridEditingUnit.Cell, true); } catch (InvalidOperationException) { }
            Grid.Focus();
            cell.Focus();
        };
        popup.Cancelled += () =>
        {
            if (ReferenceEquals(_calendar, popup)) _calendar = null;
            tb.Focus();
        };
        _calendar = popup;
        popup.Open(cell);
    }

    /// <summary>Day → keeps the time part; Today → today (time kept, 00:00 for NULL); Clear → NULL.</summary>
    internal static object CalendarValue(DateCalendarResult result, object? current, DateColumnKind kind) => result.Action switch
    {
        DateCalendarAction.Day => DateValueText.PickDay(current, result.Date!.Value, kind),
        DateCalendarAction.Today => DateValueText.Today(current, kind, DateTime.Today),
        _ => DBNull.Value
    };

    // ===== Text columns: "..." memo window =====

    private MemoEditorWindow? _memo;

    private static bool IsMemoColumn(GridColumnSpec spec) => spec.DataType == typeof(string) && MemoEditRules.IsTextColumn(spec.Info);

    /// <summary>Ctrl+Enter: open the memo window of a text cell.</summary>
    internal static bool IsMemoGesture(KeyEventArgs e) =>
        e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control;

    private void OpenGridMemo(NullAwareTextColumn column, DataGridCell cell, TextBox tb)
    {
        if (cell.DataContext is DataRowView row && SpecOf(column) is { } spec) OpenMemo(spec, row);
    }

    /// <summary>
    /// Opens (or brings back) the memo window for <paramref name="spec"/> on the current record. The text typed in
    /// the grid or form is applied first; read-only tables / columns open it read-only.
    /// </summary>
    internal void OpenMemo(GridColumnSpec spec, DataRowView row)
    {
        if (_vm is not { } vm) return;
        CommitEdit();
        if (!ReferenceEquals(vm.CurrentRowView, row))
        {
            var index = vm.View?.Cast<DataRowView>().ToList().IndexOf(row) ?? -1;
            if (index >= 0) vm.CurrentIndex = index;
        }
        if (_memo is { IsLoaded: true } open && open.Source is TableMemoSource s && s.Field == spec.Field)
        {
            open.Activate();
            return;
        }
        CloseMemo();
        try
        {
            _memo = MemoEditorWindow.Open(Window.GetWindow(this), new TableMemoSource(vm, spec, CommitEdit));
            _memo.Closed += (_, _) => _memo = null;
        }
        catch (Exception ex)
        {
            App.ReportError(ex);
        }
    }

    private void CloseMemo()
    {
        var w = _memo;
        _memo = null;
        w?.Close();
    }

    private bool TryOpenMemoFromSelection(KeyEventArgs e)
    {
        if (!IsMemoGesture(e) || Grid.CurrentCell.Item is not DataRowView row) return false;
        if (SpecOf(Grid.CurrentCell.Column) is not { } spec || !IsMemoColumn(spec)) return false;
        OpenMemo(spec, row);
        return true;
    }

    /// <summary>Opens the FK popup for <paramref name="row"/> under <paramref name="anchor"/>. A second gesture closes it.</summary>
    private void OpenLookup(ForeignKeyLookupTarget target, DataRowView row, UIElement anchor,
        Action<IReadOnlyList<object?>?> onPick, Action onCancel)
    {
        if (_vm is not { } vm) return;
        if (_lookup is { IsOpen: true })
        {
            CloseLookup();
            return;
        }
        var current = vm.CurrentLookupKey(row, target);
        var popup = new ForeignKeyLookupPopup(target, current,
            (fetchAll, refresh, ct) => vm.GetLookupDataAsync(target, fetchAll, refresh, current, ct), onPick);
        popup.Closed += picked =>
        {
            if (ReferenceEquals(_lookup, popup)) _lookup = null;
            if (!picked) onCancel();
        };
        _lookup = popup;
        try
        {
            popup.Open(anchor);
        }
        catch (Exception ex)
        {
            _lookup = null;
            App.ReportError(ex);
        }
    }

    private void OpenGridLookup(ForeignKeyTextColumn column, DataGridCell cell)
    {
        if (_vm is null || cell.DataContext is not DataRowView row) return;
        OpenLookup(column.Target, row, cell,
            picked =>
            {
                // The picked key replaces whatever was typed in the cell; the row stays pending until Post/Cancel.
                try { Grid.CancelEdit(DataGridEditingUnit.Cell); } catch (InvalidOperationException) { }
                if (_vm?.ApplyLookup(row, column.Target, picked) == true)
                {
                    try { Grid.CommitEdit(DataGridEditingUnit.Row, true); } catch (InvalidOperationException) { }
                }
                Grid.Focus();
                cell.Focus();
            },
            () => ForeignKeyTextColumn.EditorTextBox(cell.Content as FrameworkElement)?.Focus());
    }

    /// <summary>F4 / Alt+↓ on a selected (not yet edited) FK or date cell: start editing and open the lookup / calendar.</summary>
    private bool TryOpenLookupFromSelection(KeyEventArgs e)
    {
        if (!ForeignKeyTextColumn.IsLookupGesture(e) || Grid.IsReadOnly) return false;
        if (Grid.CurrentCell.Column is NullAwareTextColumn { DateKind: not DateColumnKind.None, IsReadOnly: false } dateColumn
            && Grid.CurrentCell.Item is DataRowView dateItem)
        {
            if (!Grid.BeginEdit()) return false;
            Dispatcher.BeginInvoke(() =>
            {
                if (dateColumn.GetCellContent(dateItem) is FrameworkElement content && content.Parent is DataGridCell cell
                    && NullAwareTextColumn.EditorTextBox(content) is { } tb)
                    OpenGridCalendar(dateColumn, cell, tb);
            }, System.Windows.Threading.DispatcherPriority.Background);
            return true;
        }
        if (Grid.CurrentCell.Column is NullAwareTextColumn { IsCalculator: true, IsReadOnly: false } calcColumn
            && Grid.CurrentCell.Item is DataRowView calcItem)
        {
            if (!Grid.BeginEdit()) return false;
            Dispatcher.BeginInvoke(() =>
            {
                if (calcColumn.GetCellContent(calcItem) is FrameworkElement content && content.Parent is DataGridCell cell
                    && NullAwareTextColumn.EditorTextBox(content) is { } tb)
                    OpenGridCalculator(calcColumn, cell, tb);
            }, System.Windows.Threading.DispatcherPriority.Background);
            return true;
        }
        if (Grid.CurrentCell.Column is not ForeignKeyTextColumn fk || Grid.CurrentCell.Item is not DataRowView item) return false;
        if (!Grid.BeginEdit()) return false;
        Dispatcher.BeginInvoke(() =>
        {
            if (fk.GetCellContent(item)?.Parent is DataGridCell cell) OpenGridLookup(fk, cell);
        }, System.Windows.Threading.DispatcherPriority.Background);
        return true;
    }

    private void CommitEdit()
    {
        CommitFormEdit();
        CommitGridEdit();
    }

    private void CommitGridEdit()
    {
        try
        {
            Grid.CommitEdit(DataGridEditingUnit.Cell, true);
            Grid.CommitEdit(DataGridEditingUnit.Row, true);
        }
        catch (InvalidOperationException) { /* nothing being edited */ }
    }

    private void CancelEdit()
    {
        CancelFormEdit();
        try
        {
            Grid.CancelEdit(DataGridEditingUnit.Cell);
            Grid.CancelEdit(DataGridEditingUnit.Row);
        }
        catch (InvalidOperationException) { }
    }

    private void BeginEditCurrent()
    {
        if (_mode == DataViewMode.Form)
        {
            Dispatcher.BeginInvoke(() => FocusFirstFormEditor(editableOnly: true), System.Windows.Threading.DispatcherPriority.Background);
            return;
        }
        if (_mode != DataViewMode.Grid) return;
        if (_vm?.CurrentRowView is not { } item) return;
        Dispatcher.BeginInvoke(() =>
        {
            Grid.SelectedItem = item;
            Grid.ScrollIntoView(item);
            var col = Grid.Columns.FirstOrDefault(c => !c.IsReadOnly);
            if (col is null) return;
            Grid.Focus();
            Grid.CurrentCell = new DataGridCellInfo(item, col);
            Grid.BeginEdit();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Grid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is not null)
            Grid.ScrollIntoView(Grid.SelectedItem);
        // Left an empty appended record: drop it (after the grid finished moving / committing).
        Dispatcher.BeginInvoke(() => _vm?.DiscardBlankAppendedRecord(), System.Windows.Threading.DispatcherPriority.Background);
    }

    // ===== ↓ on the last row: new record =====

    /// <summary>
    /// ↓ on the last row (or in an empty grid) appends an empty record marked "*" with every field &lt;null&gt;, keeping
    /// the column. A cell being edited is committed first (an invalid value keeps the editor open and adds nothing).
    /// ↓ on an untouched new record does nothing.
    /// </summary>
    private bool TryAppendOnDown()
    {
        if (_vm is null || _mode != DataViewMode.Grid || !_vm.CanModify) return false;
        var count = Grid.Items.Count;
        var current = Grid.CurrentCell.Item is DataRowView rv ? Grid.Items.IndexOf(rv) : Grid.SelectedIndex;
        if (count > 0 && current != count - 1) return false;
        if (_vm.LastRowIsBlankAppended) return true; // swallow: the empty new record is already there
        var column = Grid.CurrentCell.Column ?? Grid.Columns.FirstOrDefault(c => !c.IsReadOnly) ?? Grid.Columns.FirstOrDefault();
        try
        {
            if (!Grid.CommitEdit(DataGridEditingUnit.Cell, true) || !Grid.CommitEdit(DataGridEditingUnit.Row, true)) return true;
        }
        catch (InvalidOperationException) { }
        if (!_vm.AppendRecord()) return false;
        Dispatcher.BeginInvoke(() =>
        {
            if (_vm?.CurrentRowView is not { } item) return;
            Grid.SelectedItem = item;
            Grid.ScrollIntoView(item);
            if (column is null) return;
            Grid.CurrentCell = new DataGridCellInfo(item, column);
            Grid.Focus();
            if (column.GetCellContent(item)?.Parent is DataGridCell cell) cell.Focus();
        }, System.Windows.Threading.DispatcherPriority.Background);
        return true;
    }

    /// <summary>Row header "*" on records that are not in the database yet.</summary>
    private void Grid_OnLoadingRow(object? sender, DataGridRowEventArgs e) =>
        e.Row.Header = TableDataViewModel.IsNewRow(e.Row.Item as DataRowView) ? NewRecordRules.NewRowMarker : null;

    private bool _rowHeadersQueued;

    /// <summary>Post / Cancel / append change which rows are new: refresh the "*" of the realized rows (coalesced).</summary>
    private void QueueRowHeaderRefresh()
    {
        if (_rowHeadersQueued) return;
        _rowHeadersQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _rowHeadersQueued = false;
            for (var i = 0; i < Grid.Items.Count; i++)
            {
                if (Grid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row) continue;
                var header = TableDataViewModel.IsNewRow(row.Item as DataRowView) ? NewRecordRules.NewRowMarker : null;
                if (!Equals(row.Header, header)) row.Header = header;
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Grid_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        var editing = Keyboard.FocusedElement is TextBox;
        if (IsSetNullGesture(e))
        {
            TrySetCurrentCellNull();
            e.Handled = true;
        }
        else if (TryOpenMemoFromSelection(e))
        {
            e.Handled = true;
        }
        else if (!editing && TryOpenLookupFromSelection(e))
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.None && TryAppendOnDown())
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Insert && !editing && _vm.InsertCommand.CanExecute(null))
        {
            _vm.InsertCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.Control && _vm.DeleteCommand.CanExecute(null))
        {
            _vm.DeleteCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Ctrl+0 (top row or keypad): set the current cell to NULL. Ctrl+Del deletes the record.</summary>
    internal static bool IsSetNullGesture(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        return Core.DataForms.CellNullEditing.IsSetNullGesture(key.ToString(),
            (mods & ModifierKeys.Control) != 0, (mods & ModifierKeys.Shift) != 0, (mods & ModifierKeys.Alt) != 0);
    }

    /// <summary>
    /// Ctrl+0 on a grid cell: drops the text being typed and sets the value to NULL (pending until Post/Cancel).
    /// NOT NULL and read-only columns are left alone (the status bar says why).
    /// </summary>
    private void TrySetCurrentCellNull()
    {
        if (_vm is null || Grid.CurrentCell.Column is not { } column || Grid.CurrentCell.Item is not DataRowView row) return;
        var spec = _vm.Columns.FirstOrDefault(c => c.Field == column.SortMemberPath);
        if (spec is null) return;
        try { Grid.CancelEdit(DataGridEditingUnit.Cell); } catch (InvalidOperationException) { }
        if (_vm.SetNull(row, spec, column.IsReadOnly || Grid.IsReadOnly))
        {
            try { Grid.CommitEdit(DataGridEditingUnit.Row, true); } catch (InvalidOperationException) { }
        }
        Grid.Focus();
        if (column.GetCellContent(row)?.Parent is DataGridCell cell) cell.Focus();
    }

    private void TopBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm is null) return;
        TopBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (_vm.RefreshCommand.CanExecute(null))
            _vm.RefreshCommand.Execute(null);
        e.Handled = true;
    }
}
