using SqlExpert.Core.Commands;

namespace SqlExpert.Tests;

public class CommandCatalogTests
{
    [Fact]
    public void Catalog_HasManyCommands()
    {
        Assert.True(CommandCatalog.All.Count >= 200);
    }

    [Fact]
    public void OpenMenu_ContainsCreateSiud()
    {
        var siud = CommandCatalog.ByCategory("OpenMenu")
            .FirstOrDefault(c => c.Caption.Contains("SIUD", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(siud);
        Assert.Contains("SELECT", siud!.SqlServerMapping ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sheduler_MapsToSqlAgent()
    {
        var task = CommandCatalog.ByCategory("Sheduler")
            .FirstOrDefault(c => c.Caption.Contains("task", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(task);
        Assert.Contains("SQL Agent", task!.SqlServerMapping ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Find_ById_Works()
    {
        var first = CommandCatalog.All[0];
        Assert.Same(first, CommandCatalog.Find(first.Id));
    }
}
