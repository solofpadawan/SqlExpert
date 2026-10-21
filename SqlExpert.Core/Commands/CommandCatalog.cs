namespace SqlExpert.Core.Commands;

/// <summary>Enabled-when rule for a command.</summary>
public enum CommandEnabledWhen
{
    Always,
    Connected,
    ConnectedWithSelection,
    Never
}

public sealed record CommandDefinition(
    string Id,
    string Category,
    string Caption,
    string? Shortcut,
    CommandEnabledWhen EnabledWhen,
    bool IsSeparatorBefore = false,
    string? SqlServerMapping = null,
    bool IsSubmenu = false);

/// <summary>
/// Single source of truth for SqlExpert commands (command catalog mapped from the reference UX).
/// Menus, toolbars and context menus bind by <see cref="CommandDefinition.Id"/>.
/// </summary>
public static class CommandCatalog
{
    public static IReadOnlyList<CommandDefinition> All { get; } = Build();

    public static CommandDefinition? Find(string id) =>
        All.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<CommandDefinition> ByCategory(string category) =>
        All.Where(c => c.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

    private static List<CommandDefinition> Build()
    {
        var list = new List<CommandDefinition>();
        // --- generated from the UX command map ---
        // Category: Default
        list.Add(new CommandDefinition("default.find_object_in_explorer_tree", "Default", "Find object in explorer tree", null, CommandEnabledWhen.Always, false, null, false));
        // Category: Database
        list.Add(new CommandDefinition("database.register_database", "Database", "Register Database...", "Shift+Alt+R", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("database.unregister_database", "Database", "Unregister Database", "Shift+Alt+U", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("database.connect_to_database", "Database", "Connect to Database", "Shift+Ctrl+C", CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("database.disconnect_from_database", "Database", "Disconnect from Database", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("database.create_database", "Database", "Create Database...", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("database.drop_database", "Database", "Drop Database", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("database.new_domain", "Database", "New Domain", null, CommandEnabledWhen.Connected, true, @"CREATE TYPE (UDT)", false));
        list.Add(new CommandDefinition("database.new_table", "Database", "New Table", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("database.new_view", "Database", "New View", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("database.new_procedure", "Database", "New Procedure", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("database.new_trigger", "Database", "New Trigger", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("database.new_generator", "Database", "New Generator", null, CommandEnabledWhen.Connected, false, @"CREATE SEQUENCE", false));
        list.Add(new CommandDefinition("database.new_exception", "Database", "New Exception", null, CommandEnabledWhen.Connected, false, @"sp_addmessage / THROW", false));
        list.Add(new CommandDefinition("database.new_udf", "Database", "New UDF", null, CommandEnabledWhen.Connected, false, @"CREATE FUNCTION", false));
        list.Add(new CommandDefinition("database.new_role", "Database", "New Role", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("database.recompute_selectivity_of_all_indices", "Database", "Recompute selectivity of all indices", null, CommandEnabledWhen.Connected, true, @"UPDATE STATISTICS / sp_updatestats", false));
        list.Add(new CommandDefinition("database.recompile_all_stored_procedures", "Database", "Recompile all stored procedures", null, CommandEnabledWhen.Connected, false, @"sp_recompile", false));
        list.Add(new CommandDefinition("database.reconnect", "Database", "Reconnect", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("database.database_registration_info", "Database", "Database Registration Info...", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("database.recreate_all_triggers", "Database", "Recreate All Triggers", null, CommandEnabledWhen.Connected, true, @"DROP+CREATE or ALTER; ENABLE/DISABLE TRIGGER", false));
        list.Add(new CommandDefinition("database.show_objects_description", "Database", "Show objects description", null, CommandEnabledWhen.Always, false, null, false));
        // Category: View
        list.Add(new CommandDefinition("view.db_explorer", "View", "DB Explorer", "F11", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("view.main_toolbars", "View", "Main Toolbars >", null, CommandEnabledWhen.Always, true, null, true));
        list.Add(new CommandDefinition("view.status_bar", "View", "Status bar", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("view.toolbars", "View", "Toolbars >", null, CommandEnabledWhen.Always, false, null, true));
        list.Add(new CommandDefinition("view.database_explorer", "View", "Database Explorer", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("view.windows_bar", "View", "Windows Bar", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("view.database_objects", "View", "Database Objects", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("view.autohide_db_explorer", "View", "Autohide DB Explorer", null, CommandEnabledWhen.Always, false, null, false));
        // Category: Tools
        list.Add(new CommandDefinition("tools.sql_editor", "Tools", "SQL Editor", "F12", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("tools.new_sql_editor", "Tools", "New SQL Editor", "Shift+F12", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("tools.sql_monitor", "Tools", "SQL Monitor", "Ctrl+M", CommandEnabledWhen.Connected, false, @"Extended Events / Profiler-like", false));
        list.Add(new CommandDefinition("tools.search_in_metadata", "Tools", "Search in metadata", "Shift+Alt+F", CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("tools.extract_metadata", "Tools", "Extract Metadata", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("tools.print_metadata", "Tools", "Print Metadata", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("tools.dependencies_viewer", "Tools", "Dependencies Viewer", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("tools.sp_triggers_views_analyzer", "Tools", "SP/Triggers/Views Analyzer", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("tools.database_comparer", "Tools", "Database Comparer", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("tools.log_manager", "Tools", "Log Manager", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("tools.table_data_comparer", "Tools", "Table Data Comparer", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("tools.data_analysis", "Tools", "Data Analysis", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("tools.olap", "Tools", "OLAP", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("tools.copy_database_object", "Tools", "Copy database object", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("tools.odbc_viewer", "Tools", "ODBC Viewer", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("tools.to_do_list", "Tools", "To-do list", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("tools.import_data", "Tools", "Import data", null, CommandEnabledWhen.Connected, false, null, false));
        // Category: Options
        list.Add(new CommandDefinition("options.environment_options", "Options", "Environment Options...", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("options.editor_options", "Options", "Editor Options...", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("options.keyboard_templates", "Options", "Keyboard Templates...", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("options.general_templates", "Options", "General Templates...", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("options.visual_options", "Options", "Visual Options...", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("options.object_editor_options", "Options", "Object Editor Options...", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("options.code_formatting_options", "Options", "Code formatting options...", null, CommandEnabledWhen.Always, false, null, false));
        // Category: Services
        list.Add(new CommandDefinition("services.backup_database", "Services", "Backup Database", null, CommandEnabledWhen.Connected, false, @"BACKUP DATABASE", false));
        list.Add(new CommandDefinition("services.restore_database", "Services", "Restore Database", null, CommandEnabledWhen.Always, false, @"RESTORE DATABASE", false));
        list.Add(new CommandDefinition("services.server_properties_log", "Services", "Server Properties/Log", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("services.database_validation", "Services", "Database Validation", null, CommandEnabledWhen.Connected, false, @"DBCC CHECKDB", false));
        list.Add(new CommandDefinition("services.database_statistics", "Services", "Database Statistics", null, CommandEnabledWhen.Connected, false, @"sys.dm_db_index_physical_stats / UPDATE STATISTICS", false));
        list.Add(new CommandDefinition("services.database_properties", "Services", "Database Properties", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("services.database_online", "Services", "Database Online", null, CommandEnabledWhen.Connected, true, @"ALTER DATABASE SET ONLINE", false));
        list.Add(new CommandDefinition("services.communication_diagnostics", "Services", "Communication Diagnostics", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("services.database_monitoring", "Services", "Database Monitoring", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("services.sqlexpert_services_control_center", "Services", "SqlExpert Services Control Center", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("services.trace_and_audit", "Services", "Trace and audit", null, CommandEnabledWhen.Connected, false, @"Extended Events / SQL Audit", false));
        list.Add(new CommandDefinition("services.benchmark", "Services", "Benchmark", null, CommandEnabledWhen.Connected, false, null, false));
        // Category: Windows
        list.Add(new CommandDefinition("windows.windows_manager", "Windows", "Windows Manager", "Alt+0", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("windows.close_all", "Windows", "Close All", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("windows.cascade", "Windows", "Cascade", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("windows.tile_horizontally", "Windows", "Tile Horizontally", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("windows.tile_vertically", "Windows", "Tile Vertically", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("windows.minimize_all", "Windows", "Minimize All", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("windows.arrange", "Windows", "Arrange", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("windows.help", "Windows", "Help", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("windows.close", "Windows", "Close", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("windows.minimize", "Windows", "Minimize", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("windows.restore", "Windows", "Restore", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("windows.active_title", "Windows", "Active Title", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("windows.floating_windows_manager", "Windows", "Floating Windows Manager", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("windows.close_all_except_this", "Windows", "Close all except this", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("windows.find_in_the_database_explorer", "Windows", "Find in the Database Explorer", null, CommandEnabledWhen.Always, true, null, false));
        // Category: Help
        list.Add(new CommandDefinition("help.what_s_new", "Help", "What's new?", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("help.online_documentation", "Help", "Online documentation", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("help.additional_help_files", "Help", "Additional Help Files >", null, CommandEnabledWhen.Always, false, null, true));
        list.Add(new CommandDefinition("help.product_home_page", "Help", "Product Home Page", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("help.send_bug_reports_to", "Help", "Send bug reports to", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("help.about", "Help", "About", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("help.download_sql_server_tools", "Help", "Download SQL Server tools", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("help.sqlexpert_direct", "Help", "SqlExpert Direct...", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("help.learning_center", "Help", "Learning Center", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("help.support", "Help", "Support", null, CommandEnabledWhen.Always, false, null, false));
        // Category: Menus
        list.Add(new CommandDefinition("menus.database", "Menus", "Database >", null, CommandEnabledWhen.Always, false, null, true));
        list.Add(new CommandDefinition("menus.view", "Menus", "View >", null, CommandEnabledWhen.Always, false, null, true));
        list.Add(new CommandDefinition("menus.tools", "Menus", "Tools >", null, CommandEnabledWhen.Always, false, null, true));
        list.Add(new CommandDefinition("menus.services", "Menus", "Services >", null, CommandEnabledWhen.Always, false, null, true));
        list.Add(new CommandDefinition("menus.windows", "Menus", "Windows >", null, CommandEnabledWhen.Always, false, null, true));
        list.Add(new CommandDefinition("menus.help", "Menus", "Help >", null, CommandEnabledWhen.Always, false, null, true));
        list.Add(new CommandDefinition("menus.plugins", "Menus", "Plugins >", null, CommandEnabledWhen.Always, false, null, true));
        list.Add(new CommandDefinition("menus.edit", "Menus", "Edit >", null, CommandEnabledWhen.Always, false, null, true));
        list.Add(new CommandDefinition("menus.grid", "Menus", "Grid >", null, CommandEnabledWhen.Always, false, null, true));
        // Category: OpenMenu
        list.Add(new CommandDefinition("openmenu.new_object", "OpenMenu", "New Object", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("openmenu.edit_object", "OpenMenu", "Edit Object", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("openmenu.drop_object", "OpenMenu", "Drop Object", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("openmenu.create_siud_procedures", "OpenMenu", "Create SIUD procedures", null, CommandEnabledWhen.Connected, true, @"Generate SELECT/INSERT/UPDATE/DELETE procedures", false));
        list.Add(new CommandDefinition("openmenu.autogrant_privileges", "OpenMenu", "Autogrant privileges", null, CommandEnabledWhen.Connected, false, @"GRANT defaults to roles/users", false));
        list.Add(new CommandDefinition("openmenu.goto_database", "OpenMenu", "Goto Database", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("openmenu.database_registration_info", "OpenMenu", "Database Registration Info...", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("openmenu.show_toolbar", "OpenMenu", "Show Toolbar", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("openmenu.sql_assistant", "OpenMenu", "SQL Assistant", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("openmenu.inspector_page_mode", "OpenMenu", "Inspector Page Mode", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("openmenu.hide_disconnected_databases", "OpenMenu", "Hide Disconnected Databases", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("openmenu.show_objects_description", "OpenMenu", "Show objects description", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("openmenu.deactivate_all_triggers", "OpenMenu", "Deactivate All Triggers", null, CommandEnabledWhen.ConnectedWithSelection, true, @"DISABLE TRIGGER ALL ON <table> / database", false));
        list.Add(new CommandDefinition("openmenu.activate_all_stored_procedures", "OpenMenu", "Activate All Stored Procedures", null, CommandEnabledWhen.Connected, false, @"Re-enable procedures", false));
        list.Add(new CommandDefinition("openmenu.deactivate_all_stored_procedures", "OpenMenu", "Deactivate All Stored Procedures", null, CommandEnabledWhen.Connected, false, @"ALTER PROCEDURE WITH RECOMPILE / rename pattern — prefer flag table", false));
        list.Add(new CommandDefinition("openmenu.run_procedure", "OpenMenu", "Run Procedure...", null, CommandEnabledWhen.ConnectedWithSelection, true, null, false));
        list.Add(new CommandDefinition("openmenu.deactivate_procedures", "OpenMenu", "Deactivate procedures >", null, CommandEnabledWhen.Connected, false, null, true));
        list.Add(new CommandDefinition("openmenu.activate_procedures", "OpenMenu", "Activate procedures >", null, CommandEnabledWhen.Connected, false, null, true));
        list.Add(new CommandDefinition("openmenu.activate_selected_only", "OpenMenu", "Activate selected only", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("openmenu.deactivate_selected_only", "OpenMenu", "Deactivate selected only", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("openmenu.activate_triggers", "OpenMenu", "Activate triggers >", null, CommandEnabledWhen.Connected, false, null, true));
        list.Add(new CommandDefinition("openmenu.deactivate_triggers", "OpenMenu", "Deactivate triggers >", null, CommandEnabledWhen.Connected, false, null, true));
        list.Add(new CommandDefinition("openmenu.new_block", "OpenMenu", "New block", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("openmenu.copy_object", "OpenMenu", "Copy object...", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("openmenu.apply_block_to_selected_object", "OpenMenu", "Apply block to selected object(s)...", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("openmenu.show_data", "OpenMenu", "Show data...", null, CommandEnabledWhen.Connected, true, @"SELECT TOP n * FROM", false));
        list.Add(new CommandDefinition("openmenu.get_statistics_for_selected_table", "OpenMenu", "Get statistics for selected table(s)", null, CommandEnabledWhen.Connected, false, @"sys.dm_db_partition_stats / sys.dm_db_index_usage_stats", false));
        // Category: ProjectPopup
        list.Add(new CommandDefinition("projectpopup.new_folder", "ProjectPopup", "New Folder", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("projectpopup.add_object", "ProjectPopup", "Add Object...", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("projectpopup.drop_object", "ProjectPopup", "Drop Object", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("projectpopup.create_user_item_from_clipboard", "ProjectPopup", "Create User Item from Clipboard...", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("projectpopup.copy_user_item_to_clipboard", "ProjectPopup", "Copy User Item to Clipboard", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("projectpopup.apply_block_to_selected_objects", "ProjectPopup", "Apply block to selected objects...", null, CommandEnabledWhen.Connected, true, null, false));
        // Category: RecentMenu
        list.Add(new CommandDefinition("recentmenu.delete_object", "RecentMenu", "Delete object", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("recentmenu.clear_recent_list", "RecentMenu", "Clear recent list", null, CommandEnabledWhen.Always, false, null, false));
        // Category: WindowsMenu
        list.Add(new CommandDefinition("windowsmenu.close_all", "WindowsMenu", "Close All", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("windowsmenu.close_all_database_windows", "WindowsMenu", "Close all Database Windows", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("windowsmenu.minimize_all_database_windows", "WindowsMenu", "Minimize all Database Windows", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("windowsmenu.restore_all_database_windows", "WindowsMenu", "Restore all Database Windows", null, CommandEnabledWhen.Always, false, null, false));
        // Category: Editors actions
        list.Add(new CommandDefinition("editors_actions.toggle_bookmarks", "Editors actions", "Toggle Bookmarks >", null, CommandEnabledWhen.Always, false, null, true));
        list.Add(new CommandDefinition("editors_actions.goto_bookmarks", "Editors actions", "Goto Bookmarks >", null, CommandEnabledWhen.Always, false, null, true));
        list.Add(new CommandDefinition("editors_actions.cut", "Editors actions", "Cut", "Ctrl+X", CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("editors_actions.copy", "Editors actions", "Copy", "Ctrl+C", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("editors_actions.paste", "Editors actions", "Paste", "Ctrl+V", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("editors_actions.select_all", "Editors actions", "Select All", "Ctrl+A", CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("editors_actions.print_preview", "Editors actions", "Print preview", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("editors_actions.page_setup", "Editors actions", "Page setup", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("editors_actions.quick_load", "Editors actions", "Quick Load", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("editors_actions.quick_save", "Editors actions", "Quick Save", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("editors_actions.save_to_file", "Editors actions", "Save to File", "Ctrl+S", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("editors_actions.uncomment_selected", "Editors actions", "Uncomment Selected", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("editors_actions.convert_charcase", "Editors actions", "Convert charcase", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("editors_actions.convert_to_uppercase", "Editors actions", "Convert to uppercase", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("editors_actions.convert_to_lowercase", "Editors actions", "Convert to lowercase", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("editors_actions.convert_to_namecase", "Editors actions", "Convert to namecase", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("editors_actions.invert_case", "Editors actions", "Invert case", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("editors_actions.toggle_case", "Editors actions", "Toggle case", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("editors_actions.copy_text_as_rtf", "Editors actions", "Copy Text as RTF", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("editors_actions.save_as", "Editors actions", "Save as...", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("editors_actions.convert_from_utf8", "Editors actions", "Convert FROM UTF8", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("editors_actions.convert_to_utf8", "Editors actions", "Convert TO UTF8", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("editors_actions.format_selected_text", "Editors actions", "Format selected text", null, CommandEnabledWhen.Always, true, null, false));
        // Category: Insp
        list.Add(new CommandDefinition("insp.1_1", "Insp", "1: 1", null, CommandEnabledWhen.Always, false, null, false));
        // Category: Data Grids
        list.Add(new CommandDefinition("data_grids.copy_records_to_clipboard", "Data Grids", "Copy records to clipboard", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("data_grids.copy_selected_record_to_clipboard_as_insert", "Data Grids", "Copy selected record(s) to clipboard as INSERT", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("data_grids.adjust_column_widths", "Data Grids", "Adjust column widths", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("data_grids.cut_cell_value", "Data Grids", "Cut cell value", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("data_grids.copy_cell_value", "Data Grids", "Copy cell value", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("data_grids.paste_cell_value", "Data Grids", "Paste cell value", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("data_grids.reorder_grid_columns", "Data Grids", "Reorder grid columns", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("data_grids.incremental_search", "Data Grids", "Incremental Search", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("data_grids.apply_filter", "Data Grids", "Apply Filter", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("data_grids.show_filter_panel", "Data Grids", "Show Filter Panel", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("data_grids.quick_add_filter_criteria", "Data Grids", "Quick Add Filter Criteria", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("data_grids.copy_selected_record_as_update", "Data Grids", "Copy selected record(s) as UPDATE", null, CommandEnabledWhen.Always, true, null, false));
        // Category: Indices
        list.Add(new CommandDefinition("indices.new_index", "Indices", "New index", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("indices.drop_index", "Indices", "Drop index", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("indices.copy_index_name", "Indices", "Copy index name", null, CommandEnabledWhen.Always, true, null, false));
        // Category: Recent Load Save
        list.Add(new CommandDefinition("recent_load_save.new_itemx2", "Recent Load Save", "New Itemx2", null, CommandEnabledWhen.ConnectedWithSelection, false, null, false));
        // Category: Constraints
        list.Add(new CommandDefinition("constraints.drop_constraint", "Constraints", "Drop constraint", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("constraints.new_constraint", "Constraints", "New constraint", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("constraints.autowidth", "Constraints", "Autowidth", null, CommandEnabledWhen.Connected, true, null, false));
        // Category: Dependencies
        list.Add(new CommandDefinition("dependencies.expand_all", "Dependencies", "Expand All", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("dependencies.collapse_all", "Dependencies", "Collapse All", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("dependencies.display_operations", "Dependencies", "Display Operations", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("dependencies.print_dependencies_tree", "Dependencies", "Print Dependencies Tree...", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("dependencies.apply_block_to_selected_objects", "Dependencies", "Apply block to selected objects...", null, CommandEnabledWhen.Connected, true, null, false));
        // Category: Fields
        list.Add(new CommandDefinition("fields.new_field", "Fields", "New field", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("fields.edit_field", "Fields", "Edit field", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("fields.drop_field", "Fields", "Drop field", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("fields.reorder_fields", "Fields", "Reorder fields", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("fields.copy_fields_list_to_clipboard", "Fields", "Copy fields list to clipboard", null, CommandEnabledWhen.Always, true, null, false));
        // Category: Grants
        list.Add(new CommandDefinition("grants.grant_to_all", "Grants", "(grupo) Grant to All", null, CommandEnabledWhen.Always, false, @"GRANT ... TO PUBLIC / role", false));
        list.Add(new CommandDefinition("grants.grant_all_with_grant_option", "Grants", "Grant All with GRANT OPTION", null, CommandEnabledWhen.Connected, false, @"GRANT ... WITH GRANT OPTION", false));
        list.Add(new CommandDefinition("grants.revoke_from_all", "Grants", "Revoke from All", null, CommandEnabledWhen.Connected, false, @"REVOKE ... FROM", false));
        list.Add(new CommandDefinition("grants.grant_to_all_2", "Grants", "(usuário) Grant to All", null, CommandEnabledWhen.Always, true, @"GRANT ... TO PUBLIC / role", false));
        list.Add(new CommandDefinition("grants.grant_all_with_grant_option_2", "Grants", "Grant All with GRANT OPTION", null, CommandEnabledWhen.Connected, false, @"GRANT ... WITH GRANT OPTION", false));
        list.Add(new CommandDefinition("grants.revoke_from_all_2", "Grants", "Revoke from All", null, CommandEnabledWhen.Connected, false, @"REVOKE ... FROM", false));
        list.Add(new CommandDefinition("grants.revoke_all_from_all", "Grants", "Revoke All from All", null, CommandEnabledWhen.Connected, true, @"REVOKE ALL", false));
        // Category: Grids
        list.Add(new CommandDefinition("grids.apply_best_fit", "Grids", "Apply best fit", "Ctrl+Add", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("grids.save_grid_data_as", "Grids", "Save grid data as...", "Shift+Ctrl+S", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("grids.copy_current_record_to_clipboard", "Grids", "Copy current record to clipboard", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("grids.copy_all_to_clipboard", "Grids", "Copy all to clipboard", null, CommandEnabledWhen.Always, false, null, false));
        // Category: Blocks
        list.Add(new CommandDefinition("blocks.new_folder", "Blocks", "New Folder", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("blocks.drop_object", "Blocks", "Drop Object", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("blocks.execute", "Blocks", "Execute", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("blocks.new_block_script_webform", "Blocks", "New block/script/webform...", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("blocks.create_edit_event_block", "Blocks", "Create/Edit event block...", null, CommandEnabledWhen.Connected, false, null, false));
        // Category: Sheduler
        list.Add(new CommandDefinition("sheduler.new_task", "Sheduler", "New task...", null, CommandEnabledWhen.Connected, false, @"SQL Agent job", false));
        list.Add(new CommandDefinition("sheduler.edit_task", "Sheduler", "Edit task...", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("sheduler.delete_task", "Sheduler", "Delete task", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("sheduler.new_folder", "Sheduler", "New folder...", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("sheduler.edit_folder", "Sheduler", "Edit folder...", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("sheduler.delete_folder", "Sheduler", "Delete folder", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("sheduler.connect_to_sheduler_server", "Sheduler", "Connect to Sheduler Server", null, CommandEnabledWhen.Connected, true, @"SQL Agent jobs", false));
        list.Add(new CommandDefinition("sheduler.disconnect_from_sheduler_server", "Sheduler", "Disconnect from Sheduler Server", null, CommandEnabledWhen.Connected, false, @"SQL Agent jobs", false));
        // Category: Todo
        list.Add(new CommandDefinition("todo.add_to_do_item", "Todo", "Add to-do item", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("todo.edit_to_do_item", "Todo", "Edit to-do item", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("todo.delete_to_do_item", "Todo", "Delete to-do item", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("todo.refresh_to_do_list", "Todo", "Refresh to-do list", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("todo.hide_done_to_do_items", "Todo", "Hide done to-do items", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("todo.duplicate_to_do_item", "Todo", "Duplicate to-do item", null, CommandEnabledWhen.Always, true, null, false));
        // --- extras present in the reference menus/toolbar but abbreviated in catalog ---
        // Category: DB Explorer popup (right-click on a registered database; reference order, separators and shortcuts)
        list.Add(new CommandDefinition("dbexplorer.new_database_folder", "DbExplorerPopup", "New database folder...", "Ctrl+N", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("dbexplorer.apply_block_to_selected_objects", "DbExplorerPopup", "Apply block to selected objects...", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("dbexplorer.sort_nodes", "DbExplorerPopup", "Sort nodes", null, CommandEnabledWhen.Always, true, null, true));
        list.Add(new CommandDefinition("dbexplorer.sort_by_alias", "DbExplorerPopup", "By alias", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("dbexplorer.sort_as_registered", "DbExplorerPopup", "As registered", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("dbexplorer.autogrant_privileges", "DbExplorerPopup", "Autogrant privileges", "Shift+Ctrl+A", CommandEnabledWhen.Connected, true, @"GRANT defaults to roles/users", false));
        list.Add(new CommandDefinition("dbexplorer.goto_database", "DbExplorerPopup", "Goto Database", null, CommandEnabledWhen.Always, true, null, true));
        list.Add(new CommandDefinition("dbexplorer.refresh", "DbExplorerPopup", "Refresh", "F5", CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("dbexplorer.connect_to_database", "DbExplorerPopup", "Connect to Database", "Shift+Ctrl+C", CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("dbexplorer.reconnect", "DbExplorerPopup", "Reconnect", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("dbexplorer.disconnect_from_database", "DbExplorerPopup", "Disconnect from Database", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("dbexplorer.register_database", "DbExplorerPopup", "Register Database", "Shift+Alt+R", CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("dbexplorer.unregister_database", "DbExplorerPopup", "Unregister Database", "Shift+Alt+U", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("dbexplorer.clone_registration_info", "DbExplorerPopup", "Clone Registration Info", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("dbexplorer.database_registration_info", "DbExplorerPopup", "Database Registration Info...", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("dbexplorer.show_sql_assistant", "DbExplorerPopup", "Show SQL Assistant", "Ctrl+A", CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("dbexplorer.show_objects_description", "DbExplorerPopup", "Show objects description", null, CommandEnabledWhen.Always, false, @"MS_Description (sys.extended_properties)", false));
        list.Add(new CommandDefinition("dbexplorer.inspector_page_mode", "DbExplorerPopup", "Inspector Page Mode", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("dbexplorer.hide_disconnected_databases", "DbExplorerPopup", "Hide Disconnected Databases", null, CommandEnabledWhen.Always, true, null, false));
        // Category: Procedures folder popup (right-click on "Procedures"; reference order, separators and shortcuts)
        list.Add(new CommandDefinition("procfolder.new_procedure", "ProceduresPopup", "New Procedure...", "Ctrl+N", CommandEnabledWhen.Connected, false, @"CREATE PROCEDURE", false));
        list.Add(new CommandDefinition("procfolder.apply_block_to_selected_objects", "ProceduresPopup", "Apply block to selected objects...", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("procfolder.autogrant_privileges", "ProceduresPopup", "Autogrant privileges", "Shift+Ctrl+A", CommandEnabledWhen.Connected, true, @"GRANT EXECUTE to roles/users", false));
        list.Add(new CommandDefinition("procfolder.deactivate_procedures", "ProceduresPopup", "Deactivate procedures", null, CommandEnabledWhen.Connected, true, @"Not applicable to SQL Server", true));
        list.Add(new CommandDefinition("procfolder.activate_procedures", "ProceduresPopup", "Activate procedures", null, CommandEnabledWhen.Connected, false, @"Not applicable to SQL Server", true));
        list.Add(new CommandDefinition("procfolder.goto_database", "ProceduresPopup", "Goto Database", null, CommandEnabledWhen.Always, true, null, true));
        list.Add(new CommandDefinition("procfolder.goto_object", "ProceduresPopup", "Goto Object", null, CommandEnabledWhen.Connected, false, null, true));
        list.Add(new CommandDefinition("procfolder.refresh", "ProceduresPopup", "Refresh", "F5", CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("procfolder.connect_to_database", "ProceduresPopup", "Connect to Database", "Shift+Ctrl+C", CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("procfolder.reconnect", "ProceduresPopup", "Reconnect", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("procfolder.disconnect_from_database", "ProceduresPopup", "Disconnect from Database", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("procfolder.register_database", "ProceduresPopup", "Register Database", "Shift+Alt+R", CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("procfolder.unregister_database", "ProceduresPopup", "Unregister Database", "Shift+Alt+U", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("procfolder.clone_registration_info", "ProceduresPopup", "Clone Registration Info", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("procfolder.database_registration_info", "ProceduresPopup", "Database Registration Info...", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("procfolder.recompute_selectivity_of_all_indices", "ProceduresPopup", "Recompute selectivity of all indices", null, CommandEnabledWhen.Connected, true, @"UPDATE STATISTICS on every user table", false));
        list.Add(new CommandDefinition("procfolder.recompile_all_stored_procedures", "ProceduresPopup", "Recompile all stored procedures", null, CommandEnabledWhen.Connected, false, @"sp_refreshsqlmodule on every procedure", false));
        list.Add(new CommandDefinition("procfolder.recompile_all_triggers", "ProceduresPopup", "Recompile All Triggers", null, CommandEnabledWhen.Connected, false, @"sp_refreshsqlmodule on every trigger", false));
        list.Add(new CommandDefinition("procfolder.show_sql_assistant", "ProceduresPopup", "Show SQL Assistant", "Ctrl+A", CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("procfolder.show_objects_description", "ProceduresPopup", "Show objects description", null, CommandEnabledWhen.Always, false, @"MS_Description (sys.extended_properties)", false));
        list.Add(new CommandDefinition("procfolder.inspector_page_mode", "ProceduresPopup", "Inspector Page Mode", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("procfolder.hide_disconnected_databases", "ProceduresPopup", "Hide Disconnected Databases", null, CommandEnabledWhen.Always, true, null, false));
        // Children of the greyed Deactivate/Activate procedures submenus (no SQL Server equivalent).
        list.Add(new CommandDefinition("procfolder.deactivate_all", "ProceduresPopup.Sub", "All procedures", null, CommandEnabledWhen.Never, false, @"Not applicable to SQL Server", false));
        list.Add(new CommandDefinition("procfolder.deactivate_selected", "ProceduresPopup.Sub", "Selected only", null, CommandEnabledWhen.Never, false, @"Not applicable to SQL Server", false));
        list.Add(new CommandDefinition("procfolder.activate_all", "ProceduresPopup.Sub", "All procedures", null, CommandEnabledWhen.Never, false, @"Not applicable to SQL Server", false));
        list.Add(new CommandDefinition("procfolder.activate_selected", "ProceduresPopup.Sub", "Selected only", null, CommandEnabledWhen.Never, false, @"Not applicable to SQL Server", false));
        // Category: popup of one procedure node
        list.Add(new CommandDefinition("procnode.edit_procedure", "ProcedurePopup", "Edit Procedure...", null, CommandEnabledWhen.Connected, false, @"OBJECT_DEFINITION / CREATE OR ALTER PROCEDURE", false));
        list.Add(new CommandDefinition("procnode.new_procedure", "ProcedurePopup", "New Procedure...", "Ctrl+N", CommandEnabledWhen.Connected, false, @"CREATE PROCEDURE", false));
        list.Add(new CommandDefinition("procnode.drop_procedure", "ProcedurePopup", "Drop Procedure", null, CommandEnabledWhen.Connected, false, @"DROP PROCEDURE", false));
        list.Add(new CommandDefinition("procnode.execute", "ProcedurePopup", "Execute...", null, CommandEnabledWhen.Connected, true, @"EXEC with input parameter values", false));
        list.Add(new CommandDefinition("procnode.copy_name", "ProcedurePopup", "Copy name", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("procnode.script_to_clipboard", "ProcedurePopup", "Script to clipboard (CREATE)", null, CommandEnabledWhen.Connected, false, @"OBJECT_DEFINITION", false));
        list.Add(new CommandDefinition("procnode.apply_block_to_selected_objects", "ProcedurePopup", "Apply block to selected objects...", null, CommandEnabledWhen.Connected, true, null, false));
        list.Add(new CommandDefinition("procnode.autogrant_privileges", "ProcedurePopup", "Autogrant privileges", "Shift+Ctrl+A", CommandEnabledWhen.Connected, false, @"GRANT EXECUTE to roles/users", false));
        list.Add(new CommandDefinition("procnode.refresh", "ProcedurePopup", "Refresh", "F5", CommandEnabledWhen.Connected, true, null, false));
        // Category: procedure editor ("Procedure" drop-down of the editor toolbar)
        list.Add(new CommandDefinition("proceditor.compile", "ProcedureEditor", "Compile", "Ctrl+F9", CommandEnabledWhen.Connected, false, @"CREATE OR ALTER PROCEDURE (ALTER/CREATE before 2016 SP1)", false));
        list.Add(new CommandDefinition("proceditor.execute", "ProcedureEditor", "Execute...", "F9", CommandEnabledWhen.Connected, false, @"EXEC with input parameter values", false));
        list.Add(new CommandDefinition("proceditor.commit", "ProcedureEditor", "Commit", null, CommandEnabledWhen.Connected, true, @"COMMIT TRANSACTION", false));
        list.Add(new CommandDefinition("proceditor.rollback", "ProcedureEditor", "Rollback", null, CommandEnabledWhen.Connected, false, @"ROLLBACK TRANSACTION", false));
        list.Add(new CommandDefinition("proceditor.edit_full_text", "ProcedureEditor", "Edit full text", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("proceditor.reload", "ProcedureEditor", "Reload from server", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("proceditor.copy_name", "ProcedureEditor", "Copy name", null, CommandEnabledWhen.Always, true, null, false));
        list.Add(new CommandDefinition("proceditor.script_to_clipboard", "ProcedureEditor", "Script to clipboard", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("proceditor.drop_procedure", "ProcedureEditor", "Drop Procedure", null, CommandEnabledWhen.Connected, false, @"DROP PROCEDURE", false));
        list.Add(new CommandDefinition("proceditor.debug_procedure", "ProcedureEditor", "Debug procedure", null, CommandEnabledWhen.Never, true, @"No T-SQL debugger in SqlExpert yet", false));
        list.Add(new CommandDefinition("proceditor.print", "ProcedureEditor", "Print", null, CommandEnabledWhen.Never, false, null, false));
        list.Add(new CommandDefinition("proceditor.save_to_file", "ProcedureEditor", "Save to File", null, CommandEnabledWhen.Never, false, null, false));
        list.Add(new CommandDefinition("database.exit", "Database", "Exit", "Alt+F4", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("database.recreate_database", "Database", "Recreate database", null, CommandEnabledWhen.Connected, true, @"DROP + CREATE DATABASE", false));
        list.Add(new CommandDefinition("database.recompile_all_triggers", "Database", "Recompile All Triggers", null, CommandEnabledWhen.Connected, false, @"sp_recompile / ENABLE-DISABLE TRIGGER", false));
        list.Add(new CommandDefinition("tools.script_executive", "Tools", "Script Executive", "Ctrl+F12", CommandEnabledWhen.Connected, false, @"Execute .sql batches with GO", false));
        list.Add(new CommandDefinition("tools.query_builder", "Tools", "Query builder", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("tools.user_manager", "Tools", "User Manager", null, CommandEnabledWhen.Connected, false, @"Logins / database users", false));
        list.Add(new CommandDefinition("tools.grant_manager", "Tools", "Grant Manager", null, CommandEnabledWhen.Connected, false, @"GRANT / REVOKE / DENY", false));
        list.Add(new CommandDefinition("tools.secondary_files_manager", "Tools", "Secondary Files Manager", null, CommandEnabledWhen.Connected, false, @"Database files / filegroups", false));
        list.Add(new CommandDefinition("tools.generate_html_documentation", "Tools", "Generate HTML Documentation", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("tools.database_designer", "Tools", "Database designer", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("tools.test_data_generator", "Tools", "Test data generator", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("tools.report_manager", "Tools", "Report Manager", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("tools.blob_viewer_editor", "Tools", "Blob Viewer/Editor", null, CommandEnabledWhen.Never, false, null, false));
        list.Add(new CommandDefinition("tools.database_inside", "Tools", "Database Inside", null, CommandEnabledWhen.Connected, false, null, false));
        list.Add(new CommandDefinition("tools.localize_sql_server_messages", "Tools", "Localize SQL Server Messages", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("tools.localize_sqlexpert", "Tools", "Localize SqlExpert", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("services.database_shutdown", "Services", "Database Shutdown", null, CommandEnabledWhen.Connected, false, @"ALTER DATABASE SET OFFLINE", false));
        list.Add(new CommandDefinition("edit.load_from_file", "Edit", "Load from File", "Ctrl+L", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("edit.save_to_file", "Edit", "Save to File", "Ctrl+S", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("edit.save_as", "Edit", "Save as...", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("edit.find", "Edit", "Find", "Ctrl+F", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("edit.search_again", "Edit", "Search Again", "F3", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("edit.replace", "Edit", "Replace", "Ctrl+R", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("edit.incremental_search", "Edit", "Incremental Search", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("edit.go_to", "Edit", "Go to...", "Ctrl+G", CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("edit.print", "Edit", "Print", null, CommandEnabledWhen.Always, false, null, false));
        list.Add(new CommandDefinition("plugins.plugin_manager", "Plugins", "Plugin Manager...", null, CommandEnabledWhen.Always, false, null, false));
        return list;
    }
}
