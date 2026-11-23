using System.Collections.ObjectModel;

namespace SqlExpert.Core.Documents;

/// <summary>
/// Index math for drag-and-drop reordering of the Windows-bar document tabs.
/// Insertion uses the left/right half of each tab and works across wrapped rows.
/// </summary>
public static class TabReorder
{
    /// <summary>Bounds of one tab, in a shared coordinate space (e.g. the Windows bar).</summary>
    public readonly struct TabBounds
    {
        public TabBounds(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
        public double MidX => X + Width / 2;
        public double Right => X + Width;
        public double Bottom => Y + Height;

        public bool ContainsY(double y) => y >= Y && y <= Bottom;
    }

    /// <summary>
    /// Insertion slot for a drop at (<paramref name="x"/>, <paramref name="y"/>):
    /// the item currently at this index is pushed to the right
    /// (or the end of the list when the value equals the tab count).
    /// </summary>
    public static int InsertBeforeIndex(IReadOnlyList<TabBounds> tabs, double x, double y)
    {
        if (tabs is null || tabs.Count == 0) return 0;

        var row = RowIndices(tabs, y);
        if (row.Count == 0)
        {
            var guideY = NearestRowGuideY(tabs, y);
            row = RowIndices(tabs, guideY);
        }

        foreach (var i in row)
        {
            if (x < tabs[i].MidX)
                return i;
        }

        return row.Count == 0 ? tabs.Count : row[^1] + 1;
    }

    /// <summary>
    /// Target index for <see cref="ObservableCollection{T}.Move"/>, or null when the drop
    /// would not change the order (or the indices are out of range).
    /// </summary>
    public static int? MoveDestination(int sourceIndex, int insertBeforeIndex, int count)
    {
        if (count <= 0 || sourceIndex < 0 || sourceIndex >= count) return null;
        if (insertBeforeIndex < 0 || insertBeforeIndex > count) return null;
        var dest = insertBeforeIndex > sourceIndex ? insertBeforeIndex - 1 : insertBeforeIndex;
        if (dest < 0 || dest >= count || dest == sourceIndex) return null;
        return dest;
    }

    private static List<int> RowIndices(IReadOnlyList<TabBounds> tabs, double y)
    {
        var row = new List<int>();
        for (var i = 0; i < tabs.Count; i++)
        {
            if (tabs[i].ContainsY(y))
                row.Add(i);
        }
        return row;
    }

    private static double NearestRowGuideY(IReadOnlyList<TabBounds> tabs, double y)
    {
        var bestY = tabs[0].Y + tabs[0].Height / 2;
        var best = DistanceToBand(y, tabs[0].Y, tabs[0].Bottom);
        for (var i = 1; i < tabs.Count; i++)
        {
            var d = DistanceToBand(y, tabs[i].Y, tabs[i].Bottom);
            if (d < best)
            {
                best = d;
                bestY = tabs[i].Y + tabs[i].Height / 2;
            }
        }
        return bestY;
    }

    private static double DistanceToBand(double y, double top, double bottom)
    {
        if (y < top) return top - y;
        if (y > bottom) return y - bottom;
        return 0;
    }
}
