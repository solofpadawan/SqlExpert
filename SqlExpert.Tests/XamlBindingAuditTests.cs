using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SqlExpert.Core.Registration;

namespace SqlExpert.Tests;

/// <summary>
/// Static audit of the App XAML: DataGrid columns default to TwoWay bindings, which throw at runtime
/// against get-only view-model properties. Every XAML-declared DataGrid column must bind OneWay/OneTime
/// (editing goes through dialogs). Data-tab columns are created in code with explicit modes.
/// </summary>
public class XamlBindingAuditTests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "SqlExpert.App");
    }

    public static IEnumerable<object[]> XamlFiles() =>
        Directory.GetFiles(AppDir(), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => new object[] { Path.GetRelativePath(AppDir(), f) });

    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void DataGridColumns_BindOneWay(string relativePath)
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), relativePath));
        var offenders = doc.Descendants()
            .Where(e => Regex.IsMatch(e.Name.LocalName, "^DataGrid(Text|CheckBox|ComboBox|Hyperlink)Column$"))
            .Select(e => (string?)e.Attribute("Binding"))
            .Where(b => b is not null && b.Contains("{Binding") && !Regex.IsMatch(b, @"Mode\s*=\s*(OneWay|OneTime)\b"))
            .ToList();
        Assert.True(offenders.Count == 0, $"{relativePath}: DataGrid column bindings without Mode=OneWay: {string.Join(" | ", offenders)}");
    }

    private static IEnumerable<(string Element, string Attribute, string Path, string Binding)> Bindings(XElement root) =>
        root.DescendantsAndSelf().SelectMany(e => e.Attributes()
            .Where(a => a.Value.StartsWith("{Binding", StringComparison.Ordinal))
            .Select(a =>
            {
                var m = Regex.Match(a.Value, @"^\{Binding\s*(?:Path=)?(?<p>[A-Za-z_][\w.]*)?");
                return (e.Name.LocalName, a.Name.LocalName, m.Groups["p"].Value, a.Value);
            }));

    /// <summary>Context/sub menus built from CommandMenuItem rows: every binding of the item style must exist on the row class.</summary>
    [Fact]
    public void CommandMenuItemStyle_BindsExistingProperties()
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), "Resources", "CommandMenuTemplates.xaml"));
        var style = doc.Descendants().Single(e => e.Name.LocalName == "Style" && e.Attributes().Any(a => a.Name.LocalName == "Key" && a.Value == "CommandMenuItemStyle"));
        var source = File.ReadAllText(Path.Combine(AppDir(), "Services", "AppCommandRegistry.cs"));
        var cls = source[source.IndexOf("public sealed class CommandMenuItem", StringComparison.Ordinal)..];
        var props = Regex.Matches(cls, @"public\s+(?:required\s+)?[\w<>?,\s]+?\s+(?<n>\w+)\s*\{\s*get;").Select(m => m.Groups["n"].Value).ToHashSet();
        var paths = Bindings(style).Select(b => b.Path).Where(p => p.Length > 0).Distinct().ToList();
        Assert.NotEmpty(paths);
        foreach (var path in paths)
            Assert.True(props.Contains(path), $"CommandMenuItemStyle binds '{path}', which CommandMenuItem does not declare ({string.Join(", ", props)})");
        // Check marks are display-only: a TwoWay IsChecked would try to write back into the init-only row.
        Assert.All(Bindings(style).Where(b => b.Attribute == "Value" && b.Path == "IsChecked"), b => Assert.Contains("Mode=OneWay", b.Binding));
    }

    [Fact]
    public void ExplorerContextMenu_UsesRebuiltMenuItems()
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), "MainWindow.xaml"));
        var tree = doc.Descendants().Single(e => e.Name.LocalName == "TreeView" && (string?)e.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name") == "ExplorerTree");
        Assert.Equal("Explorer_OnContextMenuOpening", (string?)tree.Attribute("ContextMenuOpening"));
        var menu = tree.Descendants().Single(e => e.Name.LocalName == "ContextMenu");
        Assert.Equal("{Binding ExplorerMenuItems}", (string?)menu.Attribute("ItemsSource"));
        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "MainViewModel.cs"));
        Assert.Contains("public ObservableCollection<CommandMenuItem> ExplorerMenuItems", vm);
        Assert.Contains("public void PrepareExplorerContextMenu()", vm);
    }

    /// <summary>
    /// Database Properties binds to <see cref="RegistrationForm"/>: every path must exist, and read-only properties
    /// bound to a TwoWay-by-default target (Text, IsChecked, SelectedIndex…) must say Mode=OneWay.
    /// </summary>
    [Fact]
    public void DatabaseProperties_BindsRegistrationForm()
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), "Views", "DatabasePropertiesWindow.xaml"));
        var props = typeof(RegistrationForm).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(p => p.Name);
        var twoWayTargets = new HashSet<string> { "Text", "IsChecked", "SelectedIndex", "SelectedItem", "SelectedValue" };
        var bindings = Bindings(doc.Root!).Where(b => b.Path.Length > 0).ToList();
        Assert.True(bindings.Count > 20);
        foreach (var b in bindings)
        {
            Assert.True(props.TryGetValue(b.Path, out var prop), $"DatabasePropertiesWindow binds '{b.Path}' ({b.Element}.{b.Attribute}), which RegistrationForm does not have");
            if (twoWayTargets.Contains(b.Attribute) && (prop!.SetMethod is null || !prop.SetMethod.IsPublic))
                Assert.Matches(@"Mode\s*=\s*(OneWay|OneTime)", b.Binding);
        }
    }

    [Fact]
    public void FieldsGrid_IsReadOnly()
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), "MainWindow.xaml"));
        var grid = doc.Descendants().Single(e => e.Name.LocalName == "DataGrid" && ((string?)e.Attribute("ItemsSource"))?.Contains("SampleColumns") == true);
        Assert.Equal("True", (string?)grid.Attribute("IsReadOnly"));
        Assert.Equal("False", (string?)grid.Attribute("CanUserAddRows"));
    }
    /// <summary>
    /// Folder nodes: "Tables" (normal color) + " (505)" (blue, own TextBlock) — not one concatenated string.
    /// The count uses the Branding brush and switches to the selection foreground on the focused highlight.
    /// </summary>
    [Fact]
    public void ExplorerTree_DrawsCountInSeparateBlueText()
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), "MainWindow.xaml"));
        var tree = doc.Descendants().Single(e => e.Name.LocalName == "TreeView" && (string?)e.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name") == "ExplorerTree");
        var blocks = tree.Descendants().Where(e => e.Name.LocalName == "TextBlock").ToList();
        string? NameOf(XElement e) => (string?)e.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name");
        var label = blocks.Single(b => NameOf(b) == "NodeLabel");
        var count = blocks.Single(b => NameOf(b) == "NodeCount");
        // Name drawn as prefix / bold filter match / suffix runs (Explorer filter highlight).
        var runs = label.Elements().Where(e => e.Name.LocalName == "Run").ToList();
        Assert.Equal(new[] { "{Binding NamePrefix, Mode=OneWay}", "{Binding NameMatch, Mode=OneWay}", "{Binding NameSuffix, Mode=OneWay}" },
            runs.Select(r => (string?)r.Attribute("Text")).ToArray());
        Assert.Equal("Bold", (string?)runs[1].Attribute("FontWeight"));
        Assert.Null(label.Attribute("Text"));
        Assert.Null(label.Attribute("Foreground"));
        Assert.Matches(@"^\{Binding CountText, Mode=OneWay\}$", (string?)count.Attribute("Text") ?? "");
        // Foreground only through the style (a local value would beat the selection trigger).
        Assert.Null(count.Attribute("Foreground"));
        Assert.True(label.ElementsAfterSelf().First() == count, "NodeCount must follow NodeLabel");
        var style = count.ToString();
        Assert.Contains("{DynamicResource ExplorerCountBrush}", style);
        Assert.Contains("SystemColors.HighlightTextBrushKey", style);
        Assert.Contains("(Selector.IsSelectionActive)", style);

        var branding = XDocument.Load(Path.Combine(AppDir(), "Resources", "Branding.xaml"));
        var brush = branding.Descendants().Single(e => e.Name.LocalName == "SolidColorBrush" && e.Attributes().Any(a => a.Name.LocalName == "Key" && a.Value == "ExplorerCountBrush"));
        Assert.Equal("#1F3FD0", (string?)brush.Attribute("Color"));

        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "MainViewModel.cs"));
        Assert.Contains("public string? CountText => ExplorerNodeText.CountText(_count, _filteredCount);", vm);
        Assert.Contains("Count = cat.Count,", vm);
        var metadata = File.ReadAllText(Path.Combine(AppDir(), "..", "SqlExpert.Core", "Services", "MetadataService.cs"));
        Assert.DoesNotContain("({count})", metadata);
    }

    /// <summary>Explorer tree: virtualized with recycled containers, and the deep filter never queries the server.</summary>
    [Fact]
    public void ExplorerTree_IsVirtualized_AndFilterIsLocal()
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), "MainWindow.xaml"));
        var tree = doc.Descendants().Single(e => e.Name.LocalName == "TreeView" && (string?)e.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name") == "ExplorerTree");
        string? Attr(string name) => (string?)tree.Attributes().FirstOrDefault(a => a.Name.LocalName == name);
        Assert.Equal("True", Attr("VirtualizingPanel.IsVirtualizing"));
        Assert.Equal("Recycling", Attr("VirtualizingPanel.VirtualizationMode"));

        var filter = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "MainViewModel.Filter.cs"));
        Assert.DoesNotContain("_metadata.", filter);           // no metadata query from the filter itself
        Assert.Contains("Task.Run(", filter);                   // matching off the UI thread
        Assert.Contains("FilterDebounceMs = 220", filter);
        var vm = File.ReadAllText(Path.Combine(AppDir(), "ViewModels", "MainViewModel.cs"));
        Assert.Contains("OnFilterTextChanged();", vm);
    }
}
