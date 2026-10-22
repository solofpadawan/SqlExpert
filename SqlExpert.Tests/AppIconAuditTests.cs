using System.Xml.Linq;

namespace SqlExpert.Tests;

/// <summary>
/// Application icon: one original multi-size .ico (Assets/sqlexpert.ico) used by the exe and by every window.
/// Static checks only; no GUI is started.
/// </summary>
public class AppIconAuditTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string RepoFile(params string[] parts) => Path.Combine(new[] { Root() }.Concat(parts).ToArray());

    [Fact]
    public void Ico_HasAllSizes_With32BitAlpha()
    {
        var bytes = File.ReadAllBytes(RepoFile("SqlExpert.App", "Assets", "sqlexpert.ico"));
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2)); // icon
        var count = BitConverter.ToUInt16(bytes, 4);
        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + i * 16;
            sizes.Add(bytes[entry] == 0 ? 256 : bytes[entry]);
            Assert.Equal(32, BitConverter.ToUInt16(bytes, entry + 6)); // bits per pixel
            var size = BitConverter.ToInt32(bytes, entry + 8);
            var offset = BitConverter.ToInt32(bytes, entry + 12);
            Assert.True(size > 0 && offset + size <= bytes.Length);
        }
        Assert.Equal(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }, sizes.OrderBy(s => s));
    }

    [Fact]
    public void Project_EmbedsTheIcon_InTheExeAndAsWpfResource()
    {
        var csproj = XDocument.Load(RepoFile("SqlExpert.App", "SqlExpert.App.csproj"));
        Assert.Equal(@"Assets\sqlexpert.ico", csproj.Descendants("ApplicationIcon").Single().Value);
        Assert.Contains(csproj.Descendants("Resource"), r => (string?)r.Attribute("Include") == @"Assets\sqlexpert.ico");
    }

    [Fact]
    public void Windows_UseTheAppIcon()
    {
        Assert.Contains("Icon=\"pack://application:,,,/SqlExpert;component/Assets/sqlexpert.ico\"", File.ReadAllText(RepoFile("SqlExpert.App", "MainWindow.xaml")));

        var app = File.ReadAllText(RepoFile("SqlExpert.App", "App.xaml.cs"));
        var register = app.IndexOf("AppIcon.ApplyToAllWindows();", StringComparison.Ordinal);
        Assert.True(register > 0, "App.OnStartup registers the icon for every window");
        Assert.True(register < app.IndexOf("new SplashWindow", StringComparison.Ordinal), "registered before the first window");
        Assert.True(register < app.IndexOf("UpdaterMode.Run", StringComparison.Ordinal), "registered before the updater windows");

        var helper = File.ReadAllText(RepoFile("SqlExpert.App", "Services", "AppIcon.cs"));
        Assert.Contains("pack://application:,,,/SqlExpert;component/Assets/sqlexpert.ico", helper);
        Assert.Contains("RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent", helper);
    }

    [Fact]
    public void IconSources_ArePresentAndNeutral()
    {
        var dir = RepoFile("tools", "icons", "appicon");
        foreach (var f in new[] { "genT.py", "previewT.py", "textpath.py", "sqlexpert-T.svg", "README.md" })
            Assert.True(File.Exists(Path.Combine(dir, f)), f);
        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain(".jpg", Path.GetFileName(f), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ibexpert", File.ReadAllText(f), StringComparison.OrdinalIgnoreCase);
        }
    }
}