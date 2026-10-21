using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SqlExpert.Tests;

/// <summary>
/// Check boxes and radio buttons (SqlExpert.App/Themes/Controls.xaml): one implicit style with the approved palette,
/// used by dialogs and by every DataGridCheckBoxColumn (whose cells otherwise get the WPF default look).
/// </summary>
public class CheckBoxStyleAuditTests
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
        doc.Root!.Elements().Single(e => e.Name.LocalName == "Style" && Key(e) == key);

    private static string Color(XDocument doc, string key) =>
        (string)doc.Root!.Elements().Single(e => Key(e) == key).Attribute("Color")!;

    private static IEnumerable<string> SourceFiles(string pattern)
    {
        var sep = Path.DirectorySeparatorChar;
        return Directory.GetFiles(AppFile(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"));
    }

    [Fact]
    public void Palette_MatchesTheApprovedSpec()
    {
        var doc = Controls();
        Assert.Equal("#8D9AAD", Color(doc, "Check.Border"));
        Assert.Equal("#FFFFFF", Color(doc, "Check.Background"));
        Assert.Equal("#23446F", Color(doc, "Check.Glyph"));
        Assert.Equal("#5B84B8", Color(doc, "Check.HoverBorder"));
        Assert.Equal("#EEF4FB", Color(doc, "Check.HoverFill"));
        Assert.Equal("#DCE8F7", Color(doc, "Check.PressedFill"));
        Assert.Equal("#CBD3DE", Color(doc, "Check.DisabledBorder"));
        Assert.Equal("#F3F5F8", Color(doc, "Check.DisabledBackground"));
        Assert.Equal("#A3ACB9", Color(doc, "Check.DisabledGlyph"));
    }

    [Fact]
    public void CheckBox_ImplicitStyle_Is13pxRoundedBoxWithVectorCheck()
    {
        var doc = Controls();
        var implicitStyle = doc.Root!.Elements().Single(e => e.Name.LocalName == "Style" && Key(e) is null
            && (string?)e.Attribute("TargetType") == "CheckBox");
        Assert.Equal("{StaticResource SqlCheckBox}", (string?)implicitStyle.Attribute("BasedOn"));

        var s = Style(doc, "SqlCheckBox").ToString();
        Assert.Contains("Width=\"13\" Height=\"13\" CornerRadius=\"2\"", s);
        Assert.Contains("<Path x:Name=\"CheckMark\"", s);
        Assert.Contains("Stroke=\"{StaticResource Check.Glyph}\"", s);
        Assert.Contains("x:Name=\"IndeterminateMark\"", s);                      // IsThreeState / NULL
        Assert.Contains("Property=\"IsChecked\" Value=\"{x:Null}\"", s);
        Assert.Contains("Property=\"IsChecked\" Value=\"True\"", s);
        Assert.Contains("Property=\"IsMouseOver\" Value=\"True\"", s);
        Assert.Contains("Property=\"IsPressed\" Value=\"True\"", s);
        Assert.Contains("Check.HoverBorder", s);
        Assert.Contains("Check.HoverFill", s);
        Assert.Contains("Check.PressedFill", s);
        Assert.Contains("Property=\"IsEnabled\" Value=\"False\"", s);
        Assert.Contains("Check.DisabledGlyph", s);
        Assert.Contains("Ui.TextDisabled", s);                                      // caption greyed when disabled
        Assert.Contains("Property=\"Padding\" Value=\"4,-1,0,0\"", s);             // 4px gap box → caption
        Assert.Contains("RecognizesAccessKey=\"True\"", s);
        Assert.Contains("Property=\"SnapsToDevicePixels\" Value=\"True\"", s);
        Assert.Contains("Property=\"UseLayoutRounding\" Value=\"True\"", s);
    }

    [Fact]
    public void RadioButton_ImplicitStyle_IsA13pxCircleInTheSamePalette()
    {
        var doc = Controls();
        var implicitStyle = doc.Root!.Elements().Single(e => e.Name.LocalName == "Style" && Key(e) is null
            && (string?)e.Attribute("TargetType") == "RadioButton");
        Assert.Equal("{StaticResource SqlRadioButton}", (string?)implicitStyle.Attribute("BasedOn"));
        var s = Style(doc, "SqlRadioButton").ToString();
        Assert.Contains("Width=\"13\" Height=\"13\"", s);
        Assert.Contains("<Ellipse x:Name=\"Circle\"", s);
        Assert.Contains("<Ellipse x:Name=\"Dot\"", s);
        Assert.Contains("Check.Glyph", s);
        Assert.Contains("Check.DisabledBorder", s);
    }

    [Fact]
    public void GridCheckBoxStyles_AreBasedOnTheCheckBoxStyle_AndReadOnlyCellsAreNotDisabled()
    {
        var doc = Controls();
        foreach (var key in new[] { "GridCheckBox", "GridCheckBoxEditing" })
        {
            var style = Style(doc, key);
            Assert.Equal("{StaticResource SqlCheckBox}", (string?)style.Attribute("BasedOn"));
            Assert.Contains("Property=\"HorizontalAlignment\" Value=\"Center\"", style.ToString());
            // Read-only cells look normal: never disabled, only not clickable (display element).
            Assert.DoesNotContain("Property=\"IsEnabled\"", style.ToString());
        }
        Assert.Contains("Property=\"IsHitTestVisible\" Value=\"False\"", Style(doc, "GridCheckBox").ToString());
    }

    [Fact]
    public void EveryXamlDataGridCheckBoxColumn_UsesTheGridCheckBoxStyles()
    {
        var count = 0;
        foreach (var file in SourceFiles("*.xaml"))
        {
            foreach (var col in XDocument.Load(file).Descendants().Where(e => e.Name.LocalName == "DataGridCheckBoxColumn"))
            {
                count++;
                Assert.Equal("{StaticResource GridCheckBox}", (string?)col.Attribute("ElementStyle"));
                Assert.Equal("{StaticResource GridCheckBoxEditing}", (string?)col.Attribute("EditingElementStyle"));
            }
        }
        Assert.True(count >= 4, "the Fields grid has PK / FK / UNQ / Null check box columns");
    }

    [Fact]
    public void EveryCodeDataGridCheckBoxColumn_UsesTheGridCheckBoxStyles()
    {
        var count = 0;
        foreach (var file in SourceFiles("*.cs"))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"new DataGridCheckBoxColumn\b"))
            {
                count++;
                var end = FindInitializerEnd(text, m.Index);
                var init = text[m.Index..end];
                Assert.True(Regex.IsMatch(init, @"\bElementStyle\s*=") && Regex.IsMatch(init, @"\bEditingElementStyle\s*="),
                    $"{Path.GetFileName(file)}: DataGridCheckBoxColumn without ElementStyle/EditingElementStyle");
                Assert.Contains("GridCheckBox", init + ResolveHelpers(text, init));
            }
        }
        Assert.True(count >= 3, "table data bit columns + procedure parameter Output / ReadOnly");
    }

    private static int FindInitializerEnd(string text, int start)
    {
        var open = text.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return i + 1;
        }
        return text.Length;
    }

    // ElementStyle = GridCheckBoxStyle() → body of that helper.
    private static string ResolveHelpers(string text, string init)
    {
        var bodies = "";
        foreach (Match call in Regex.Matches(init, @"ElementStyle\s*=\s*(\w+)\(\)"))
        {
            var def = Regex.Match(text, @"Style\s+" + call.Groups[1].Value + @"\(\)\s*=>\s*([^;]+);");
            if (def.Success) bodies += def.Groups[1].Value;
        }
        return bodies;
    }

    [Fact]
    public void LocalImplicitCheckBoxStyles_InheritTheAppStyle()
    {
        // A window-level <Style TargetType="CheckBox"> without BasedOn would hide the app-wide look.
        foreach (var file in SourceFiles("*.xaml").Where(f => !f.EndsWith("Controls.xaml", StringComparison.Ordinal)))
        {
            foreach (var style in XDocument.Load(file).Descendants().Where(e => e.Name.LocalName == "Style"
                         && (string?)e.Attribute("TargetType") is "CheckBox" or "RadioButton"))
            {
                var basedOn = (string?)style.Attribute("BasedOn");
                Assert.True(basedOn is not null && (basedOn.Contains("x:Type") || basedOn.Contains("SqlCheckBox") || basedOn.Contains("SqlRadioButton")),
                    $"{Path.GetFileName(file)}: CheckBox/RadioButton style must be BasedOn the app style");
            }
        }
    }

    [Fact]
    public void BitColumns_KeepNullAsIndeterminate()
    {
        var grid = File.ReadAllText(AppFile("Views", "TableDataGrid.xaml.cs"));
        Assert.Contains("Converter = new NullableBoolConverter()", grid);
        Assert.Contains("IsThreeState = spec.IsNullable", grid);
        var conv = File.ReadAllText(AppFile("Views", "CellValueConverters.cs"));
        Assert.Contains("value is bool b ? b : null", conv);
        Assert.Contains("value is bool b ? b : DBNull.Value", conv);
    }

    [Fact]
    public void CheckBoxColors_HaveNoGreen()
    {
        var doc = Controls();
        foreach (var e in doc.Root!.Elements().Where(e => Key(e)?.StartsWith("Check.", StringComparison.Ordinal) == true
                     && e.Attribute("Color") is not null))
        {
            var hex = ((string)e.Attribute("Color")!).TrimStart('#');
            var r = int.Parse(hex[..2], NumberStyles.HexNumber);
            var g = int.Parse(hex[2..4], NumberStyles.HexNumber);
            var b = int.Parse(hex[4..6], NumberStyles.HexNumber);
            Assert.False(g > r + 8 && g > b + 8, $"{Key(e)} #{hex} looks green");
        }
    }
}
