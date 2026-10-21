using SqlExpert.Core.DataForms;
using SqlExpert.Core.Models;
using SqlExpert.Core.Services;

namespace SqlExpert.Tests;

public class NewRecordRulesTests
{
    [Fact]
    public void Blank_WhenEveryValueIsNull()
    {
        Assert.True(NewRecordRules.IsBlank(new object?[] { DBNull.Value, null, DBNull.Value }));
        Assert.True(NewRecordRules.IsBlank(Array.Empty<object?>()));
        Assert.False(NewRecordRules.IsBlank(new object?[] { DBNull.Value, "" })); // an empty string was typed
        Assert.False(NewRecordRules.IsBlank(new object?[] { 0 }));
    }

    [Theory]
    [InlineData(4, 5, true, false, true)]   // last row → append
    [InlineData(3, 5, true, false, false)]  // not the last row → normal move
    [InlineData(4, 5, false, false, false)] // read-only table
    [InlineData(4, 5, true, true, false)]   // already on an untouched new record
    [InlineData(-1, 0, true, false, true)]  // empty grid
    [InlineData(-1, 0, false, false, false)]
    public void AppendOnDown(int current, int count, bool canModify, bool lastIsBlankNew, bool expected) =>
        Assert.Equal(expected, NewRecordRules.ShouldAppendOnDown(current, count, canModify, lastIsBlankNew));

    [Theory]
    [InlineData(true, true, true, false, true)]   // blank and left → discard
    [InlineData(true, true, true, true, false)]   // still the current row
    [InlineData(true, true, false, false, false)] // filled in → pending INSERT
    [InlineData(true, false, true, false, false)] // already posted
    [InlineData(false, true, true, false, false)] // a record from the Insert button stays
    public void Discard(bool appended, bool stillNew, bool blank, bool current, bool expected) =>
        Assert.Equal(expected, NewRecordRules.ShouldDiscard(appended, stillNew, blank, current));

    [Fact]
    public void Marker_IsTheClassicStar() => Assert.Equal("*", NewRecordRules.NewRowMarker);

    private static TableDataSchema Pedidos() => new()
    {
        Schema = "dbo",
        Table = "Pedidos",
        Columns = new[]
        {
            new DataColumnInfo { Ordinal = 1, Name = "PedidoId", BaseTypeName = "int", IsIdentity = true, IsPrimaryKey = true, KeyOrdinal = 1 },
            new DataColumnInfo { Ordinal = 2, Name = "Data", BaseTypeName = "datetime2", IsNullable = false }, // NOT NULL DEFAULT SYSDATETIME()
            new DataColumnInfo { Ordinal = 3, Name = "Obs", BaseTypeName = "nvarchar", IsNullable = true },
            new DataColumnInfo { Ordinal = 4, Name = "Total", BaseTypeName = "decimal", IsComputed = true }
        }
    };

    [Fact]
    public void FilledNewRecord_InsertsOnlyTypedWritableColumns_SoDefaultsApply()
    {
        var values = new Dictionary<string, object?>
        {
            ["PedidoId"] = DBNull.Value, ["Data"] = DBNull.Value, ["Obs"] = "entregar cedo", ["Total"] = DBNull.Value
        };
        var st = TableDataSql.BuildInsert(Pedidos(), values);
        var insert = st.Sql.Split("SELECT")[0];
        Assert.Equal("INSERT INTO [dbo].[Pedidos] ([Obs]) VALUES (@p0); ", insert);
        Assert.Contains("SCOPE_IDENTITY()", st.Sql); // the posted row comes back with its identity / defaults
        Assert.True(st.ReturnsRow);
    }

    [Fact]
    public void AllNullNewRecord_UsesDefaultValues()
    {
        var st = TableDataSql.BuildInsert(Pedidos(), new Dictionary<string, object?> { ["Data"] = DBNull.Value, ["Obs"] = null });
        Assert.StartsWith("INSERT INTO [dbo].[Pedidos] DEFAULT VALUES;", st.Sql);
    }
}

/// <summary>Static checks of the ↓-appends-a-record wiring (WPF grid + view model).</summary>
public class NewRecordWiringAuditTests
{
    private static string App()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "SqlExpert.App");
    }

    [Fact]
    public void Grid_DownOnLastRowAppends_AndMarksNewRows()
    {
        var grid = File.ReadAllText(Path.Combine(App(), "Views", "TableDataGrid.xaml.cs"));
        Assert.Contains("e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.None && TryAppendOnDown()", grid);
        Assert.Contains("_vm.AppendRecord()", grid);
        Assert.Contains("Grid.LoadingRow += Grid_OnLoadingRow", grid);
        Assert.Contains("NewRecordRules.NewRowMarker", grid);
        Assert.Contains("DiscardBlankAppendedRecord()", grid); // on selection change
    }

    [Fact]
    public void ViewModel_DropsBlankAppendedRecord_OnPost_AndIgnoresItForPendingChanges()
    {
        var vm = File.ReadAllText(Path.Combine(App(), "ViewModels", "TableDataViewModel.cs"));
        Assert.Contains("DiscardBlankAppendedRecord(force: true)", vm);
        Assert.Contains("if (IsBlankAppended(row)) continue;", vm);
        Assert.Contains("NewRecordRules.ShouldAppendOnDown(", vm);
        Assert.Contains("NewRecordRules.ShouldDiscard(", vm);
    }
}
