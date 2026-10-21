using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SqlExpert.Core.Explorer;
using SqlExpert.Core.Models;

namespace SqlExpert.Tests;

/// <summary>
/// Vector icon set (SqlExpert.App/Themes/Icons.xaml): every icon the app asks for exists, every main-toolbar command
/// shows one, Explorer nodes and Windows-bar tabs bind to it, and the palette has no green.
/// </summary>
public class IconResourceAuditTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string AppFile(params string[] parts) => Path.Combine(new[] { Root(), "SqlExpert.App" }.Concat(parts).ToArray());

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument IconsXaml() => XDocument.Load(AppFile("Themes", "Icons.xaml"));

    private static HashSet<string> IconKeys() =>
        IconsXaml().Root!.Elements().Select(e => (string?)e.Attribute(X + "Key")).OfType<string>().ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void IconsXaml_IsMergedIntoTheApplication()
    {
        var app = File.ReadAllText(AppFile("App.xaml"));
        Assert.Contains("<ResourceDictionary Source=\"Themes/Icons.xaml\"/>", app);
        Assert.Contains("x:Key=\"IconKeyToImage\"", app);
    }

    [Fact]
    public void IconsXaml_OnlyDrawingImages_On16x16Bounds()
    {
        var root = IconsXaml().Root!;
        Assert.True(root.Elements().Count() >= 60);
        foreach (var e in root.Elements())
        {
            Assert.Equal("DrawingImage", e.Name.LocalName);
            var key = (string)e.Attribute(X + "Key")!;
            Assert.StartsWith(ObjectIcons.Prefix, key);
            // Transparent 16x16 frame first: icons keep their position and size whatever they draw.
            var first = e.Descendants().First(d => d.Name.LocalName == "GeometryDrawing");
            Assert.Equal("Transparent", (string?)first.Attribute("Brush"));
            Assert.Equal("M0,0 H16 V16 H0 Z", (string?)first.Attribute("Geometry"));
        }
    }

    [Fact]
    public void EveryExplorerNodeType_HasAnIcon()
    {
        var keys = IconKeys();
        foreach (var key in ObjectIcons.AllKeys())
            Assert.True(keys.Contains(key), $"missing icon {key}");
        foreach (var kind in Enum.GetValues<ObjectKind>())
            Assert.Contains(ObjectIcons.For(kind), keys);
    }

    public static IEnumerable<object[]> XamlFiles()
    {
        var app = AppFile();
        var sep = Path.DirectorySeparatorChar;
        return Directory.GetFiles(app, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .Select(f => new object[] { Path.GetRelativePath(app, f) });
    }

    /// <summary>A StaticResource to a missing icon only fails at run time (XAML parse error on startup).</summary>
    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void StaticIconReferences_Exist(string relativePath)
    {
        var keys = IconKeys();
        var text = File.ReadAllText(AppFile(relativePath));
        foreach (Match m in Regex.Matches(text, @"\{StaticResource (Icon\.\w+)\}"))
            Assert.True(keys.Contains(m.Groups[1].Value), $"{relativePath}: unknown icon {m.Groups[1].Value}");
    }

    [Fact]
    public void MainToolbar_EveryCommandHasAnIcon_AndKeepsItsCaptionAsToolTip()
    {
        var keys = IconKeys();
        var doc = XDocument.Load(AppFile("MainWindow.xaml"));
        var tray = doc.Descendants().Single(e => e.Name.LocalName == "ToolBarTray");
        var buttons = tray.Descendants().Where(e => e.Name.LocalName == "Button").ToList();
        Assert.True(buttons.Count >= 30, $"only {buttons.Count} toolbar buttons");
        foreach (var b in buttons)
        {
            var tip = (string?)b.Attribute("ToolTip");
            Assert.False(string.IsNullOrWhiteSpace(tip), "toolbar button without tooltip");
            Assert.Null(b.Attribute("Content")); // no more "+DB" / "SQL+" text captions
            Assert.Equal("{StaticResource IconToolButton}", (string?)b.Attribute("Style"));
            var icon = b.Elements().SingleOrDefault(e => e.Name.LocalName == "IconImage");
            Assert.True(icon is not null, $"'{tip}' has no IconImage");
            var key = Regex.Match((string?)icon!.Attribute("Icon") ?? "", @"^\{StaticResource (Icon\.\w+)\}$").Groups[1].Value;
            Assert.True(keys.Contains(key), $"'{tip}' uses unknown icon '{key}'");
        }
        foreach (var tip in new[] { "Register Database", "Unregister Database", "Connect to Database", "Disconnect from Database", "Reconnect",
                     "Database Registration Info...", "SQL Editor", "New SQL Editor", "New Table", "New Procedure", "Exit" })
            Assert.Contains(buttons, b => (string?)b.Attribute("ToolTip") == tip);
    }

    [Fact]
    public void DisabledIcons_AreGreyed()
    {
        var src = File.ReadAllText(AppFile("Views", "IconImage.cs"));
        Assert.Contains("IsEnabled", src);
        Assert.Contains("Opacity", src);
        Assert.Contains("ShowOnDisabled", File.ReadAllText(AppFile("App.xaml"))); // tooltips of disabled commands still show
    }

    [Fact]
    public void ExplorerNodes_ShowTheNodeTypeIcon()
    {
        var xaml = File.ReadAllText(AppFile("MainWindow.xaml"));
        Assert.Contains("<views:IconImage x:Name=\"NodeIcon\"", xaml);
        Assert.Contains("Icon=\"{Binding IconKey, Mode=OneWay, Converter={StaticResource IconKeyToImage}}\"", xaml);
        Assert.DoesNotContain("DbIconTemplate", xaml); // the old green "connected" cylinder is gone
        var vm = File.ReadAllText(AppFile("ViewModels", "MainViewModel.cs"));
        Assert.Contains("public string? IconKey => NodeKind switch", vm);
        Assert.Contains("ExplorerNodeKind.Category or ExplorerNodeKind.Object when ObjectKind is { } kind => ObjectIcons.For(kind)", vm);
        Assert.Contains("ObjectIcons.Registration(IsActive)", vm);
    }

    [Fact]
    public void EveryDocumentType_HasATabIcon()
    {
        var sources = Directory.GetFiles(AppFile("ViewModels"), "*.cs").Select(File.ReadAllText).ToList();
        var documents = sources.SelectMany(s => Regex.Matches(s, @"class (\w+) : DocumentViewModel\b").Select(m => m.Groups[1].Value)).ToList();
        Assert.True(documents.Count >= 3);
        var overrides = sources.Sum(s => Regex.Matches(s, @"public override string IconKey =>").Count);
        Assert.Equal(documents.Count, overrides);
        Assert.Contains(sources, s => s.Contains("public abstract string IconKey { get; }"));
        // Windows bar tab template shows it next to the name.
        var xaml = File.ReadAllText(AppFile("MainWindow.xaml"));
        var bar = xaml[xaml.IndexOf("<!-- Windows bar -->", StringComparison.Ordinal)..];
        bar = bar[..bar.IndexOf("</ItemsControl>", StringComparison.Ordinal)];
        Assert.Contains("<views:IconImage", bar);
        Assert.Contains("{Binding IconKey", bar);
    }

    [Theory]
    [InlineData("Views/TableDataGrid.xaml", "NavButton")]
    [InlineData("Views/SqlEditorView.xaml", "SqlTbButton")]
    [InlineData("Views/ProcedureEditorView.xaml", "ProcTbButton")]
    public void EditorToolbars_UseVectorIcons_NotEmoji(string file, string style)
    {
        var xaml = File.ReadAllText(AppFile(file.Split('/')));
        Assert.Contains("<views:IconImage", xaml);
        foreach (var glyph in new[] { "📂", "💾", "🔍", "🖨", "▶ Execute", "■ Stop", "|◀", "▶|" })
            Assert.DoesNotContain($"Content=\"{glyph}", xaml);
        Assert.Contains(style, xaml);
    }

    /// <summary>The user asked for soft colors and no green anywhere in the icons.</summary>
    [Fact]
    public void Palette_HasNoGreen()
    {
        var text = File.ReadAllText(AppFile("Themes", "Icons.xaml"));
        var colors = Regex.Matches(text, "#(?:[0-9A-Fa-f]{2})?([0-9A-Fa-f]{6})\\b").Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToList();
        Assert.NotEmpty(colors);
        foreach (var hex in colors)
        {
            var r = int.Parse(hex[..2], NumberStyles.HexNumber) / 255.0;
            var g = int.Parse(hex[2..4], NumberStyles.HexNumber) / 255.0;
            var b = int.Parse(hex[4..], NumberStyles.HexNumber) / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var delta = max - min;
            if (delta < 0.08) continue; // grey
            var sat = delta / max;
            double hue = max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
            if (hue < 0) hue += 360;
            Assert.False(hue is > 75 and < 160 && sat > 0.15, $"#{hex} looks green (hue {hue:0}, saturation {sat:0.00})");
        }
    }

    [Fact]
    public void IconsXaml_IsGenerated_FromTheToolScript()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "tools", "icons", "generate_icons.py")));
        Assert.Contains("GENERATED by tools/icons/generate_icons.py", File.ReadAllText(AppFile("Themes", "Icons.xaml")));
    }
}
