using SqlExpert.Core.Commands;

namespace SqlExpert.Tests;

public class CommandAvailabilityTests
{
    [Fact]
    public void UnimplementedCatalogCommands_CannotExecute()
    {
        var unimplemented = CommandCatalog.All.Where(c => !CommandAvailability.IsImplemented(c.Id)).ToList();
        Assert.True(unimplemented.Count > 150);
        foreach (var def in unimplemented)
            Assert.False(CommandAvailability.CanExecute(def, isConnected: true, hasSelection: true), def.Id);
    }

    [Theory]
    [InlineData("constraints.drop_constraint")]
    [InlineData("view.autohide_db_explorer")]
    [InlineData("indices.new_index")]
    public void KnownStubs_AreDisabled(string id)
    {
        var def = CommandCatalog.Find(id);
        Assert.NotNull(def);
        Assert.False(CommandAvailability.CanExecute(def!, true, true));
    }

    [Fact]
    public void ImplementedIds_ExistInCatalog()
    {
        foreach (var id in CommandAvailability.ImplementedIds)
            Assert.NotNull(CommandCatalog.Find(id));
    }

    [Fact]
    public void ImplementedCommands_FollowConnectionRule()
    {
        var register = CommandCatalog.Find("database.register_database")!;
        Assert.True(CommandAvailability.CanExecute(register, false, false));
        var newField = CommandCatalog.Find("fields.new_field")!;
        Assert.Equal(newField.EnabledWhen != CommandEnabledWhen.Connected, CommandAvailability.CanExecute(newField, false, false));
        Assert.True(CommandAvailability.CanExecute(newField, true, false));
        Assert.True(CommandAvailability.CanExecute(CommandCatalog.Find("fields.copy_fields_list_to_clipboard")!, false, false));
    }

    [Fact]
    public void MenuActions_OnlyImplementedCaptions()
    {
        Assert.True(CommandAvailability.IsMenuActionImplemented("Connect to Database"));
        Assert.True(CommandAvailability.IsMenuActionImplemented("SQL Editor"));
        Assert.False(CommandAvailability.IsMenuActionImplemented("Query builder"));
        Assert.False(CommandAvailability.IsMenuActionImplemented("Backup Database"));
        Assert.False(CommandAvailability.IsMenuActionImplemented(null));
    }

    [Fact]
    public void SqlEditorCommands_AreEnabled()
    {
        Assert.True(CommandAvailability.CanExecute(CommandCatalog.Find("tools.sql_editor")!, false, false));
        Assert.True(CommandAvailability.CanExecute(CommandCatalog.Find("tools.new_sql_editor")!, false, false));
        Assert.True(CommandAvailability.IsMenuActionEnabled("New SQL Editor", sqlEditorActive: false));
        // Other editor/tool commands that are not done stay grey.
        Assert.False(CommandAvailability.CanExecute(CommandCatalog.Find("editors_actions.format_selected_text")!, true, true));
        Assert.False(CommandAvailability.CanExecute(CommandCatalog.Find("tools.script_executive")!, true, true));
    }

    [Theory]
    [InlineData("Load from File")]
    [InlineData("Save to File")]
    [InlineData("Save as...")]
    [InlineData("Find")]
    [InlineData("Replace")]
    public void EditorMenuActions_OnlyWithActiveSqlEditor(string caption)
    {
        Assert.False(CommandAvailability.IsMenuActionImplemented(caption));
        Assert.False(CommandAvailability.IsMenuActionEnabled(caption, sqlEditorActive: false));
        Assert.True(CommandAvailability.IsMenuActionEnabled(caption, sqlEditorActive: true));
        Assert.False(CommandAvailability.IsMenuActionEnabled("Print", sqlEditorActive: true));
    }
}
