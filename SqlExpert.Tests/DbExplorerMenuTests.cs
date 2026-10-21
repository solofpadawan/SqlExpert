using SqlExpert.Core.Commands;

namespace SqlExpert.Tests;

/// <summary>DB Explorer popup on a registered database (reference order, shortcuts, enabled/greyed state).</summary>
public class DbExplorerMenuTests
{
    private static readonly string[] Implemented =
    {
        "dbexplorer.sort_nodes", "dbexplorer.sort_by_alias", "dbexplorer.sort_as_registered", "dbexplorer.goto_database",
        "dbexplorer.refresh", "dbexplorer.connect_to_database", "dbexplorer.reconnect", "dbexplorer.disconnect_from_database",
        "dbexplorer.register_database", "dbexplorer.unregister_database", "dbexplorer.clone_registration_info",
        "dbexplorer.database_registration_info", "dbexplorer.show_objects_description", "dbexplorer.hide_disconnected_databases"
    };

    private static readonly string[] Greyed =
    {
        "dbexplorer.new_database_folder", "dbexplorer.apply_block_to_selected_objects", "dbexplorer.autogrant_privileges",
        "dbexplorer.show_sql_assistant", "dbexplorer.inspector_page_mode"
    };

    private static readonly ExplorerMenuContext Connected = new(HasProfile: true, IsNodeConnected: true, RegisteredCount: 2);
    private static readonly ExplorerMenuContext Disconnected = new(HasProfile: true, IsNodeConnected: false, RegisteredCount: 2);
    private static readonly ExplorerMenuContext NoNode = new(HasProfile: false, IsNodeConnected: false, RegisteredCount: 0);

    [Fact]
    public void AllPopupIds_AreInCatalog()
    {
        foreach (var id in Implemented.Concat(Greyed))
            Assert.NotNull(CommandCatalog.Find(id));
        Assert.Equal(19, CommandCatalog.ByCategory("DbExplorerPopup").Count());
    }

    [Fact]
    public void ImplementedItems_AreRegistered()
    {
        foreach (var id in Implemented)
            Assert.True(CommandAvailability.IsImplemented(id), id);
    }

    [Fact]
    public void GreyedItems_NeverExecute()
    {
        foreach (var id in Greyed)
        {
            Assert.False(CommandAvailability.IsImplemented(id), id);
            Assert.False(CommandAvailability.CanExecuteExplorerItem(id, Connected), id);
            Assert.False(CommandAvailability.CanExecuteExplorerItem(id, Disconnected), id);
            Assert.False(CommandAvailability.CanExecute(CommandCatalog.Find(id)!, true, true), id);
        }
    }

    [Theory]
    [InlineData("dbexplorer.refresh", true, false)]
    [InlineData("dbexplorer.connect_to_database", false, true)]
    [InlineData("dbexplorer.reconnect", true, false)]
    [InlineData("dbexplorer.disconnect_from_database", true, false)]
    [InlineData("dbexplorer.register_database", true, true)]
    [InlineData("dbexplorer.unregister_database", true, true)]
    [InlineData("dbexplorer.clone_registration_info", true, true)]
    [InlineData("dbexplorer.database_registration_info", true, true)]
    [InlineData("dbexplorer.hide_disconnected_databases", true, true)]
    [InlineData("dbexplorer.show_objects_description", true, true)]
    [InlineData("dbexplorer.sort_by_alias", true, true)]
    public void Items_FollowConnectionState(string id, bool whenConnected, bool whenDisconnected)
    {
        Assert.Equal(whenConnected, CommandAvailability.CanExecuteExplorerItem(id, Connected));
        Assert.Equal(whenDisconnected, CommandAvailability.CanExecuteExplorerItem(id, Disconnected));
    }

    [Theory]
    [InlineData("dbexplorer.unregister_database", false)]
    [InlineData("dbexplorer.clone_registration_info", false)]
    [InlineData("dbexplorer.database_registration_info", false)]
    [InlineData("dbexplorer.connect_to_database", false)]
    [InlineData("dbexplorer.goto_database", false)]
    [InlineData("dbexplorer.register_database", true)]
    [InlineData("dbexplorer.hide_disconnected_databases", true)]
    public void Items_WithoutSelectedDatabase(string id, bool expected)
        => Assert.Equal(expected, CommandAvailability.CanExecuteExplorerItem(id, NoNode));

    [Theory]
    [InlineData("dbexplorer.new_database_folder", "Ctrl+N")]
    [InlineData("dbexplorer.autogrant_privileges", "Shift+Ctrl+A")]
    [InlineData("dbexplorer.refresh", "F5")]
    [InlineData("dbexplorer.connect_to_database", "Shift+Ctrl+C")]
    [InlineData("dbexplorer.register_database", "Shift+Alt+R")]
    [InlineData("dbexplorer.unregister_database", "Shift+Alt+U")]
    [InlineData("dbexplorer.show_sql_assistant", "Ctrl+A")]
    public void Shortcuts_MatchReference(string id, string shortcut)
        => Assert.Equal(shortcut, CommandCatalog.Find(id)!.Shortcut);

    [Fact]
    public void Popup_OrderAndSeparators_MatchReference()
    {
        var rows = new List<string>();
        foreach (var d in CommandCatalog.ByCategory("DbExplorerPopup").Where(d => d.Id is not "dbexplorer.sort_by_alias" and not "dbexplorer.sort_as_registered"))
        {
            if (d.IsSeparatorBefore && rows.Count > 0) rows.Add("-");
            rows.Add(d.Caption);
        }
        Assert.Equal(new[]
        {
            "New database folder...", "-",
            "Apply block to selected objects...", "-",
            "Sort nodes", "-",
            "Autogrant privileges", "-",
            "Goto Database", "-",
            "Refresh", "-",
            "Connect to Database", "Reconnect", "Disconnect from Database", "-",
            "Register Database", "Unregister Database", "Clone Registration Info", "-",
            "Database Registration Info...", "-",
            "Show SQL Assistant", "Show objects description", "-",
            "Inspector Page Mode", "-",
            "Hide Disconnected Databases"
        }, rows);
    }

    [Fact]
    public void Submenus_AreMarked()
    {
        Assert.True(CommandCatalog.Find("dbexplorer.sort_nodes")!.IsSubmenu);
        Assert.True(CommandCatalog.Find("dbexplorer.goto_database")!.IsSubmenu);
    }
}
