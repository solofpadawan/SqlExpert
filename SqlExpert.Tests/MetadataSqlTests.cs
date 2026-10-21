using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.Tests;

public class MetadataSqlTests
{
    [Theory]
    [InlineData(ObjectKind.Table, "sys.tables")]
    [InlineData(ObjectKind.View, "sys.views")]
    [InlineData(ObjectKind.Procedure, "sys.procedures")]
    [InlineData(ObjectKind.Sequence, "sys.sequences")]
    [InlineData(ObjectKind.Role, "sys.database_principals")]
    [InlineData(ObjectKind.Function, "sys.objects")]
    [InlineData(ObjectKind.Trigger, "sys.triggers")]
    [InlineData(ObjectKind.UserDefinedType, "sys.types")]
    public void CountByKind_ReferencesExpectedCatalog(ObjectKind kind, string expectedFragment)
    {
        var sql = MetadataSql.CountByKind(kind);
        Assert.Contains(expectedFragment, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ObjectKind.Table)]
    [InlineData(ObjectKind.View)]
    [InlineData(ObjectKind.Procedure)]
    public void ListByKind_SelectsSchemaAndName(ObjectKind kind)
    {
        var sql = MetadataSql.ListByKind(kind);
        Assert.Contains("[Schema]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[Name]", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListDatabases_FiltersAccessibleOnline()
    {
        Assert.Contains("sys.databases", MetadataSql.ListDatabases, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HAS_DBACCESS", MetadataSql.ListDatabases, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TableColumns_IncludesMsDescriptionAndKeys()
    {
        Assert.Contains("MS_Description", MetadataSql.TableColumns, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsPrimaryKey", MetadataSql.TableColumns, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsForeignKey", MetadataSql.TableColumns, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@schema", MetadataSql.TableColumns, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@table", MetadataSql.TableColumns, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplorerKinds_MatchReferenceGroups()
    {
        Assert.Contains(ObjectKind.UserDefinedType, MetadataSql.ExplorerKinds);
        Assert.Contains(ObjectKind.Table, MetadataSql.ExplorerKinds);
        Assert.Contains(ObjectKind.Sequence, MetadataSql.ExplorerKinds);
        Assert.Equal(8, MetadataSql.ExplorerKinds.Length);
    }

    [Fact]
    public void CategoryDisplayName_Tables()
    {
        Assert.Equal("Tables", MetadataSql.CategoryDisplayName(ObjectKind.Table));
        Assert.Equal("Sequences", MetadataSql.CategoryDisplayName(ObjectKind.Sequence));
        Assert.Equal("User-Defined Types", MetadataSql.CategoryDisplayName(ObjectKind.UserDefinedType));
    }
}
