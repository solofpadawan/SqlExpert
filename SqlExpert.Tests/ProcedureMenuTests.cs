using System.Text.RegularExpressions;
using SqlExpert.Core.Commands;

namespace SqlExpert.Tests;

/// <summary>
/// "Procedures" folder popup, procedure node popup and the procedure editor menu: reference order and shortcuts,
/// implemented vs greyed (shown disabled, never hidden), and every implemented id has a handler.
/// </summary>
public class ProcedureMenuTests
{
    private static string RepoDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string AppFile(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoDir(), "SqlExpert.App" }.Concat(parts).ToArray()));

    private static readonly ExplorerMenuContext Connected = new(HasProfile: true, IsNodeConnected: true, RegisteredCount: 2);
    private static readonly ExplorerMenuContext Disconnected = new(HasProfile: true, IsNodeConnected: false, RegisteredCount: 2);

    /// <summary>Caption list with "-" for separators, as the popup draws it.</summary>
    private static List<string> Layout(string category)
    {
        var list = new List<string>();
        foreach (var d in CommandCatalog.ByCategory(category))
        {
            if (d.IsSeparatorBefore && list.Count > 0) list.Add("-");
            list.Add(d.Caption + (d.Shortcut is null ? "" : " [" + d.Shortcut + "]") + (d.IsSubmenu ? " >" : ""));
        }
        return list;
    }

    [Fact]
    public void ProceduresFolderPopup_FollowsReferenceOrder()
    {
        Assert.Equal(new[]
        {
            "New Procedure... [Ctrl+N]",
            "-", "Apply block to selected objects...",
            "-", "Autogrant privileges [Shift+Ctrl+A]",
            "-", "Deactivate procedures >", "Activate procedures >",
            "-", "Goto Database >", "Goto Object >",
            "-", "Refresh [F5]",
            "-", "Connect to Database [Shift+Ctrl+C]", "Reconnect", "Disconnect from Database",
            "-", "Register Database [Shift+Alt+R]", "Unregister Database [Shift+Alt+U]", "Clone Registration Info",
            "-", "Database Registration Info...",
            "-", "Recompute selectivity of all indices", "Recompile all stored procedures", "Recompile All Triggers",
            "-", "Show SQL Assistant [Ctrl+A]", "Show objects description",
            "-", "Inspector Page Mode",
            "-", "Hide Disconnected Databases"
        }, Layout("ProceduresPopup"));
    }

    [Fact]
    public void ProcedureNodePopup_Layout()
    {
        Assert.Equal(new[]
        {
            "Edit Procedure...", "New Procedure... [Ctrl+N]", "Drop Procedure",
            "-", "Execute...",
            "-", "Copy name", "Script to clipboard (CREATE)",
            "-", "Apply block to selected objects...", "Autogrant privileges [Shift+Ctrl+A]",
            "-", "Refresh [F5]"
        }, Layout("ProcedurePopup"));
    }

    [Fact]
    public void ProcedureEditorMenu_Layout()
    {
        Assert.Equal(new[]
        {
            "Compile [Ctrl+F9]", "Execute... [F9]",
            "-", "Commit", "Rollback",
            "-", "Edit full text", "Reload from server",
            "-", "Copy name", "Script to clipboard", "Drop Procedure",
            "-", "Debug procedure", "Print", "Save to File"
        }, Layout("ProcedureEditor"));
    }

    [Fact]
    public void MainViewModel_MenuIdLists_MatchCatalogOrder()
    {
        var src = AppFile("ViewModels", "MainViewModel.Procedures.cs");
        List<string> Ids(string field)
        {
            var start = src.IndexOf(field + " =", StringComparison.Ordinal);
            Assert.True(start > 0, field);
            var block = src[start..src.IndexOf("};", start, StringComparison.Ordinal)];
            return Regex.Matches(block, "\"([a-z_.]+)\"").Select(m => m.Groups[1].Value).ToList();
        }
        Assert.Equal(CommandCatalog.ByCategory("ProceduresPopup").Select(d => d.Id), Ids("ProceduresFolderMenuIds"));
        Assert.Equal(CommandCatalog.ByCategory("ProcedurePopup").Select(d => d.Id), Ids("ProcedureNodeMenuIds"));
    }

    private static readonly string[] GreyedFolder =
    {
        "procfolder.apply_block_to_selected_objects", "procfolder.autogrant_privileges", "procfolder.deactivate_procedures",
        "procfolder.activate_procedures", "procfolder.deactivate_all", "procfolder.deactivate_selected", "procfolder.activate_all",
        "procfolder.activate_selected", "procfolder.show_sql_assistant", "procfolder.inspector_page_mode",
        "procnode.apply_block_to_selected_objects", "procnode.autogrant_privileges",
        "proceditor.debug_procedure", "proceditor.print", "proceditor.save_to_file"
    };

    [Fact]
    public void GreyedItems_AreListedButNeverExecute()
    {
        foreach (var id in GreyedFolder)
        {
            Assert.NotNull(CommandCatalog.Find(id));
            Assert.False(CommandAvailability.IsImplemented(id), id);
            Assert.False(CommandAvailability.CanExecuteExplorerItem(id, Connected), id);
            Assert.False(CommandAvailability.CanExecuteProcedureEditorItem(id, new ProcedureEditorContext(true, false, true, true)), id);
        }
    }

    [Fact]
    public void ImplementedItems_HaveHandlers()
    {
        var vm = AppFile("ViewModels", "MainViewModel.Procedures.cs");
        var ids = CommandCatalog.ByCategory("ProceduresPopup").Concat(CommandCatalog.ByCategory("ProcedurePopup"))
            .Select(d => d.Id).Where(CommandAvailability.IsImplemented).ToList();
        Assert.True(ids.Count >= 20);
        foreach (var id in ids)
        {
            if (CommandAvailability.SharedExplorerId(id) is { } shared)
            {
                Assert.True(CommandAvailability.IsImplemented(shared), shared);
                continue;
            }
            // goto_database / goto_object are submenu parents whose rows carry the id.
            Assert.True(vm.Contains($"case \"{id}\":", StringComparison.Ordinal), $"no handler for {id}");
        }

        var editor = AppFile("ViewModels", "ProcedureDocumentViewModel.cs");
        foreach (var id in CommandCatalog.ByCategory("ProcedureEditor").Select(d => d.Id).Where(CommandAvailability.IsImplemented))
            Assert.True(editor.Contains($"[\"{id}\"] =", StringComparison.Ordinal), $"editor menu has no command for {id}");
    }

    [Theory]
    [InlineData("procfolder.new_procedure", true, false)]
    [InlineData("procfolder.refresh", true, false)]
    [InlineData("procfolder.goto_object", true, false)]
    [InlineData("procfolder.recompile_all_stored_procedures", true, false)]
    [InlineData("procfolder.recompile_all_triggers", true, false)]
    [InlineData("procfolder.recompute_selectivity_of_all_indices", true, false)]
    [InlineData("procfolder.connect_to_database", false, true)]
    [InlineData("procfolder.reconnect", true, false)]
    [InlineData("procfolder.register_database", true, true)]
    [InlineData("procfolder.goto_database", true, true)]
    [InlineData("procfolder.show_objects_description", true, true)]
    [InlineData("procnode.edit_procedure", true, false)]
    [InlineData("procnode.drop_procedure", true, false)]
    [InlineData("procnode.execute", true, false)]
    [InlineData("procnode.script_to_clipboard", true, false)]
    [InlineData("procnode.copy_name", true, true)]
    public void EnabledState_FollowsConnection(string id, bool whenConnected, bool whenDisconnected)
    {
        Assert.True(CommandAvailability.IsImplemented(id), id);
        Assert.Equal(whenConnected, CommandAvailability.CanExecuteExplorerItem(id, Connected));
        Assert.Equal(whenDisconnected, CommandAvailability.CanExecuteExplorerItem(id, Disconnected));
    }

    [Theory]
    // connected, busy, inTransaction, exists
    [InlineData("proceditor.compile", true, false, false, false, true)]
    [InlineData("proceditor.compile", false, false, false, true, false)]
    [InlineData("proceditor.compile", true, true, false, true, false)]
    [InlineData("proceditor.execute", true, false, false, false, false)]
    [InlineData("proceditor.execute", true, false, false, true, true)]
    [InlineData("proceditor.commit", true, false, false, true, false)]
    [InlineData("proceditor.commit", true, false, true, true, true)]
    [InlineData("proceditor.rollback", true, false, true, false, true)]
    [InlineData("proceditor.drop_procedure", true, false, false, false, false)]
    [InlineData("proceditor.edit_full_text", false, false, false, false, true)]
    [InlineData("proceditor.copy_name", false, false, false, false, true)]
    public void EditorCommands_FollowEditorState(string id, bool connected, bool busy, bool tran, bool exists, bool expected) =>
        Assert.Equal(expected, CommandAvailability.CanExecuteProcedureEditorItem(id, new ProcedureEditorContext(connected, busy, tran, exists)));

    [Theory]
    [InlineData("New Procedure")]
    [InlineData("Recompile all stored procedures")]
    [InlineData("Recompile All Triggers")]
    [InlineData("Recompute selectivity of all indices")]
    public void DatabaseMenuEntries_AreImplemented(string caption)
    {
        Assert.True(CommandAvailability.IsMenuActionImplemented(caption));
        Assert.Contains($"case \"{caption}\":", AppFile("ViewModels", "MainViewModel.Procedures.cs"));
    }

    [Fact]
    public void EditorView_ShowsUnimplementedTabsDisabled()
    {
        var xaml = AppFile("Views", "ProcedureEditorView.xaml");
        foreach (var tab in new[] { "Operations/Index Using", "Plan Analyzer", "Comparison", "To-do" })
            Assert.Matches($"<TabItem Header=\"{Regex.Escape(tab)}\" Style=\"{{StaticResource ProcDisabledTab}}\"", xaml);
        foreach (var tab in new[] { "Description", "Dependencies", "DDL", "Grants", "Version History" })
            Assert.Contains($"<TabItem Header=\"{tab}\">", xaml);
        foreach (var sub in new[] { "Input Parameters", "Output Parameters", "Variables", "Cursors" })
            Assert.Contains($"<TabItem Header=\"{sub}\">", xaml);
        // Print is visible but bound to the always-disabled command.
        Assert.Matches("ToolTip=\"Print[^\"]*\" Command=\"\\{Binding DisabledCommand\\}\"><views:IconImage Icon=\"\\{StaticResource Icon.Print\\}\"", xaml);
    }

    [Fact]
    public void ExplorerPopup_UsesProcedureMenus()
    {
        var vm = AppFile("ViewModels", "MainViewModel.cs");
        Assert.Contains("BuildProcedureMenu(node) ?? OpenMenuItems.ToList()", vm);
        var window = AppFile("MainWindow.xaml.cs");
        Assert.Contains("RefreshProceduresShortcut()", window);
        Assert.Contains("NewProcedureShortcut()", window);
    }
}
