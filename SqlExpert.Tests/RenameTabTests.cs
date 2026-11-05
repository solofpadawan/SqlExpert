using System.Text.Json;
using System.Text.RegularExpressions;
using SqlExpert.Core.Documents;
using Xunit;

namespace SqlExpert.Tests;

public class RenameTabTests
{
    private static string AppFile(string rel)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "SqlExpert.App", "MainWindow.xaml"))) dir = Path.GetDirectoryName(dir)!;
        return File.ReadAllText(Path.Combine(dir, "SqlExpert.App", rel));
    }

    [Fact]
    public void WindowsBarContextMenu_HasRenomear_BeforeClose()
    {
        var xaml = AppFile("MainWindow.xaml");
        var i = xaml.IndexOf("<Button.ContextMenu>", StringComparison.Ordinal);
        Assert.True(i > 0);
        var menu = xaml[i..(i + 600)];
        Assert.Contains("Header=\"Renomear\"", menu);
        Assert.Contains("RenameDocumentCommand", menu);
        Assert.True(menu.IndexOf("Renomear", StringComparison.Ordinal) < menu.IndexOf("Header=\"Close\"", StringComparison.Ordinal));
    }

    [Fact]
    public void RenameTabWindow_Exists_WithPortugueseTitle()
    {
        var cs = AppFile(Path.Combine("Views", "RenameTabWindow.cs"));
        Assert.Contains("Title = \"Renomear\"", cs);
        Assert.Contains("Nome da aba:", cs);
        Assert.Contains("MaxNameLength = 80", cs);
    }

    [Fact]
    public void SessionTab_CustomTitle_RoundTripsInJson()
    {
        var tab = new SessionTab
        {
            Kind = SessionTabKind.SqlEditor,
            Number = 3,
            Database = "Sales",
            SqlText = "select 1",
            IsModified = true,
            CustomTitle = "consulta clientes"
        };
        var json = JsonSerializer.Serialize(tab);
        var loaded = JsonSerializer.Deserialize<SessionTab>(json)!;
        Assert.Equal("consulta clientes", loaded.CustomTitle);
        Assert.Equal("consulta clientes", loaded.DisplayText);
    }

    [Fact]
    public void SessionTab_WithoutCustomTitle_KeepsDefaultDisplay()
    {
        var tab = new SessionTab { Kind = SessionTabKind.SqlEditor, Number = 2 };
        Assert.Null(tab.CustomTitle);
        Assert.Equal("SQL Editor 2", tab.DisplayText);
    }

    [Fact]
    public void OpenTabsStore_PersistsCustomTitle()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SqlExpert-tests", Guid.NewGuid().ToString("N"), "sessions");
        var store = new OpenTabsSessionStore(dir);
        var session = new OpenTabsSession
        {
            ProfileId = "0123456789abcdef0123456789abcdef",
            Tabs =
            {
                new SessionTab { Kind = SessionTabKind.SqlEditor, Number = 1, Database = "Sales", CustomTitle = "meu select", SqlText = "select 1" }
            }
        };
        store.Save(session);
        var loaded = store.Load(session.ProfileId);
        Assert.NotNull(loaded);
        Assert.Equal("meu select", loaded!.Tabs[0].CustomTitle);
    }

    [Fact]
    public void SqlEditor_ExposesCustomTitleAndDefaultShortName()
    {
        var cs = AppFile(Path.Combine("ViewModels", "SqlEditorDocumentViewModel.cs"));
        Assert.Contains("public string? CustomTitle", cs);
        Assert.Contains("public string DefaultShortName", cs);
        Assert.Contains("CustomTitle = _customTitle", cs); // CaptureSessionTab
        Assert.Contains("tab.CustomTitle", cs); // RestoreSessionText
    }
}
