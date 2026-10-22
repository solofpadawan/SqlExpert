using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SqlExpert.Tests;

/// <summary>
/// Data-driven text keeps its underscores. WPF treats the first '_' of string content as an access key wherever a
/// ContentPresenter has RecognizesAccessKey="True" (default DataGrid column header, tab headers), so a column
/// FK_ORDER would read FKORDER. Static audit of the App XAML / code; no GUI is started.
/// </summary>
public class UnderscoreTextAuditTests
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

    private static IEnumerable<string> AppSources(string pattern)
    {
        var sep = Path.DirectorySeparatorChar;
        return Directory.GetFiles(AppFile(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"));
    }

    [Fact]
    public void ColumnHeaders_ShowStringHeadersAsPlainText()
    {
        var doc = XDocument.Load(AppFile("Resources", "Branding.xaml"));
        var header = doc.Root!.Elements().Single(e => e.Name.LocalName == "Style" && e.Attribute(X + "Key") is null
            && (string?)e.Attribute("TargetType") == "DataGridColumnHeader");
        Assert.Contains(header.Elements(), s => s.Name.LocalName == "Setter"
            && (string?)s.Attribute("Property") == "ContentTemplateSelector"
            && ((string?)s.Attribute("Value"))!.Contains("PlainTextTemplateSelector.Instance"));

        var selector = File.ReadAllText(AppFile("Views", "PlainTextTemplateSelector.cs"));
        Assert.Contains("item is string ? PlainText : null", selector);
        Assert.Contains("typeof(TextBlock)", selector);
        Assert.DoesNotContain("AccessText", selector);
    }

    [Fact]
    public void ColumnHeaderStyles_AreBasedOnTheImplicitHeaderStyle()
    {
        // A header style that is not BasedOn the implicit one would lose the plain-text selector.
        foreach (var file in AppSources("*.cs"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"new Style\(typeof\(DataGridColumnHeader\)(?<rest>[^;]*)"))
                Assert.True(m.Groups["rest"].Value.Contains("typeof(DataGridColumnHeader)"),
                    $"{Path.GetFileName(file)}: header style must be BasedOn the implicit DataGridColumnHeader style");

        foreach (var file in AppSources("*.xaml"))
        {
            var doc = XDocument.Load(file);
            foreach (var s in doc.Descendants().Where(e => e.Name.LocalName == "Style"
                         && ((string?)e.Attribute("TargetType"))?.Contains("DataGridColumnHeader") == true
                         && e.Attribute(X + "Key") is not null))
                Assert.True(s.Attribute("BasedOn") is not null, $"{Path.GetFileName(file)}: keyed header style without BasedOn");
        }
    }

    [Fact]
    public void Tabs_DoNotTreatUnderscoresAsAccessKeys()
    {
        var doc = XDocument.Load(AppFile("Themes", "Controls.xaml"));
        var tab = doc.Root!.Elements().Single(e => e.Name.LocalName == "Style" && e.Attribute(X + "Key") is null
            && (string?)e.Attribute("TargetType") == "TabItem");
        var cp = tab.Descendants().Single(e => e.Name.LocalName == "ContentPresenter" && (string?)e.Attribute("ContentSource") == "Header");
        Assert.Equal("False", (string?)cp.Attribute("RecognizesAccessKey"));

        // No tab header relies on a mnemonic.
        foreach (var file in AppSources("*.xaml"))
            foreach (var ti in XDocument.Load(file).Descendants().Where(e => e.Name.LocalName == "TabItem"))
                Assert.DoesNotContain("_", (string?)ti.Attribute("Header") ?? "");
    }

    [Fact]
    public void WindowsMenu_EscapesUnderscoresOfDocumentTitles()
    {
        var code = File.ReadAllText(AppFile("MainWindow.xaml.cs"));
        Assert.Contains("doc.Title.Replace(\"_\", \"__\")", code);
    }
}