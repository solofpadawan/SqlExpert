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

    public static bool IsImplemented(string id) => ImplementedIds.Contains(id);

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
