using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.Tests;

public class DdlScriptBuilderTests
{
    [Fact]
    public void BuildCreateTable_IncludesPkAndTypes()
    {
        var meta = new TableMetadata
        {
            Ref = new ObjectRef("db", "dbo", "CLIENTES", ObjectKind.Table),
            Columns = new[]
            {
                new ColumnInfo
                {
                    Ordinal = 1, Name = "ID", DataType = "int", IsPrimaryKey = true, IsNullable = false
                },
                new ColumnInfo
                {
                    Ordinal = 2, Name = "NOME", DataType = "nvarchar", Size = 100, IsNullable = false,
                    Description = "Nome do cliente"
                },
                new ColumnInfo
                {
                    Ordinal = 3, Name = "NOTAS", DataType = "nvarchar", Size = -1, IsNullable = true
                }
            }
        };

        var ddl = DdlScriptBuilder.BuildCreateTable(meta);
        Assert.Contains("CREATE TABLE [dbo].[CLIENTES]", ddl);
        Assert.Contains("[ID] int NOT NULL", ddl);
        Assert.Contains("[NOME] nvarchar(100) NOT NULL", ddl);
        Assert.Contains("[NOTAS] nvarchar(max) NULL", ddl);
        Assert.Contains("PRIMARY KEY ([ID])", ddl);
        Assert.Contains("MS_Description", ddl);
        Assert.Contains("Nome do cliente", ddl);
    }

    [Theory]
    [InlineData("nvarchar", 50, null, "nvarchar(50)")]
    [InlineData("nvarchar", -1, null, "nvarchar(max)")]
    [InlineData("decimal", 18, 2, "decimal(18,2)")]
    [InlineData("int", null, null, "int")]
    public void FormatType(string type, int? size, int? scale, string expected)
    {
        var col = new ColumnInfo { DataType = type, Size = size, Scale = scale };
        Assert.Equal(expected, DdlScriptBuilder.FormatType(col));
    }
}
