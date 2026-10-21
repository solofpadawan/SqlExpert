using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SqlExpert.Tests;

/// <summary>
/// Static audit: every DataGrid uses the light gray classic-style lines from Resources/Branding.xaml
/// (GridLineBrush), never the WPF default black or any other dark brush.
/// </summary>
public class GridLineStyleAuditTests
{
    private static readonly string[] BrushAttributes = { "HorizontalGridLinesBrush", "VerticalGridLinesBrush", "BorderBrush" };
    private static readonly HashSet<string> AllowedResourceKeys = new() { "GridLineBrush", "GridBorderBrush", "GridHeaderSeparatorBrush" };
    private const int MinLuminance = 0xB0; // anything darker than this is "dark"

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "SqlExpert.App");
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.GetFiles(AppDir(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    public static IEnumerable<object[]> XamlFiles() =>
        SourceFiles("*.xaml").Select(f => new object[] { Path.GetRelativePath(AppDir(), f) });

    private static XElement BrandingDoc() => XDocument.Load(Path.Combine(AppDir(), "Resources", "Branding.xaml")).Root!;

    private static string? Key(XElement e) => e.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key")?.Value;

    internal static bool IsLight(string value)
    {
        var v = value.Trim();
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Black"] = "#000000", ["DimGray"] = "#696969", ["Gray"] = "#808080", ["DarkGray"] = "#A9A9A9",
            ["Silver"] = "#C0C0C0", ["LightGray"] = "#D3D3D3", ["Gainsboro"] = "#DCDCDC", ["White"] = "#FFFFFF",
            ["Transparent"] = "#00FFFFFF"
        };
        if (named.TryGetValue(v, out var hex)) v = hex;
        var m = Regex.Match(v, "^#(?:[0-9A-Fa-f]{2})?(?<r>[0-9A-Fa-f]{2})(?<g>[0-9A-Fa-f]{2})(?<b>[0-9A-Fa-f]{2})$");
        if (!m.Success) return false;
        int C(string g) => int.Parse(m.Groups[g].Value, NumberStyles.HexNumber);
        var lum = 0.299 * C("r") + 0.587 * C("g") + 0.114 * C("b");
        return lum >= MinLuminance;
    }

    [Theory]
    [InlineData("#000000", false)]
    [InlineData("Black", false)]
    [InlineData("#808080", false)]
    [InlineData("#DCDCDC", true)]
    [InlineData("#FFD8D8D8", true)]
    public void IsLight_ClassifiesColors(string color, bool light) => Assert.Equal(light, IsLight(color));

    [Fact]
    public void Branding_DefinesLightGridBrushes()
    {
        var brushes = BrandingDoc().Elements().Where(e => e.Name.LocalName == "SolidColorBrush").ToDictionary(e => Key(e) ?? "", e => (string?)e.Attribute("Color") ?? "");
        foreach (var key in AllowedResourceKeys)
        {
            Assert.True(brushes.ContainsKey(key), $"Branding.xaml must define SolidColorBrush '{key}'");
            Assert.True(IsLight(brushes[key]), $"{key} = {brushes[key]} is too dark");
        }
    }

    [Fact]
    public void Branding_ImplicitDataGridStyle_UsesGridLineBrush()
    {
        var style = BrandingDoc().Elements().SingleOrDefault(e => e.Name.LocalName == "Style" && (string?)e.Attribute("TargetType") == "DataGrid" && Key(e) is null);
        Assert.NotNull(style);
        var setters = style!.Elements().Where(e => e.Name.LocalName == "Setter")
            .ToDictionary(e => (string)e.Attribute("Property")!, e => (string)e.Attribute("Value")!);
        Assert.Equal("{StaticResource GridLineBrush}", setters["HorizontalGridLinesBrush"]);
        Assert.Equal("{StaticResource GridLineBrush}", setters["VerticalGridLinesBrush"]);
        Assert.Equal("{StaticResource GridBorderBrush}", setters["BorderBrush"]);
    }

    [Fact]
    public void App_MergesBranding()
    {
        var app = File.ReadAllText(Path.Combine(AppDir(), "App.xaml"));
        Assert.Contains("Resources/Branding.xaml", app);
    }

    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void DataGrids_HaveNoDarkLineBrushes(string relativePath)
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), relativePath));
        var offenders = new List<string>();
        foreach (var grid in doc.Descendants().Where(e => e.Name.LocalName == "DataGrid"))
        {
            foreach (var attr in BrushAttributes)
            {
                var v = (string?)grid.Attribute(attr);
                if (v is null) continue;
                var res = Regex.Match(v, @"^\{(?:Static|Dynamic)Resource\s+(?<k>\w+)\}$");
                if (res.Success ? !AllowedResourceKeys.Contains(res.Groups["k"].Value) : !IsLight(v))
                    offenders.Add($"{attr}=\"{v}\"");
            }
            // An explicit Style replaces the implicit one: it must be BasedOn the implicit DataGrid style.
            var styleRef = (string?)grid.Attribute("Style");
            if (styleRef is not null)
            {
                var key = Regex.Match(styleRef, @"Resource\s+(?<k>\w+)\}").Groups["k"].Value;
                var style = doc.Descendants().Concat(BrandingDoc().Descendants())
                    .FirstOrDefault(e => e.Name.LocalName == "Style" && Key(e) == key);
                var basedOn = (string?)style?.Attribute("BasedOn") ?? "";
                if (!basedOn.Contains("x:Type DataGrid"))
                    offenders.Add($"Style=\"{styleRef}\" not BasedOn the implicit DataGrid style");
            }
            foreach (var inline in grid.Elements().Where(e => e.Name.LocalName == "DataGrid.Style").SelectMany(e => e.Elements()))
                if (!((string?)inline.Attribute("BasedOn") ?? "").Contains("x:Type DataGrid"))
                    offenders.Add("inline DataGrid.Style not BasedOn the implicit DataGrid style");
        }
        // Any style (cell/row/header) setting a dark border or gridline brush.
        foreach (var setter in doc.Descendants().Where(e => e.Name.LocalName == "Setter"))
        {
            var prop = (string?)setter.Attribute("Property") ?? "";
            var parentType = (string?)setter.Parent?.Attribute("TargetType") ?? "";
            if (!parentType.StartsWith("DataGrid", StringComparison.Ordinal)) continue;
            if (!(prop.EndsWith("GridLinesBrush") || prop is "BorderBrush" or "SeparatorBrush")) continue;
            var v = (string?)setter.Attribute("Value") ?? "";
            var res = Regex.Match(v, @"^\{(?:Static|Dynamic)Resource\s+(?<k>\w+)\}$");
            if (res.Success ? !AllowedResourceKeys.Contains(res.Groups["k"].Value) : !IsLight(v))
                offenders.Add($"{parentType}.{prop}=\"{v}\"");
        }
        Assert.True(offenders.Count == 0, $"{relativePath}: dark DataGrid line brushes: {string.Join(" | ", offenders)}");
    }

    [Fact]
    public void CodeBuiltGrids_UseGridLineBrushResource()
    {
        var offenders = new List<string>();
        foreach (var file in SourceFiles("*.cs"))
        {
            var src = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(src, @"(Horizontal|Vertical)GridLinesBrush(Property)?\s*[=,][^;\r\n]*"))
            {
                var text = m.Value;
                if (text.Contains("SetResourceReference") || Regex.IsMatch(text, "\"Grid(Line|Border|HeaderSeparator)Brush\"")) continue;
                offenders.Add($"{Path.GetFileName(file)}: {text.Trim()}");
            }
        }
        Assert.True(offenders.Count == 0, $"Grid line brushes set in code without the GridLineBrush resource: {string.Join(" | ", offenders)}");
    }
}
