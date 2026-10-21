using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SqlExpert.Tests;

/// <summary>
/// Static audit (no GUI is started): every grid shows NULL as "&lt;null&gt;" in red through the NullValueBrush
/// resource of Resources/Branding.xaml (classic-style), not in grey italics.
/// </summary>
public class NullValueStyleAuditTests
{
    private const string NullMarker = "<null>";

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "SqlExpert.App");
    }

    private static XElement Load(params string[] parts) => XDocument.Load(Path.Combine(new[] { AppDir() }.Concat(parts).ToArray())).Root!;

    private static string? Key(XElement e) => e.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key")?.Value;

    private static XElement StyleByKey(XElement root, string key) =>
        root.Descendants().Single(e => e.Name.LocalName == "Style" && Key(e) == key);

    [Fact]
    public void Branding_DefinesRedNullValueBrush()
    {
        var brush = Load("Resources", "Branding.xaml").Elements()
            .Single(e => e.Name.LocalName == "SolidColorBrush" && Key(e) == "NullValueBrush");
        var color = (string)brush.Attribute("Color")!;
        var m = Regex.Match(color, "^#(?:[0-9A-Fa-f]{2})?(?<r>[0-9A-Fa-f]{2})(?<g>[0-9A-Fa-f]{2})(?<b>[0-9A-Fa-f]{2})$");
        Assert.True(m.Success || color.Equals("Red", StringComparison.OrdinalIgnoreCase), $"NullValueBrush = {color}");
        if (m.Success)
        {
            int C(string g) => int.Parse(m.Groups[g].Value, NumberStyles.HexNumber);
            Assert.True(C("r") >= 0xC0 && C("g") <= 0x40 && C("b") <= 0x40, $"NullValueBrush = {color} is not red");
        }
    }

    [Fact]
    public void NullAwareCellText_UsesNullValueBrush_NotItalic_AndSelectionForeground()
    {
        var style = StyleByKey(Load("Resources", "Branding.xaml"), "NullAwareCellText");
        Assert.Equal("TextBlock", (string?)style.Attribute("TargetType"));

        var trigger = style.Descendants().Single(e => e.Name.LocalName == "Trigger" && (string?)e.Attribute("Property") == "Text");
        Assert.Equal(NullMarker, (string?)trigger.Attribute("Value"));
        var setters = trigger.Elements().Where(e => e.Name.LocalName == "Setter")
            .ToDictionary(e => (string)e.Attribute("Property")!, e => (string)e.Attribute("Value")!);
        Assert.Equal("{StaticResource NullValueBrush}", setters["Foreground"]);
        Assert.NotEqual("Italic", setters.GetValueOrDefault("FontStyle"));

        // Selected cell: take the cell's selection foreground so "<null>" stays readable on the blue highlight.
        var selected = style.Descendants().Single(e => e.Name.LocalName == "MultiDataTrigger");
        var conditions = selected.Descendants().Where(e => e.Name.LocalName == "Condition").ToList();
        Assert.Contains(conditions, c => ((string?)c.Attribute("Binding") ?? "").Contains("RelativeSource Self") && (string?)c.Attribute("Value") == NullMarker);
        Assert.Contains(conditions, c => ((string?)c.Attribute("Binding") ?? "").Contains("IsSelected") && ((string?)c.Attribute("Binding") ?? "").Contains("AncestorType=DataGridCell") && (string?)c.Attribute("Value") == "True");
        var fg = selected.Descendants().Single(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "Foreground");
        Assert.Matches(@"^\{Binding Foreground, RelativeSource=\{RelativeSource AncestorType=DataGridCell\}, Mode=OneWay\}$", (string)fg.Attribute("Value")!);
    }

    [Theory]
    [InlineData("TableDataGrid.xaml", "CellText")]
    [InlineData("SqlEditorView.xaml", "SqlCellText")]
    public void GridCellStyles_AreBasedOnNullAwareCellText(string file, string key)
    {
        var style = StyleByKey(Load("Views", file), key);
        Assert.Equal("{StaticResource NullAwareCellText}", (string?)style.Attribute("BasedOn"));
        Assert.DoesNotContain(style.Descendants(), e => e.Name.LocalName == "Trigger");
    }

    [Fact]
    public void NoXaml_HardCodesTheNullColor()
    {
        var files = Directory.GetFiles(AppDir(), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
        var offenders = new List<string>();
        foreach (var f in files)
        {
            foreach (var t in XDocument.Load(f).Descendants().Where(e => e.Name.LocalName == "Trigger" && (string?)e.Attribute("Value") == NullMarker))
            {
                var fg = t.Elements().FirstOrDefault(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "Foreground");
                if (fg is not null && (string?)fg.Attribute("Value") != "{StaticResource NullValueBrush}")
                    offenders.Add($"{Path.GetFileName(f)}: {(string?)fg.Attribute("Value")}");
            }
        }
        Assert.True(offenders.Count == 0, $"\"<null>\" foreground not taken from NullValueBrush: {string.Join(" | ", offenders)}");
    }

    [Fact]
    public void CodeBuiltGrids_UseTheNullAwareStyles()
    {
        var views = Path.Combine(AppDir(), "Views");
        var data = File.ReadAllText(Path.Combine(views, "TableDataGrid.xaml.cs"));
        // Plain text column and the FK "▾" column both take CellText / CellTextRight.
        Assert.Equal(2, Regex.Matches(data, Regex.Escape("ElementStyle = (Style)FindResource(IsNumeric(spec.DataType) ? \"CellTextRight\" : \"CellText\")")).Count);

        var editor = File.ReadAllText(Path.Combine(views, "SqlEditorView.xaml.cs"));
        Assert.Contains("FindResource(\"SqlCellText\")", editor);
        Assert.Contains("FindResource(\"SqlCellTextRight\")", editor);

        var popup = File.ReadAllText(Path.Combine(views, "ForeignKeyLookupPopup.cs"));
        Assert.Contains("new Style(typeof(TextBlock), _grid.TryFindResource(\"NullAwareCellText\") as Style)", popup);
    }
}
