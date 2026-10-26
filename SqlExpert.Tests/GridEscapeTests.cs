using SqlExpert.Core.DataForms;

namespace SqlExpert.Tests;

/// <summary>
/// Esc in the data grids (Data tab, editable SQL results) and the form view: the cell edit is cancelled first,
/// then the pending changes are discarded like the Cancel button; with nothing pending Esc does nothing. No GUI.
/// </summary>
public class GridEscapeTests
{
    [Fact]
    public void CellInEditMode_PassesThrough_SoTheEditorCancelsTheCell()
    {
        Assert.Equal(GridEscapeAction.PassThrough, GridEscape.Decide(cellEditing: true, popupOpen: false, hasPending: true));
        Assert.Equal(GridEscapeAction.PassThrough, GridEscape.Decide(cellEditing: true, popupOpen: false, hasPending: false));
    }

    [Fact]
    public void OpenPopup_PassesThrough_SoThePopupClosesItself()
    {
        Assert.Equal(GridEscapeAction.PassThrough, GridEscape.Decide(cellEditing: false, popupOpen: true, hasPending: true));
        Assert.Equal(GridEscapeAction.PassThrough, GridEscape.Decide(cellEditing: false, popupOpen: true, hasPending: false));
    }

    [Fact]
    public void NotEditing_WithPendingChanges_RunsCancel()
    {
        Assert.Equal(GridEscapeAction.CancelChanges, GridEscape.Decide(cellEditing: false, popupOpen: false, hasPending: true));
    }

    [Fact]
    public void NothingPending_IsSwallowed_NeverClosesTheWindowOrTab()
    {
        Assert.Equal(GridEscapeAction.Swallow, GridEscape.Decide(cellEditing: false, popupOpen: false, hasPending: false));
    }

    [Fact]
    public void OnlyPlainEscape_IsHandled()
    {
        Assert.True(GridEscape.IsPlainEscape(true, false));
        Assert.False(GridEscape.IsPlainEscape(true, true));
        Assert.False(GridEscape.IsPlainEscape(false, false));
    }

    // ===== wiring (source) =====

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Src(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray())).Replace("\r\n", "\n");

    [Fact]
    public void Grid_EscapeRunsTheCancelCommand_AfterTheCellEdit()
    {
        var grid = Src("SqlExpert.App", "Views", "TableDataGrid.xaml.cs");
        var handler = grid[grid.IndexOf("private void Grid_OnPreviewKeyDown", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("internal static bool IsSetNullGesture", StringComparison.Ordinal)];
        Assert.Contains("HandleEscape(e, editing || IsCurrentCellEditing(), Grid.CurrentItem as DataRowView);", handler);
        Assert.Contains("GridEscape.Decide(cellEditing, popupOpen, _vm.CanDiscardOnEscape(current))", handler);
        Assert.Contains("_vm.CancelCommand.Execute(null);", handler);
        // popups / context menu keep their own Esc
        Assert.Contains("_lookup is { IsOpen: true }", handler);
        Assert.Contains("_calendar is { IsOpen: true }", handler);
        Assert.Contains("_calculator is { IsOpen: true }", handler);
        Assert.Contains("Grid.ContextMenu is { IsOpen: true }", handler);
        Assert.Contains("!IsAncestorOf(source)", handler);
        Assert.Contains("case GridEscapeAction.Swallow:", handler);
    }

    [Fact]
    public void Form_DirtyFieldRevertsFirst_ThenEscapeCancels()
    {
        var form = Src("SqlExpert.App", "Views", "TableDataGrid.Form.cs");
        var handler = form[form.IndexOf("private void Form_OnPreviewKeyDown", StringComparison.Ordinal)..];
        Assert.Contains("GetBindingExpression(TextBox.TextProperty) is { IsDirty: true }", handler);
        Assert.Contains("HandleEscape(e, fieldEditing, FormRow);", handler);
        // the field's own Esc (revert the typed text) is still there
        Assert.Contains("else if (e.Key == Key.Escape)", form);
    }

    [Fact]
    public void ViewModel_DiscardOnEscape_CoversPendingChangesAppendedRowAndOpenEdit()
    {
        var vm = Src("SqlExpert.App", "ViewModels", "TableDataViewModel.cs");
        var body = vm[vm.IndexOf("public bool CanDiscardOnEscape", StringComparison.Ordinal)..];
        body = body[..body.IndexOf(';')];
        Assert.Contains("CancelCommand.CanExecute(null)", body);
        Assert.Contains("HasPendingChanges", body);
        Assert.Contains("_appendedRow is { RowState: not DataRowState.Detached }", body);
        Assert.Contains("{ IsEdit: true } or { IsNew: true }", body);
        // Cancel removes unposted new rows
        Assert.Contains("_table.RejectChanges();", vm);
    }
}
