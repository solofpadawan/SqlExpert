using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SqlExpert.Core.Commands;

namespace SqlExpert.Tests;

/// <summary>
/// Shared look of the app (SqlExpert.App/Themes/Controls.xaml): folder-style tabs everywhere, flat inner toolbars
/// whose buttons all show a vector icon, modern dialog buttons, crisp rendering and a palette without green.
/// </summary>
public class UiStyleAuditTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string AppFile(params string[] parts) => Path.Combine(new[] { Root(), "SqlExpert.App" }.Concat(parts).ToArray());

    private static XDocument Controls() => XDocument.Load(AppFile("Themes", "Controls.xaml"));

    private static string? Key(XElement e) => (string?)e.Attribute(X + "Key");

    private static XElement Style(XDocument doc, string key) =>
        doc.Descendants().Single(e => e.Name.LocalName == "Style" && Key(e) == key);

    private static XElement ImplicitStyle(XDocument doc, string target) =>
        doc.Root!.Elements().Single(e => e.Name.LocalName == "Style" && Key(e) is null && (string?)e.Attribute("TargetType") == target);

    public static IEnumerable<object[]> XamlFiles()
    {
        var app = AppFile();
        var sep = Path.DirectorySeparatorChar;
        return Directory.GetFiles(app, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .Select(f => new object[] { Path.GetRelativePath(app, f) });
    }

    [Fact]
    public void ControlsXaml_IsMergedIntoTheApplication()
    {
        var app = File.ReadAllText(AppFile("App.xaml"));
        Assert.Contains("<ResourceDictionary Source=\"Themes/Controls.xaml\"/>", app);
        // Merged after Icons.xaml and Branding.xaml (it may use their resources).
        Assert.True(app.IndexOf("Themes/Controls.xaml", StringComparison.Ordinal) > app.IndexOf("Themes/Icons.xaml", StringComparison.Ordinal));
    }

    [Fact]
    public void Tabs_ImplicitFolderStyle_MatchesTheApprovedSpec()
    {
        var doc = Controls();
        var tabControl = ImplicitStyle(doc, "TabControl").ToString();
        Assert.Contains("PART_SelectedContentHost", tabControl);
        Assert.Contains("TabPanel", tabControl);
        Assert.Contains("Tab.StripBackground", tabControl);
        Assert.Contains("Property=\"TabStripPlacement\" Value=\"Bottom\"", tabControl);

        var tabItem = ImplicitStyle(doc, "TabItem").ToString();
        Assert.Contains("CornerRadius=\"4,4,0,0\"", tabItem);           // folder tab: rounded top corners
        Assert.Contains("CornerRadius\" Value=\"0,0,4,4\"", tabItem);    // pointing down on bottom strips
        Assert.Contains("BorderThickness=\"1,1,1,0\"", tabItem);
        Assert.Contains("Height=\"22\"", tabItem);
        Assert.Contains("Property=\"Height\" Value=\"25\"", tabItem);    // active tab raised 3px
        Assert.Contains("Value=\"0,0,2,-1\"", tabItem);                  // ... and joined to the content panel
        Assert.Contains("Ui.Accent", tabItem);                           // 2px top accent line
        Assert.Contains("Height=\"2\"", tabItem);
        Assert.Contains("Property=\"IsEnabled\" Value=\"False\"", tabItem);
        Assert.Contains("Tab.HoverBackground", tabItem);
        // The page fills the panel (TabControl copies the selected TabItem's content alignment to its host).
        Assert.Contains("Property=\"HorizontalContentAlignment\" Value=\"Stretch\"", tabItem);
        Assert.Contains("Property=\"VerticalContentAlignment\" Value=\"Stretch\"", tabItem);
        Assert.DoesNotContain("HorizontalContentAlignment", tabControl);

        string Color(string key) => (string)doc.Descendants().Single(e => Key(e) == key).Attribute("Color")!;
        Assert.Equal("#5B84B8", Color("Ui.Accent"));
        Assert.Equal("#E8EDF4", Color("Tab.StripBackground"));
        Assert.Equal("#A9B6C8", Color("Ui.Border"));
        Assert.Equal("#EEF1F5", Color("Tab.DisabledBackground"));
        var inactive = doc.Descendants().Single(e => Key(e) == "Tab.InactiveBackground").Descendants().Select(s => (string?)s.Attribute("Color")).ToList();
        Assert.Equal(new[] { "#F4F7FB", "#DCE4EE" }, inactive);
    }

    /// <summary>A keyed TabItem / TabControl style without BasedOn would drop the folder template.</summary>
    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void KeyedTabStyles_AreBasedOnTheImplicitOnes(string relativePath)
    {
        var doc = XDocument.Load(AppFile(relativePath));
        foreach (var style in doc.Descendants().Where(e => e.Name.LocalName == "Style" && Key(e) is not null))
        {
            var target = (string?)style.Attribute("TargetType");
            if (target is not ("TabItem" or "TabControl")) continue;
            Assert.Equal($"{{StaticResource {{x:Type {target}}}}}", (string?)style.Attribute("BasedOn"));
        }
        // No TabControl / TabItem replaces the template locally.
        Assert.DoesNotContain(doc.Descendants(), e => e.Name.LocalName is "TabControl.Template" or "TabItem.Template");
    }

    [Fact]
    public void InnerToolbar_FlatButtons_MatchTheApprovedSpec()
    {
        var doc = Controls();
        var button = Style(doc, "FlatToolButton").ToString();
        Assert.Contains("CornerRadius=\"3\"", button);
        Assert.Contains("Property=\"Height\" Value=\"24\"", button);
        Assert.Contains("Value=\"5,0,7,0\"", button);
        Assert.Contains("Ui.HoverFill", button);
        Assert.Contains("Ui.PressedFill", button);
        Assert.Contains("TranslateTransform Y=\"1\"", button);           // pressed: content 1px down
        Assert.Contains("Ui.TextDisabled", button);
        string Color(string key) => (string)doc.Descendants().Single(e => Key(e) == key).Attribute("Color")!;
        Assert.Equal("#DCE8F7", Color("Ui.HoverFill"));
        Assert.Equal("#9DB9E0", Color("Ui.HoverBorder"));
        Assert.Equal("#C6D9F1", Color("Ui.PressedFill"));
        Assert.Equal("#7FA2D2", Color("Ui.PressedBorder"));
        Assert.Equal("#A3ACB9", Color("Ui.TextDisabled"));

        var toolbar = Style(doc, "InnerToolBar").ToString();
        Assert.Contains("PART_ToolBarPanel", toolbar);
        Assert.Contains("PART_ToolBarOverflowPanel", toolbar);
        Assert.Contains("x:Name=\"Gripper\"", toolbar);
        Assert.Contains("ToolBar.ButtonStyleKey", toolbar);
        Assert.Contains("ToolBar.SeparatorStyleKey", toolbar);
        var separator = Style(doc, "FlatToolSeparator").ToString();
        Assert.Contains("#C3CDDA", separator);
        Assert.Contains("#FFFFFF", separator);

        // Disabled icons: grey copy at ~45% opacity.
        Assert.Contains("Opacity = IsEnabled ? 1.0 : 0.45;", File.ReadAllText(AppFile("Views", "IconImage.cs")));
    }

    /// <summary>Every ToolBar except the main toolbar band uses the inner toolbar style.</summary>
    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void InnerToolbars_UseTheFlatStyle(string relativePath)
    {
        var doc = XDocument.Load(AppFile(relativePath));
        foreach (var bar in doc.Descendants().Where(e => e.Name.LocalName == "ToolBar"))
        {
            var inMainTray = relativePath == "MainWindow.xaml" && bar.Parent?.Name.LocalName == "ToolBarTray";
            if (inMainTray) continue;
            var style = (string?)bar.Attribute("Style");
            Assert.True(style is "{StaticResource InnerToolBar}" or "{StaticResource InnerTrayToolBar}",
                $"{relativePath}: ToolBar without the inner toolbar style ({style ?? "none"})");
        }
    }

    /// <summary>Every button on an inner toolbar shows a vector icon (with its caption next to it, or the tooltip).</summary>
    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void InnerToolbarButtons_AllHaveAnIcon(string relativePath)
    {
        var doc = XDocument.Load(AppFile(relativePath));
        var buttons = doc.Descendants().Where(e => e.Name.LocalName == "ToolBar")
            .SelectMany(b => b.Descendants().Where(e => e.Name.LocalName == "Button")).ToList();
        foreach (var b in buttons)
        {
            var tip = (string?)b.Attribute("ToolTip") ?? b.ToString()[..Math.Min(80, b.ToString().Length)];
            Assert.True(b.Descendants().Any(e => e.Name.LocalName == "IconImage"), $"{relativePath}: toolbar button '{tip}' has no icon");
            Assert.Null(b.Attribute("Content"));
        }
        // Command lists (Fields, Constraints, ...) go through the shared icon + caption template.
        foreach (var list in doc.Descendants().Where(e => e.Name.LocalName == "ItemsControl" && e.Ancestors().Any(a => a.Name.LocalName == "ToolBar")))
            Assert.Equal("{StaticResource InnerToolbarCommandTemplate}", (string?)list.Attribute("ItemTemplate"));
    }

    [Fact]
    public void TableEditorToolbars_UseTheCommandTemplate()
    {
        var xaml = File.ReadAllText(AppFile("MainWindow.xaml"));
        foreach (var list in new[] { "FieldsMenuItems", "ConstraintsMenuItems", "IndicesMenuItems", "DependenciesMenuItems", "GrantsMenuItems" })
            Assert.Contains($"<ItemsControl ItemsSource=\"{{Binding Shell.{list}}}\" Focusable=\"False\" ItemTemplate=\"{{StaticResource InnerToolbarCommandTemplate}}\">", xaml);
        var template = Controls().Descendants().Single(e => e.Name.LocalName == "DataTemplate" && Key(e) == "InnerToolbarCommandTemplate").ToString();
        Assert.Contains("FlatToolButton", template);
        Assert.Contains("{Binding IconKey", template);
        Assert.Contains("FlatToolSeparator", template);                 // groups separated, not hidden
        Assert.Contains("Margin=\"5,0,0,0\"", template);                // 5px between icon and caption
        Assert.Contains("CommandIcons.For(def.Id)", File.ReadAllText(AppFile("ViewModels", "MainViewModel.cs")));
    }

    [Fact]
    public void EveryInnerToolbarCommand_HasAnExistingIcon()
    {
        var icons = XDocument.Load(AppFile("Themes", "Icons.xaml")).Root!.Elements().Select(Key).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var category in CommandIcons.ToolbarCategories)
        {
            var commands = CommandCatalog.ByCategory(category).ToList();
            Assert.NotEmpty(commands);
            foreach (var def in commands)
            {
                var key = CommandIcons.For(def.Id);
                Assert.True(key is not null, $"{def.Id} has no toolbar icon");
                Assert.True(icons.Contains(key!), $"{def.Id}: unknown icon {key}");
            }
        }
        Assert.Equal("Icon.InsertRecord", CommandIcons.For("fields.new_field"));
        Assert.Equal("Icon.EditRecord", CommandIcons.For("fields.edit_field"));
        Assert.Equal("Icon.DeleteRecord", CommandIcons.For("fields.drop_field"));
        Assert.Equal("Icon.Reorder", CommandIcons.For("fields.reorder_fields"));
        Assert.Equal("Icon.Paste", CommandIcons.For("fields.copy_fields_list_to_clipboard"));
    }

    [Fact]
    public void ReorderIcon_UsesTheApprovedArrows()
    {
        var script = File.ReadAllText(Path.Combine(Root(), "tools", "icons", "generate_icons.py"));
        Assert.Contains("M5,13.5 V2.5 M2.5,5 L5,2.5 L7.5,5", script);
        Assert.Contains("M11,2.5 V13.5 M8.5,11 L11,13.5 L13.5,11", script);
        Assert.Contains("\"Reorder\": reorder()", script);
        Assert.Contains("x:Key=\"Icon.Reorder\"", File.ReadAllText(AppFile("Themes", "Icons.xaml")));
    }

    [Fact]
    public void DocumentTitleBars_UseTheSharedStyle()
    {
        var xaml = File.ReadAllText(AppFile("MainWindow.xaml"));
        Assert.Equal(4, Regex.Matches(xaml, Regex.Escape("Style=\"{StaticResource DocTitleBar}\"")).Count); // 3 documents + Explorer
        Assert.Equal(3, Regex.Matches(xaml, Regex.Escape("Style=\"{StaticResource DocTitleText}\"")).Count);
        var bar = Style(Controls(), "DocTitleBar").ToString();
        Assert.Contains("Value=\"22\"", bar);
        Assert.Contains("DocTitle.Background", bar);
        var text = Style(Controls(), "DocTitleText").ToString();
        Assert.Contains("SemiBold", text);
        Assert.Contains("Ui.TitleText", text);
    }

    [Fact]
    public void WindowsBar_TabsPointDown_WithTheSamePalette()
    {
        var xaml = File.ReadAllText(AppFile("MainWindow.xaml"));
        var style = xaml[xaml.IndexOf("<Style x:Key=\"WindowsBarTab\"", StringComparison.Ordinal)..];
        style = style[..(style.IndexOf("</Style>", StringComparison.Ordinal) + 8)];
        Assert.Contains("CornerRadius=\"0,0,4,4\"", style);
        Assert.Contains("Ui.Accent", style);
        Assert.Contains("Tab.InactiveBackgroundDown", style);
        Assert.Contains("Value=\"0,-1,2,0\"", style);
        Assert.DoesNotContain("SemiBold", style); // normal weight: widths do not jump
    }

    /// <summary>OK / Cancel and the other dialog buttons share the rounded "DialogButton" style (main action: "DialogPrimaryButton").</summary>
    [Theory]
    [InlineData("Views/FieldEditorWindow.xaml")]
    [InlineData("Views/RegisterDatabaseWindow.xaml")]
    [InlineData("Views/DatabasePropertiesWindow.xaml")]
    [InlineData("Views/ReorderFieldsWindow.xaml")]
    public void DialogWindows_UseDialogButtons_AndLayoutRounding(string file)
    {
        var doc = XDocument.Load(AppFile(file.Split('/')));
        Assert.Equal("True", (string?)doc.Root!.Attribute("UseLayoutRounding"));
        var buttons = doc.Descendants().Where(e => e.Name.LocalName == "Button").ToList();
        Assert.NotEmpty(buttons);
        foreach (var b in buttons)
            Assert.Contains((string?)b.Attribute("Style"), new[] { "{StaticResource DialogButton}", "{StaticResource DialogPrimaryButton}" });
        var style = Style(Controls(), "DialogButton").ToString();
        Assert.Contains("CornerRadius=\"3\"", style);
        Assert.Contains("Property=\"IsDefault\" Value=\"True\"", style);
    }

    [Fact]
    public void CodeBuiltDialogs_UseDialogButtons()
    {
        foreach (var file in new[] { "ProcedureExecuteWindow.cs", "UpdateWindow.cs", "BatchProgressWindow.cs", "ChooseRegistrationWindow.cs", "ReplaceWindow.cs", "PrimaryKeyWindow.cs", "MemoEditorWindow.cs" })
        {
            var src = File.ReadAllText(AppFile("Views", file));
            Assert.Contains(".WithDialogStyle()", src);
            Assert.DoesNotMatch(@"= new Button \{[^{}\n]*Content = ""(OK|Cancel|Close|Fechar|Cancelar|Depois)""[^{}\n]*\};", src);
        }
        var memo = File.ReadAllText(AppFile("Views", "MemoEditorWindow.cs"));
        Assert.Contains("\"InnerTrayToolBar\"", memo);
        Assert.DoesNotContain("\"|◀\"", memo); // vector icons on its navigator
    }

    [Fact]
    public void Rendering_SnapsToDevicePixels()
    {
        var root = XDocument.Load(AppFile("MainWindow.xaml")).Root!;
        Assert.Equal("True", (string?)root.Attribute("UseLayoutRounding"));
        var doc = Controls();
        foreach (var key in new[] { "FlatToolButton", "InnerToolBar", "DialogButton", "DocTitleBar" })
            Assert.Contains("Property=\"SnapsToDevicePixels\" Value=\"True\"", Style(doc, key).ToString());
        foreach (var target in new[] { "TabControl", "TabItem" })
        {
            var s = ImplicitStyle(doc, target).ToString();
            Assert.Contains("Property=\"SnapsToDevicePixels\" Value=\"True\"", s);
            Assert.Contains("Property=\"UseLayoutRounding\" Value=\"True\"", s);
        }
    }

    /// <summary>The user asked for no green anywhere: control styles, windows and code-set colors.</summary>
    [Fact]
    public void AppColors_HaveNoGreen()
    {
        var app = AppFile();
        var sep = Path.DirectorySeparatorChar;
        var files = Directory.GetFiles(app, "*.xaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(app, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .ToList();
        Assert.Contains(files, f => f.EndsWith("Controls.xaml", StringComparison.Ordinal));
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var name = Path.GetRelativePath(app, file);
            Assert.DoesNotMatch(@"\bBrushes\.(Dark|Light|Lime|Forest|Sea|Spring|Lawn|Pale|Medium|Yellow)?Green\b", text);
            Assert.DoesNotMatch(@"""(Dark|Lime|Forest|Lawn)?Green""", text);
            foreach (Match m in Regex.Matches(text, "#(?:[0-9A-Fa-f]{2})?([0-9A-Fa-f]{6})\\b"))
                Assert.False(IsGreen(m.Groups[1].Value), $"{name}: #{m.Groups[1].Value} looks green");
            foreach (Match m in Regex.Matches(text, @"Color\.FromRgb\(0x([0-9A-Fa-f]{2}), 0x([0-9A-Fa-f]{2}), 0x([0-9A-Fa-f]{2})\)"))
                Assert.False(IsGreen(m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value), $"{name}: {m.Value} looks green");
        }
    }

    private static bool IsGreen(string hex)
    {
        var r = int.Parse(hex[..2], NumberStyles.HexNumber) / 255.0;
        var g = int.Parse(hex[2..4], NumberStyles.HexNumber) / 255.0;
        var b = int.Parse(hex[4..], NumberStyles.HexNumber) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        if (delta < 0.08) return false;
        var sat = delta / max;
        var hue = max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        return hue is > 75 and < 160 && sat > 0.15;
    }
}
