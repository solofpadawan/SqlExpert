using SqlExpert.Core.Documents;
using Xunit;

namespace SqlExpert.Tests;

public class BareObjectNameTests
{
    [Theory]
    [InlineData("Clientes", "Clientes")]
    [InlineData("  Clientes ", "Clientes")]
    [InlineData("dbo.Clientes", "Clientes")]
    [InlineData("[dbo].[Clientes]", "Clientes")]
    [InlineData("[Clientes]", "Clientes")]
    [InlineData("[dbo].[Nome.Com.Ponto]", "Nome.Com.Ponto")]
    [InlineData("[dbo].[Com]]Colchete]", "Com]Colchete")]
    [InlineData("A_CAIXA_ARQUIVO", "A_CAIXA_ARQUIVO")]
    public void ReturnsTheNameWithoutSchemaOrBrackets(string input, string expected)
        => Assert.Equal(expected, WindowTitles.BareObjectName(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    public void EmptyName_GivesNull(string? input) => Assert.Null(WindowTitles.BareObjectName(input));

    [Fact]
    public void TitleBars_OfTableAndProcedure_HaveTheCopyButton()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "SqlExpert.App", "MainWindow.xaml"))) dir = Path.GetDirectoryName(dir)!;
        var xaml = File.ReadAllText(Path.Combine(dir, "SqlExpert.App", "MainWindow.xaml"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(xaml, "<views:CopyNameButton").Count);
    }
}
