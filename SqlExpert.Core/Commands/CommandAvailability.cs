namespace SqlExpert.Core.Commands;

/// <summary>
/// Central list of commands that have a real handler. Everything else is disabled (greyed out) in menus,
/// toolbars and context menus, so the UI never shows a button that does nothing.
/// Add an id here only together with its handler (MainViewModel.OnCommandExecuted / HandleMenuAction).
/// </summary>
public static class CommandAvailability
{
    /// <summary>Catalog ids (<see cref="CommandCatalog"/>) handled by MainViewModel.OnCommandExecuted.</summary>
    public static IReadOnlySet<string> ImplementedIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "database.register_database",
        "database.unregister_database",
        "database.connect_to_database",
        "database.disconnect_from_database",
        "database.reconnect",
        "database.database_registration_info",
        "openmenu.database_registration_info",
        "database.exit",
        "view.db_explorer",
        "view.database_explorer",
        "view.status_bar",
        "view.windows_bar",
        "windows.close_all",
        "windows.close_all_except_this",
        "windowsmenu.close_all",
        "fields.new_field",
        "fields.edit_field",
        "fields.drop_field",
        "fields.reorder_fields",
        "fields.copy_fields_list_to_clipboard",
        "tools.sql_editor",
        "tools.new_sql_editor",
        "help.about",
        // DB Explorer popup (registered database node)
        "dbexplorer.sort_nodes",
        "dbexplorer.sort_by_alias",
        "dbexplorer.sort_as_registered",
        "dbexplorer.goto_database",
        "dbexplorer.refresh",
        "dbexplorer.connect_to_database",
        "dbexplorer.reconnect",
        "dbexplorer.disconnect_from_database",
        "dbexplorer.register_database",
        "dbexplorer.unregister_database",
        "dbexplorer.clone_registration_info",
        "dbexplorer.database_registration_info",
        "dbexplorer.show_objects_description",
        "dbexplorer.hide_disconnected_databases",
        // Main menu Database (handled by MainViewModel.OnCommandExecuted / HandleMenuAction)
        "database.new_procedure",
        "database.recompile_all_stored_procedures",
        "database.recompile_all_triggers",
        "database.recompute_selectivity_of_all_indices",
        // "Procedures" folder popup
        "procfolder.new_procedure",
        "procfolder.goto_database",
        "procfolder.goto_object",
        "procfolder.refresh",
        "procfolder.connect_to_database",
        "procfolder.reconnect",
        "procfolder.disconnect_from_database",
        "procfolder.register_database",
        "procfolder.unregister_database",
        "procfolder.clone_registration_info",
        "procfolder.database_registration_info",
        "procfolder.recompute_selectivity_of_all_indices",
        "procfolder.recompile_all_stored_procedures",
        "procfolder.recompile_all_triggers",
        "procfolder.show_objects_description",
        "procfolder.hide_disconnected_databases",
        // Popup of one procedure
        "procnode.edit_procedure",
        "procnode.new_procedure",
        "procnode.drop_procedure",
        "procnode.execute",
        "procnode.copy_name",
        "procnode.script_to_clipboard",
        "procnode.refresh",
        // Procedure editor
        "proceditor.compile",
        "proceditor.execute",
        "proceditor.commit",
        "proceditor.rollback",
        "proceditor.edit_full_text",
        "proceditor.reload",
        "proceditor.copy_name",
        "proceditor.script_to_clipboard",
        "proceditor.drop_procedure",
    };

    /// <summary>
    /// SQL Editor pages, panels and editor features that are not menu commands (no <see cref="CommandCatalog"/> entry).
    /// Plan Analyzer, Performance Analysis and Logs are not listed, so their tabs stay greyed.
    /// </summary>
    public static IReadOnlySet<string> SqlEditorFeatureIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sqleditor.page.edit",
        "sqleditor.page.results",
        "sqleditor.page.history",
        "sqleditor.panel.messages",
        "sqleditor.panel.query_columns",
        "sqleditor.code_completion",
        "sqleditor.object_links",
    };

    /// <summary>Upper page tabs of the SQL Editor in classic order (id, caption).</summary>
    public static IReadOnlyList<(string Id, string Caption)> SqlEditorPages { get; } = new[]
    {
        ("sqleditor.page.edit", "Edit"),
        ("sqleditor.page.results", "Results"),
        ("sqleditor.page.history", "History"),
        ("sqleditor.page.plan_analyzer", "Plan Analyzer"),
        ("sqleditor.page.performance_analysis", "Performance Analysis"),
        ("sqleditor.page.logs", "Logs"),
    };

    /// <summary>Main-menu captions (CommandParameter of the Stub commands) handled by MainViewModel.HandleMenuAction.</summary>
    public static IReadOnlySet<string> ImplementedMenuActions { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Register Database",
        "Register Database...",
        "Unregister Database",
        "Connect to Database",
        "Disconnect from Database",
        "Reconnect",
        "Database Registration Info...",
        "Close All",
        "Exit",
        "SQL Editor",
        "New SQL Editor",
        "About",
        "New Procedure",
        "Recompile all stored procedures",
        "Recompile All Triggers",
        "Recompute selectivity of all indices",
    };

    /// <summary>
    /// Edit-menu / Edit-toolbar captions handled by the active SQL Editor. Enabled only while a SQL Editor
    /// is the active document (see <see cref="IsMenuActionEnabled"/>).
    /// </summary>
    public static IReadOnlySet<string> SqlEditorMenuActions { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Load from File",
        "Load from File: SQL Script",
        "Load from File: Text",
        "Save to File",
        "Save to File: SQL Script",
        "Save to File: Text",
        "Save as...",
        "Find",
        "Search Again",
        "Replace",
    };

    public static bool IsImplemented(string id) => ImplementedIds.Contains(id) || SqlEditorFeatureIds.Contains(id);

    public static bool IsMenuActionImplemented(string? caption) =>
        caption is not null && ImplementedMenuActions.Contains(caption);

    /// <summary>Enabled state of a main-menu caption: implemented globally, or a SQL Editor action while one is active.</summary>
    public static bool IsMenuActionEnabled(string? caption, bool sqlEditorActive) =>
        IsMenuActionImplemented(caption) || (sqlEditorActive && caption is not null && SqlEditorMenuActions.Contains(caption));

    /// <summary>
    /// Enabled state of an item of the DB Explorer popup (right-click on a registered database).
    /// Unimplemented items stay grey; the others follow the connection state of the node under the mouse.
    /// </summary>
    public static bool CanExecuteExplorerItem(string id, ExplorerMenuContext ctx)
    {
        if (!IsImplemented(id)) return false;
        // Folder / object popups share the registration items of the database popup.
        var shared = SharedExplorerId(id);
        if (shared is not null) return CanExecuteExplorerItem(shared, ctx);
        if (id.StartsWith("procfolder.", StringComparison.OrdinalIgnoreCase) || id.StartsWith("procnode.", StringComparison.OrdinalIgnoreCase))
            return id is "procnode.copy_name" || ctx.IsNodeConnected;
        return id switch
        {
            "dbexplorer.refresh" => ctx.IsNodeConnected,
            "dbexplorer.connect_to_database" => ctx.HasProfile && !ctx.IsNodeConnected,
            "dbexplorer.reconnect" => ctx.IsNodeConnected,
            "dbexplorer.disconnect_from_database" => ctx.IsNodeConnected,
            "dbexplorer.unregister_database" => ctx.HasProfile,
            "dbexplorer.clone_registration_info" => ctx.HasProfile,
            "dbexplorer.database_registration_info" => ctx.HasProfile,
            "dbexplorer.goto_database" => ctx.RegisteredCount > 0,
            _ => true
        };
    }

    /// <summary>Registration items of the folder popups that behave like the database popup ("procfolder.reconnect" → "dbexplorer.reconnect").</summary>
    private static readonly string[] SharedSuffixes =
    {
        "goto_database", "connect_to_database", "reconnect", "disconnect_from_database", "register_database", "unregister_database",
        "clone_registration_info", "database_registration_info", "show_objects_description", "hide_disconnected_databases"
    };

    /// <summary>The "dbexplorer.*" id a folder-popup item mirrors, or null.</summary>
    public static string? SharedExplorerId(string id)
    {
        if (!id.StartsWith("procfolder.", StringComparison.OrdinalIgnoreCase)) return null;
        var suffix = id["procfolder.".Length..];
        return SharedSuffixes.Contains(suffix, StringComparer.OrdinalIgnoreCase) ? "dbexplorer." + suffix : null;
    }

    /// <summary>
    /// Procedure editor actions: implemented ones follow the editor state (connected, busy, open transaction);
    /// greyed ones (debugger, print…) never run.
    /// </summary>
    public static bool CanExecuteProcedureEditorItem(string id, ProcedureEditorContext ctx)
    {
        if (!IsImplemented(id)) return false;
        return id switch
        {
            "proceditor.compile" => ctx.IsConnected && !ctx.IsBusy,
            "proceditor.execute" => ctx.IsConnected && !ctx.IsBusy && ctx.Exists,
            "proceditor.commit" or "proceditor.rollback" => ctx.IsConnected && !ctx.IsBusy && ctx.InTransaction,
            "proceditor.reload" => ctx.IsConnected && !ctx.IsBusy && ctx.Exists,
            "proceditor.drop_procedure" => ctx.IsConnected && !ctx.IsBusy && ctx.Exists,
            "proceditor.edit_full_text" => !ctx.IsBusy,
            _ => true
        };
    }

    /// <summary>CanExecute of a catalog command: false when it has no handler, otherwise its Connected/selection rule.</summary>
    public static bool CanExecute(CommandDefinition def, bool isConnected, bool hasSelection)
    {
        if (!IsImplemented(def.Id)) return false;
        return def.EnabledWhen switch
        {
            CommandEnabledWhen.Always => true,
            CommandEnabledWhen.Connected => isConnected,
            CommandEnabledWhen.ConnectedWithSelection => isConnected && hasSelection,
            CommandEnabledWhen.Never => false,
            _ => true
        };
    }
}

/// <summary>State the DB Explorer popup depends on.</summary>
/// <param name="HasProfile">A registered database node is selected.</param>
/// <param name="IsNodeConnected">That registration is the connected one.</param>
/// <param name="RegisteredCount">Number of registered databases.</param>
public readonly record struct ExplorerMenuContext(bool HasProfile, bool IsNodeConnected, int RegisteredCount);

/// <summary>State the procedure editor actions depend on.</summary>
/// <param name="IsConnected">The editor's registration is connected.</param>
/// <param name="IsBusy">A compile/execute/load is running.</param>
/// <param name="InTransaction">The editor's session holds an open transaction.</param>
/// <param name="Exists">The procedure exists on the server (false for a new, not yet compiled procedure).</param>
public readonly record struct ProcedureEditorContext(bool IsConnected, bool IsBusy, bool InTransaction, bool Exists);
