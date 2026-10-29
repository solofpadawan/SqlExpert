using System.Text.RegularExpressions;
using Xunit;

namespace SqlExpert.Tests;

/// <summary>The space after the last grid column is plain background (no filler header, no row stripe).</summary>
public class GridFillerStylingTests
{
    private static string AppFile(string rel)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "SqlExpert.App", "MainWindow.xaml"))) dir = Path.GetDirectoryName(dir)!;
        return File.ReadAllText(Path.Combine(dir, "SqlExpert.App", rel));
    }

    [Fact]
    public void FillerHeader_IsBlank_AndRowBackground_EndsAtLastColumn()
    {
        var b = AppFile(Path.Combine("Resources", "Branding.xaml"));
        Assert.Contains("<Trigger Property=\"DisplayIndex\" Value=\"-1\">", b);
        Assert.Contains("<Style TargetType=\"DataGridRow\">", b);
        Assert.Contains("<Border Grid.Column=\"1\" HorizontalAlignment=\"Left\" Background=\"{TemplateBinding Background}\"", b);
        Assert.Contains("<DataGridRowHeader", b);
        Assert.Contains("<DataGridDetailsPresenter", b);
    }

    [Fact]
    public void LocalRowStyles_AreBasedOnTheSharedOne()
    {
        var xaml = AppFile("MainWindow.xaml");
        var all = Regex.Matches(xaml, "<Style TargetType=\"DataGridRow\"[^>]*>");
        Assert.NotEmpty(all);
        Assert.All(all, m => Assert.Contains("BasedOn=\"{StaticResource {x:Type DataGridRow}}\"", m.Value));
    }
}
