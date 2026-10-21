using SqlExpert.Core.Explorer;

namespace SqlExpert.Tests;

public class ExplorerNodeTextTests
{
    [Theory]
    [InlineData(505, " (505)")]
    [InlineData(0, " (0)")]
    [InlineData(12345, " (12345)")]
    public void CountText_IncludesSpaceAndParentheses(int count, string expected) =>
        Assert.Equal(expected, ExplorerNodeText.CountText(count));

    [Fact]
    public void CountText_NullWhenNoCount() => Assert.Null(ExplorerNodeText.CountText(null));

    [Fact]
    public void FullText_JoinsNameAndCount()
    {
        Assert.Equal("Tables (505)", ExplorerNodeText.FullText("Tables", 505));
        Assert.Equal("dbo.Clientes", ExplorerNodeText.FullText("dbo.Clientes", null));
    }

    [Fact]
    public void CountText_IgnoresCulture()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
            Assert.Equal(" (1234)", ExplorerNodeText.CountText(1234));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }
}
