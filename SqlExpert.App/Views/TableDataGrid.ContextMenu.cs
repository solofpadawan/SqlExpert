using System.Data;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SqlExpert.App.ViewModels;
using SqlExpert.Core.DataForms;
using SqlExpert.Core.Models;

namespace SqlExpert.App.Views;

/// <summary>
/// Right-click menu of the Data grid (table editor Data tab and SQL Editor results, which host this same control).
/// Layout in <see cref="GridContextMenuLayout"/>; scripts and clipboard text in <see cref="RecordCopyText"/>.
/// Cell actions (Cut/Paste, Set NULL / empty / NOW) only change the row in memory: Post / Cancel decide, as typing.
/// Items not implemented yet stay visible and disabled.
/// </summary>
public partial class TableDataGrid
{
    private ContextMenu? _gridMenu;

    private void InitGridContextMenu()
    {
        _gridMenu = new ContextMenu();
        Grid.ContextMenu = _gridMenu;
        Grid.ContextMenuOpening += Grid_OnContextMenuOpening;
        Grid.PreviewMouseRightButtonDown += Grid_OnPreviewMouseRightButtonDown;
    }

    /// <summary>Right-click on a cell makes it the current cell; a row outside the selection becomes the selection.</summary>
    private void Grid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (FindAncestor<TextBox>(source) is not null) return; // editing text: the text box keeps its own menu
        var row = FindAncestor<DataGridRow>(source);
        if (row?.Item is not DataRowView item) return;
        var cell = FindAncestor<DataGridCell>(source);
        if (!Grid.SelectedItems.Contains(item))
        {
            CommitGridEdit();
            Grid.SelectedItem = item;
        }
        if (cell?.Column is { } column)
        {
            Grid.CurrentCell = new DataGridCellInfo(item, column);
            cell.Focus();
        }
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null && d is not T)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    private void Grid_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_vm is null || _gridMenu is null || _mode != DataViewMode.Grid)
        {
            e.Handled = true;
            return;
        }
        _gridMenu.Items.Clear();
        foreach (var item in BuildGridMenuItems()) _gridMenu.Items.Add(item);
    }

    /// <summary>The menu rows with their enabled state for the current cell / selection.</summary>
    internal IEnumerable<Control> BuildGridMenuItems()
    {
        foreach (var entry in GridContextMenuLayout.Entries)
        {
            if (entry.IsSeparator)
            {
                yield return new Separator();
                continue;
            }
            var mi = new MenuItem
            {
                Header = entry.Caption,
                InputGestureText = entry.Shortcut,
                Tag = entry.Id
            };
            ToolTipService.SetShowOnDisabled(mi, true);
            if (entry.IconKey.Length > 0 && TryFindResource("Icon." + entry.IconKey) is ImageSource icon)
                mi.Icon = new IconImage { Icon = icon };
            if (!entry.IsImplemented)
            {
                mi.IsEnabled = false;
                mi.ToolTip = $"{entry.Caption} — ainda não implementado";
            }
            else
            {
                var (enabled, tip) = CanRun(entry.Id);
                mi.IsEnabled = enabled;
                if (tip is not null) mi.ToolTip = tip;
                var id = entry.Id;
                mi.Click += (_, _) => RunGridMenu(id);
            }
            yield return mi;
        }
    }

    // ===== state =====

    private (DataRowView Row, DataGridColumn Column, GridColumnSpec Spec)? CurrentCellTarget()
    {
        if (_vm is null || Grid.CurrentCell.Column is not { } column || Grid.CurrentCell.Item is not DataRowView row) return null;
        var spec = SpecOf(column);
        return spec is null ? null : (row, column, spec);
    }

    private bool CellEditable(DataGridColumn column, GridColumnSpec spec) =>
        _vm is not null && !_vm.IsReadOnly && !Grid.IsReadOnly && !column.IsReadOnly && !spec.IsReadOnly;

    private (bool Enabled, string? ToolTip) CanRun(string id)
    {
        if (_vm is null) return (false, null);
        var cell = CurrentCellTarget();
        var editable = cell is { } c && CellEditable(c.Column, c.Spec);
        const string readOnly = "A célula atual é somente leitura.";
        switch (id)
        {
            case GridContextMenuLayout.CopyCell:
                return (cell is not null, null);
            case GridContextMenuLayout.CutCell:
                if (!editable) return (false, readOnly);
                return cell!.Value.Spec.IsNullable || cell.Value.Spec.DataType == typeof(string)
                    ? (true, "Copia o valor e limpa a célula (NULL; texto NOT NULL fica vazio)")
                    : (false, "Coluna NOT NULL: use Copy cell value.");
            case GridContextMenuLayout.PasteCell:
                if (!editable) return (false, readOnly);
                return (SafeContainsText(), "Cola o texto da área de transferência na célula atual");
            case GridContextMenuLayout.SetNull:
                if (!editable) return (false, readOnly);
                return cell!.Value.Spec.IsNullable ? (true, null) : (false, "Coluna NOT NULL: não aceita NULL.");
            case GridContextMenuLayout.SetEmpty:
                if (!editable) return (false, readOnly);
                return cell!.Value.Spec.DataType == typeof(string) ? (true, null) : (false, "Somente colunas de texto.");
            case GridContextMenuLayout.SetNow:
                if (!editable) return (false, readOnly);
                return RecordCopyText.NowValue(cell!.Value.Spec.Info, cell.Value.Spec.DataType, DateTimeOffset.Now) is not null
                    ? (true, null) : (false, "Somente colunas de data/hora.");
            case GridContextMenuLayout.AdjustWidths:
                return (Grid.Columns.Count > 0, "Ajusta a largura das colunas ao conteúdo visível");
            case GridContextMenuLayout.CopyAll:
                return (Grid.Items.Count > 0, "Todos os registros lidos, separados por TAB, com cabeçalho (cola no Excel)");
            case GridContextMenuLayout.CopySelected:
                return (SelectedRecords().Count > 0, "Registros selecionados, separados por TAB, com cabeçalho (cola no Excel)");
            case GridContextMenuLayout.CopyInsert:
            case GridContextMenuLayout.CopyUpdate:
                if (SelectedRecords().Count == 0) return (false, null);
                if (_vm.ScriptTarget() is null) return (false, "O resultado não vem de uma única tabela.");
                return (true, id == GridContextMenuLayout.CopyInsert
                    ? "Um INSERT por registro (colunas identity incluídas com SET IDENTITY_INSERT; computed/rowversion omitidas)"
                    : "Um UPDATE por registro (WHERE pela chave primária)");
            case GridContextMenuLayout.ResetOrder:
                return (Grid.Columns.Select((col, i) => col.DisplayIndex != i).Any(x => x), "Volta as colunas à ordem original");
            default:
                return (false, null);
        }
    }

    private static bool SafeContainsText()
    {
        try { return Clipboard.ContainsText(); }
        catch (COMException) { return false; }
    }

    /// <summary>Selected rows in grid order; the current row when nothing is selected.</summary>
    private List<DataRowView> SelectedRecords()
    {
        var rows = Grid.SelectedItems.OfType<DataRowView>().Distinct().ToList();
        if (rows.Count == 0)
        {
            if (Grid.CurrentCell.Item is DataRowView cur) rows.Add(cur);
            else if (_vm?.CurrentRowView is { } vmRow) rows.Add(vmRow);
        }
        if (rows.Count > 1) rows.Sort((a, b) => Grid.Items.IndexOf(a).CompareTo(Grid.Items.IndexOf(b)));
        return rows;
    }

    // ===== actions =====

    /// <summary>Ctrl+Multiply (keypad *): adjust column widths. True when handled.</summary>
    private bool TryHandleGridMenuGesture(KeyEventArgs e)
    {
        if (e.Key == Key.Multiply && Keyboard.Modifiers == ModifierKeys.Control)
        {
            AdjustColumnWidths();
            return true;
        }
        return false;
    }

    private void RunGridMenu(string id)
    {
        if (_vm is null) return;
        try
        {
            switch (id)
            {
                case GridContextMenuLayout.CopyCell: CopyCellValue(cut: false); break;
                case GridContextMenuLayout.CutCell: CopyCellValue(cut: true); break;
                case GridContextMenuLayout.PasteCell: PasteCellValue(); break;
                case GridContextMenuLayout.SetNull: TrySetCurrentCellNull(); break;
                case GridContextMenuLayout.SetEmpty: SetCurrentCell(_ => ""); break;
                case GridContextMenuLayout.SetNow: SetCurrentCell(spec => RecordCopyText.NowValue(spec.Info, spec.DataType, DateTimeOffset.Now)); break;
                case GridContextMenuLayout.AdjustWidths: AdjustColumnWidths(); break;
                case GridContextMenuLayout.CopyAll: CopyRecordsAsText(Grid.Items.OfType<DataRowView>().ToList(), "registro(s)"); break;
                case GridContextMenuLayout.CopySelected: CopyRecordsAsText(SelectedRecords(), "registro(s) selecionado(s)"); break;
                case GridContextMenuLayout.CopyInsert: CopyRecordsAsSql(insert: true); break;
                case GridContextMenuLayout.CopyUpdate: CopyRecordsAsSql(insert: false); break;
                case GridContextMenuLayout.ResetOrder: ResetColumnOrder(); break;
            }
        }
        catch (Exception ex)
        {
            _vm.ShowStatus("Erro: " + ex.Message);
            App.ReportError(ex);
        }
    }

    private void CopyCellValue(bool cut)
    {
        if (_vm is null || CurrentCellTarget() is not { } c) return;
        CommitGridEdit();
        var text = RecordCopyText.ValueText(c.Row[c.Spec.Field], c.Spec.Info, CultureInfo.CurrentCulture);
        if (!SetClipboardText(text)) return;
        if (!cut)
        {
            _vm.ShowStatus($"Valor de {c.Spec.Header} copiado.");
            return;
        }
        if (c.Spec.IsNullable) TrySetCurrentCellNull();
        else SetCurrentCell(_ => "");
    }

    private void PasteCellValue()
    {
        if (_vm is null || CurrentCellTarget() is not { } c) return;
        string text;
        try { text = Clipboard.GetText(); }
        catch (COMException) { _vm.ShowStatus("Área de transferência ocupada; tente de novo."); return; }
        // One cell: a trailing line break (copied from Excel or a text editor) is not part of the value.
        if (c.Spec.DataType != typeof(string) || text.EndsWith('\n')) text = text.TrimEnd('\r', '\n');
        object value;
        try { value = CellValueConverter.ParseCellText(text, c.Spec.DataType, DateValueText.KindOf(c.Spec.Info)); }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException or ArgumentException)
        {
            _vm.ShowStatus($"{c.Spec.Header}: valor inválido na área de transferência ({ex.Message}).");
            return;
        }
        if (DataValueText.IsNull(value) && !c.Spec.IsNullable)
        {
            _vm.ShowStatus($"{c.Spec.Header} é NOT NULL: não aceita NULL.");
            return;
        }
        SetCurrentCell(_ => value);
    }

    /// <summary>Writes a value into the current cell (pending until Post/Cancel), like typing it.</summary>
    private void SetCurrentCell(Func<GridColumnSpec, object?> valueOf)
    {
        if (_vm is null || CurrentCellTarget() is not { } c) return;
        if (!CellEditable(c.Column, c.Spec))
        {
            _vm.ShowStatus($"{c.Spec.Header} é somente leitura.");
            return;
        }
        if (valueOf(c.Spec) is not { } value) return;
        try { Grid.CancelEdit(DataGridEditingUnit.Cell); } catch (InvalidOperationException) { }
        if (_vm.SetCellValue(c.Row, c.Spec, value))
        {
            try { Grid.CommitEdit(DataGridEditingUnit.Row, true); } catch (InvalidOperationException) { }
        }
        Grid.Focus();
        if (c.Column.GetCellContent(c.Row)?.Parent is DataGridCell cell) cell.Focus();
    }

    /// <summary>Every column fits its header and the realized (visible) cells, within the 40..420 px limits.</summary>
    private void AdjustColumnWidths()
    {
        foreach (var column in Grid.Columns) column.Width = new DataGridLength(0, DataGridLengthUnitType.Pixel);
        Grid.UpdateLayout();
        foreach (var column in Grid.Columns) column.Width = DataGridLength.Auto;
        _vm?.ShowStatus("Larguras das colunas ajustadas.");
    }

    private void ResetColumnOrder()
    {
        for (var i = 0; i < Grid.Columns.Count; i++) Grid.Columns[i].DisplayIndex = i;
        _vm?.ShowStatus("Ordem original das colunas restaurada.");
    }

    /// <summary>Visible columns in on-screen order with their specs.</summary>
    private List<(DataGridColumn Column, GridColumnSpec Spec)> DisplayedColumns() =>
        Grid.Columns.Where(c => c.Visibility == Visibility.Visible)
            .OrderBy(c => c.DisplayIndex)
            .Select(c => (Column: c, Spec: SpecOf(c)))
            .Where(x => x.Spec is not null)
            .Select(x => (x.Column, x.Spec!))
            .ToList();

    private void CopyRecordsAsText(IReadOnlyList<DataRowView> rows, string what)
    {
        if (_vm is null || rows.Count == 0) return;
        CommitGridEdit();
        var cols = DisplayedColumns();
        var culture = CultureInfo.CurrentCulture;
        var text = RecordCopyText.ToTabSeparated(
            cols.Select(c => c.Spec.Header).ToList(),
            rows.Select(r => (IReadOnlyList<string?>)cols.Select(c => RecordCopyText.ValueText(r[c.Spec.Field], c.Spec.Info, culture)).ToList()));
        if (SetClipboardText(text))
            _vm.ShowStatus($"{rows.Count} {what} copiado(s) para a área de transferência.");
    }

    private void CopyRecordsAsSql(bool insert)
    {
        if (_vm is null || _vm.ScriptTarget() is not { } target) return;
        var rows = SelectedRecords();
        if (rows.Count == 0) return;
        CommitGridEdit();
        var infos = target.Columns.Select(c => c.Info).ToList();
        var records = rows.Select(r => new CopyRecord(
            target.Columns.Select(c => (object?)r[c.Spec.Field]).ToList(),
            OriginalValues(r.Row, target.Columns.Select(c => c.Spec.Field).ToList()))).ToList();
        var sql = insert
            ? RecordCopyText.BuildInserts(target.Schema, target.Table, infos, records)
            : RecordCopyText.BuildUpdates(target.Schema, target.Table, infos, records);
        if (SetClipboardText(sql))
            _vm.ShowStatus($"{rows.Count} registro(s) copiado(s) como {(insert ? "INSERT" : "UPDATE")}.");
    }

    /// <summary>Values read from the database for a row with pending edits (null when not modified).</summary>
    private static IReadOnlyList<object?>? OriginalValues(DataRow row, IReadOnlyList<string> fields)
    {
        if (row.RowState != DataRowState.Modified || !row.HasVersion(DataRowVersion.Original)) return null;
        return fields.Select(f => (object?)row[f, DataRowVersion.Original]).ToList();
    }

    private bool SetClipboardText(string text)
    {
        // The clipboard can be briefly locked by another program (CLIPBRD_E_CANT_OPEN): retry a few times.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (COMException) when (attempt < 5)
            {
                Thread.Sleep(40);
            }
            catch (COMException ex)
            {
                _vm?.ShowStatus("Não foi possível usar a área de transferência: " + ex.Message);
                return false;
            }
        }
    }
}
