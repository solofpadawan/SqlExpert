using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SqlExpert.Tests;

/// <summary>
/// Static audit (no GUI is started): the foreign-key lookup popup reuses the Data grid navigator look
/// (shared toolbar/button styles, the same icons and tooltips), and every grid shows its current row
/// (inactive selection) in a soft light blue set once in the implicit DataGrid style.
/// </summary>
public class ForeignKeyLookupStyleAuditTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "SqlExpert.App");
    }

    private static string Popup() => File.ReadAllText(Path.Combine(AppDir(), "Views", "ForeignKeyLookupPopup.cs"));

    private static HashSet<string> Keys(string relPath) =>
        XDocument.Load(Path.Combine(AppDir(), relPath)).Descendants()
            .Select(e => (string?)e.Attribute(X + "Key")).Where(k => k is not null).Select(k => k!).ToHashSet();

    [Theory]
    [InlineData("DocTitleBar")]
    [InlineData("DocTitleText")]
    [InlineData("TitleCloseButton")]
    [InlineData("InnerToolBarBand")]
    [InlineData("InnerTrayToolBar")]
    [InlineData("FlatIconButton")]
    [InlineData("FlatToolButton")]
    public void Popup_UsesSharedThemeStyle(string styleKey)
    {
        Assert.Contains(styleKey, Keys(Path.Combine("Themes", "Controls.xaml")));
        Assert.Contains($"SetResourceReference(FrameworkElement.StyleProperty, \"{styleKey}\")", Popup());
    }

    [Theory]
    [InlineData("Icon.First", "First")]
    [InlineData("Icon.Prior", "Prior")]
    [InlineData("Icon.Next", "Next")]
    [InlineData("Icon.Last", "Last")]
    [InlineData("Icon.Refresh", "Refresh (recarregar do servidor)")]
    public void Popup_Navigator_MatchesDataGridNavigator(string icon, string tip)
    {
        Assert.Contains(icon, Keys(Path.Combine("Themes", "Icons.xaml")));
        var grid = File.ReadAllText(Path.Combine(AppDir(), "Views", "TableDataGrid.xaml"));
        Assert.Matches(new Regex($"Style=\"\\{{StaticResource FlatIconButton\\}}\" ToolTip=\"{Regex.Escape(tip)}\"[^>]*><views:IconImage Icon=\"\\{{StaticResource {Regex.Escape(icon)}\\}}\""), grid);
        Assert.Contains($"NavButton(\"{icon}\", \"{tip}\",", Popup());
    }

    [Fact]
    public void Popup_FetchAll_IsFlatIconAndTextButton()
    {
        var src = Popup();
        Assert.Contains("Icon.FetchAll", Keys(Path.Combine("Themes", "Icons.xaml")));
        Assert.Contains("ToolButton(\"Icon.FetchAll\", \"Fetch all\",", src);
        Assert.Contains("SetResourceReference(IconImage.IconProperty, iconKey)", src);
        Assert.Contains("ToolBar.SetOverflowMode(_fetchAll, OverflowMode.Never)", src);
    }

    [Fact]
    public void Popup_HasNoGlyphButtonsOrHardCodedChromeColors()
    {
        var src = Popup();
        Assert.DoesNotContain("Segoe UI Symbol", src);
        Assert.DoesNotContain("Color.FromRgb", src);
        Assert.DoesNotMatch(new Regex("Content = \"[⏮⏭◀▶⟳↻]\""), src);
        Assert.Contains("SetResourceReference(Border.BorderBrushProperty, \"Ui.Border\")", src);
    }

    [Fact]
    public void Grids_CurrentRow_IsSoftLightBlue_InImplicitDataGridStyle()
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), "Resources", "Branding.xaml"));
        var color = doc.Descendants().Single(e => e.Name.LocalName == "Color" && (string?)e.Attribute(X + "Key") == "GridCurrentRowColor");
        var hex = color.Value.Trim().TrimStart('#');
        Assert.Equal(6, hex.Length);
        int r = int.Parse(hex[..2], NumberStyles.HexNumber), g = int.Parse(hex[2..4], NumberStyles.HexNumber), b = int.Parse(hex[4..], NumberStyles.HexNumber);
        Assert.True(r >= 0xC8 && g >= 0xD8 && b >= 0xF0, $"#{hex} is not light enough");
        Assert.True(b - r >= 0x14, $"#{hex} is not bluish (would look grey)");

        var style = doc.Root!.Elements().Single(e => e.Name.LocalName == "Style" && (string?)e.Attribute("TargetType") == "DataGrid" && e.Attribute(X + "Key") is null);
        var brushes = style.Descendants().Where(e => e.Name.LocalName == "SolidColorBrush").ToList();
        Assert.Contains(brushes, e => ((string?)e.Attribute(X + "Key") ?? "").Contains("SystemColors.InactiveSelectionHighlightBrushKey")
                                      && (string?)e.Attribute("Color") == "{StaticResource GridCurrentRowColor}");
        Assert.Contains(brushes, e => ((string?)e.Attribute(X + "Key") ?? "").Contains("SystemColors.InactiveSelectionHighlightTextBrushKey"));
        // The focused selection colour is left to the system.
        Assert.DoesNotContain(brushes, e => ((string?)e.Attribute(X + "Key") ?? "").Contains("SystemColors.HighlightBrushKey"));

        // <null> keeps its red on the current row: the selection-foreground trigger only applies to the focused grid.
        var nullStyle = doc.Descendants().Single(e => (string?)e.Attribute(X + "Key") == "NullAwareCellText");
        Assert.Contains(nullStyle.Descendants().Where(e => e.Name.LocalName == "Condition"),
            c => ((string?)c.Attribute("Binding") ?? "").Contains("Selector.IsSelectionActive"));
    }
}
