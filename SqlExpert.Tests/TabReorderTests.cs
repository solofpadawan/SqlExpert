using SqlExpert.Core.Documents;

namespace SqlExpert.Tests;

public class TabReorderTests
{
    private static TabReorder.TabBounds T(double x, double y, double w = 100, double h = 20) => new(x, y, w, h);

    // ===== InsertBeforeIndex (single row) =====

    private static IReadOnlyList<TabReorder.TabBounds> Row3() =>
        [T(0, 0), T(100, 0), T(200, 0)];

    [Theory]
    [InlineData(-10, 10, 0)]
    [InlineData(0, 10, 0)]
    [InlineData(40, 10, 0)]   // left half of first
    [InlineData(49, 10, 0)]
    [InlineData(50, 10, 1)]   // mid of first → after it
    [InlineData(90, 10, 1)]   // right half of first
    [InlineData(100, 10, 1)]
    [InlineData(140, 10, 1)]  // left half of second
    [InlineData(160, 10, 2)]  // right half of second
    [InlineData(240, 10, 2)]  // left half of last
    [InlineData(260, 10, 3)]  // right half of last
    [InlineData(400, 10, 3)]  // past the end
    public void InsertBeforeIndex_SingleRow_UsesHorizontalMidpoint(double x, double y, int expected) =>
        Assert.Equal(expected, TabReorder.InsertBeforeIndex(Row3(), x, y));

    [Fact]
    public void InsertBeforeIndex_Empty_IsZero() =>
        Assert.Equal(0, TabReorder.InsertBeforeIndex([], 10, 10));

    // ===== InsertBeforeIndex (wrapped rows) =====

    private static IReadOnlyList<TabReorder.TabBounds> TwoRows() =>
    [
        T(0, 0), T(100, 0),          // row 0
        T(0, 24), T(100, 24)         // row 1
    ];

    [Fact]
    public void InsertBeforeIndex_SecondRow_DoesNotUseFirstRow()
    {
        var tabs = TwoRows();
        Assert.Equal(2, TabReorder.InsertBeforeIndex(tabs, 10, 30));   // left half of first tab in row 1
        Assert.Equal(3, TabReorder.InsertBeforeIndex(tabs, 60, 30));   // right half of that tab
        Assert.Equal(4, TabReorder.InsertBeforeIndex(tabs, 180, 30));  // past last tab of row 1
    }

    [Fact]
    public void InsertBeforeIndex_FirstRow_IgnoresSecondRow()
    {
        var tabs = TwoRows();
        Assert.Equal(2, TabReorder.InsertBeforeIndex(tabs, 180, 8));   // right of last tab in row 0
        Assert.Equal(0, TabReorder.InsertBeforeIndex(tabs, 10, 8));
    }

    [Fact]
    public void InsertBeforeIndex_AboveOrBelow_UsesNearestRow()
    {
        var tabs = TwoRows();
        Assert.Equal(0, TabReorder.InsertBeforeIndex(tabs, 10, -8));   // above first row, left of first
        Assert.Equal(2, TabReorder.InsertBeforeIndex(tabs, 180, -8));  // above, past last in first row
        Assert.Equal(4, TabReorder.InsertBeforeIndex(tabs, 180, 80));  // below last row, past the end
        Assert.Equal(2, TabReorder.InsertBeforeIndex(tabs, 10, 80));   // below, left of first in last row
    }

    // ===== MoveDestination (ObservableCollection.Move index) =====

    [Theory]
    // [A B C D], source → insertBefore → expected dest (-1 = no-op)
    [InlineData(0, 0, -1)]  // drop on itself (left)
    [InlineData(0, 1, -1)]  // drop just after itself
    [InlineData(0, 2, 1)]   // A before C → [B A C D]
    [InlineData(0, 3, 2)]   // A before D → [B C A D]
    [InlineData(0, 4, 3)]   // A at end   → [B C D A]
    [InlineData(2, 0, 0)]   // C before A → [C A B D]
    [InlineData(2, 1, 1)]   // C before B → [A C B D]
    [InlineData(2, 2, -1)]
    [InlineData(2, 3, -1)]
    [InlineData(2, 4, 3)]   // C at end   → [A B D C]
    [InlineData(3, 0, 0)]   // D at start → [D A B C]
    [InlineData(3, 3, -1)]
    [InlineData(3, 4, -1)]
    public void MoveDestination_AdjustsForRemoval(int source, int insertBefore, int expected)
    {
        var dest = TabReorder.MoveDestination(source, insertBefore, 4);
        if (expected < 0) Assert.Null(dest);
        else Assert.Equal(expected, dest);
    }

    [Theory]
    [InlineData(-1, 0, 3)]
    [InlineData(3, 0, 3)]
    [InlineData(0, -1, 3)]
    [InlineData(0, 4, 3)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    public void MoveDestination_Invalid_IsNull(int source, int insertBefore, int count) =>
        Assert.Null(TabReorder.MoveDestination(source, insertBefore, count));

    [Fact]
    public void MoveDestination_TwoItems_SwapOrNoOp()
    {
        Assert.Equal(1, TabReorder.MoveDestination(0, 2, 2));
        Assert.Equal(0, TabReorder.MoveDestination(1, 0, 2));
        Assert.Null(TabReorder.MoveDestination(0, 1, 2));
        Assert.Null(TabReorder.MoveDestination(1, 2, 2));
    }
}
