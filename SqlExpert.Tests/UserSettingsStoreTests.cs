using System.Text.Json.Nodes;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.Tests;

public class UserSettingsStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "SqlExpert-tests", Guid.NewGuid().ToString("N"), "settings.json");

    [Fact]
    public void MissingFile_GivesDefault()
        => Assert.Equal(UserSettings.DefaultDataGridFontSize, new UserSettingsStore(TempPath()).Load().DataGridFontSize);

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var store = new UserSettingsStore(TempPath());
        store.Save(new UserSettings { DataGridFontSize = 14 });
        Assert.Equal(14, store.Load().DataGridFontSize);
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(6, 6)]
    [InlineData(32, 32)]
    [InlineData(99, 32)]
    public void FontSize_IsClampedOnSaveAndLoad(int value, int expected)
    {
        Assert.Equal(expected, UserSettings.ClampFontSize(value));
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"{{\"dataGridFontSize\": {value}}}");
        Assert.Equal(expected, new UserSettingsStore(path).Load().DataGridFontSize);
    }

    [Theory]
    [InlineData("not json {")]
    [InlineData("{\"dataGridFontSize\": \"big\"}")]
    [InlineData("[]")]
    public void CorruptFile_GivesDefault(string content)
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        Assert.Equal(UserSettings.DefaultDataGridFontSize, new UserSettingsStore(path).Load().DataGridFontSize);
    }

    [Fact]
    public void Save_PreservesOtherKeys()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"other\": \"keep\", \"dataGridFontSize\": 9}");
        new UserSettingsStore(path).Save(new UserSettings { DataGridFontSize = 11 });
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("keep", (string?)root["other"]);
        Assert.Equal(11, (int?)root["dataGridFontSize"]);
    }
}

public class WindowPlacementTests
{
    private static readonly ScreenRect Work = new(0, 0, 1920, 1040);
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "SqlExpert-tests", Guid.NewGuid().ToString("N"), "settings.json");

    [Fact]
    public void Default_Is1280x800_CenteredAndNormal()
    {
        var p = WindowPlacementLogic.Default(Work);
        Assert.Equal(new WindowPlacement(320, 120, 1280, 800, false), p);
    }

    [Fact]
    public void Default_IsClampedToSmallWorkArea()
    {
        var p = WindowPlacementLogic.Default(new ScreenRect(0, 0, 1024, 728));
        Assert.Equal(1024, p.Width);
        Assert.Equal(728, p.Height);
        Assert.Equal(0, p.Left);
        Assert.False(p.IsMaximized);
    }

    [Fact]
    public void Restore_NullOrInvalid_GivesNull()
    {
        Assert.Null(WindowPlacementLogic.Restore(null, Work));
        Assert.Null(WindowPlacementLogic.Restore(new WindowPlacement(0, 0, double.NaN, 500, false), Work));
        Assert.Null(WindowPlacementLogic.Restore(new WindowPlacement(0, 0, 0, 500, false), Work));
    }

    [Theory]
    [InlineData(5000, 100)]    // right of the desktop (monitor unplugged)
    [InlineData(-3000, 100)]   // left
    [InlineData(100, 2000)]    // below
    [InlineData(100, -500)]    // title bar above the top edge
    public void Restore_OffScreen_GivesNull(double left, double top)
        => Assert.Null(WindowPlacementLogic.Restore(new WindowPlacement(left, top, 1000, 700, false), Work));

    [Fact]
    public void Restore_OnScreen_KeepsBoundsAndMaximized()
    {
        var saved = new WindowPlacement(100, 50, 1100, 700, true);
        Assert.Equal(saved, WindowPlacementLogic.Restore(saved, Work));
    }

    [Fact]
    public void Restore_ClampsOversizedWindow()
    {
        var r = WindowPlacementLogic.Restore(new WindowPlacement(10, 10, 5000, 3000, false), Work)!;
        Assert.Equal(1920, r.Width);
        Assert.Equal(1040, r.Height);
    }

    [Fact]
    public void Store_RoundTripsPlacement_AndFontSaveKeepsIt()
    {
        var store = new UserSettingsStore(TempPath());
        store.Save(new UserSettings { DataGridFontSize = 12, MainWindow = new WindowPlacement(10.5, 20, 1200, 760, true) });
        Assert.Equal(new WindowPlacement(10.5, 20, 1200, 760, true), store.Load().MainWindow);
        // A font-only save (MainWindow null) must not erase the saved placement.
        store.Save(new UserSettings { DataGridFontSize = 14 });
        var loaded = store.Load();
        Assert.Equal(14, loaded.DataGridFontSize);
        Assert.Equal(new WindowPlacement(10.5, 20, 1200, 760, true), loaded.MainWindow);
    }

    [Theory]
    [InlineData("{\"mainWindow\": {\"left\": 1, \"top\": 2, \"width\": \"x\", \"height\": 3}}")]
    [InlineData("{\"mainWindow\": {\"left\": 1, \"top\": 2, \"width\": -5, \"height\": 3}}")]
    [InlineData("{\"mainWindow\": 7}")]
    public void Store_BadPlacement_IsIgnored(string json)
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        Assert.Null(new UserSettingsStore(path).Load().MainWindow);
    }

    [Fact]
    public void MainWindowXaml_DoesNotForceMaximized()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln"))) dir = dir.Parent;
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "SqlExpert.App", "MainWindow.xaml"));
        Assert.DoesNotContain("WindowState=\"Maximized\"", xaml);
    }
}
