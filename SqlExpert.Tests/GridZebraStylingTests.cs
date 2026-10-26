using System.Text.RegularExpressions;

namespace SqlExpert.Tests;

/// <summary>
/// Light zebra striping on every DataGrid: set once in the implicit DataGrid style (Resources/Branding.xaml) and on the
/// grids built in code; no grid may override it with its own row background. Static checks on the sources, no GUI.
/// </summary>
public class GridZebraStylingTests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "SqlExpert.App");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(AppDir(), relative));

    private static string ImplicitDataGridStyle()
    {
        var m = Regex.Match(Read("Resources/Branding.xaml"), "<Style TargetType=\"DataGrid\">.*?\\n  </Style>", RegexOptions.Singleline);
        Assert.True(m.Success, "implicit DataGrid style not found");
        return m.Value;
    }

    [Fact]
    public void Implicit_DataGrid_style_alternates_rows()
    {
        var style = ImplicitDataGridStyle();
        Assert.Contains("<Setter Property=\"AlternationCount\" Value=\"2\"/>", style);
        Assert.Contains("<Setter Property=\"AlternatingRowBackground\" Value=\"{StaticResource GridAlternateRowBrush}\"/>", style);
    }

    [Fact]
    public void Stripe_color_is_very_light_neutral_cool_and_differs_from_current_row()
    {
        var branding = Read("Resources/Branding.xaml");
        var m = Regex.Match(branding, "x:Key=\"GridAlternateRowBrush\" Color=\"#([0-9A-Fa-f]{6})\"");
        Assert.True(m.Success);
        var rgb = Convert.ToInt32(m.Groups[1].Value, 16);
        int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
        Assert.True(r >= 0xF0 && g >= 0xF0 && b >= 0xF0, "stripe must be barely visible");
        Assert.True(b >= r && b >= g, "neutral-cool tint (no green / warm cast)");
        Assert.True(g <= b && g - r <= 4, "no green cast");
        var current = Regex.Match(branding, "<Color x:Key=\"GridCurrentRowColor\">#([0-9A-Fa-f]{6})</Color>");
        Assert.True(current.Success);
        Assert.NotEqual(current.Groups[1].Value.ToUpperInvariant(), m.Groups[1].Value.ToUpperInvariant());
    }

    [Fact]
    public void No_xaml_grid_overrides_the_row_background()
    {
        foreach (var file in Directory.EnumerateFiles(AppDir(), "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            if (name != "Branding.xaml")
            {
                Assert.DoesNotContain("RowBackground=", text);
                Assert.DoesNotContain("AlternationCount=", text);
                Assert.DoesNotContain("<DataGrid.Style>", text);
            }
            // Row styles may add event setters, but not a Background that would hide the stripe.
            foreach (Match row in Regex.Matches(text, "<Style TargetType=\"DataGridRow\".*?</Style>", RegexOptions.Singleline))
                Assert.DoesNotContain("Property=\"Background\"", row.Value);
        }
    }

    [Theory]
    [InlineData("Views/ForeignKeyLookupPopup.cs")]
    [InlineData("Views/SqlEditorView.xaml.cs")]
    public void Grids_built_in_code_alternate_rows(string file)
    {
        var text = Read(file);
        Assert.Contains("AlternationCount = 2", text);
        Assert.Contains("SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, \"GridAlternateRowBrush\")", text);
        Assert.DoesNotContain("RowBackground =", text);
    }
}
