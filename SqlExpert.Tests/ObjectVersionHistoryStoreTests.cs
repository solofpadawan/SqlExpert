using SqlExpert.Core.Procedures;

namespace SqlExpert.Tests;

public class ObjectVersionHistoryStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sqlexpert-hist-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void SaveCompile_WritesBeforeAndAfter_NewestFirst()
    {
        var store = new ObjectVersionHistoryStore(_root);
        store.SaveCompile("reg1", "Db", "dbo", "P", null, "CREATE PROCEDURE dbo.P AS SELECT 1", new DateTime(2026, 1, 2, 10, 0, 0));
        var list = store.SaveCompile("reg1", "Db", "dbo", "P", "CREATE PROCEDURE dbo.P AS SELECT 1", "CREATE PROCEDURE dbo.P AS SELECT 2", new DateTime(2026, 1, 2, 11, 0, 0));

        Assert.Equal(3, list.Count);
        Assert.Equal(ObjectVersionHistoryStore.AfterKind, list[0].Kind);
        Assert.Equal(new DateTime(2026, 1, 2, 11, 0, 0), list[0].Timestamp);
        Assert.Equal(ObjectVersionHistoryStore.BeforeKind, list[1].Kind);
        Assert.Equal("CREATE PROCEDURE dbo.P AS SELECT 2", ObjectVersionHistoryStore.Read(list[0]));
        Assert.Equal("CREATE PROCEDURE dbo.P AS SELECT 1", ObjectVersionHistoryStore.Read(list[1]));
        Assert.Equal("Compilada", list[0].KindText);
        Assert.Equal("02/01/2026 11:00:00", list[0].TimestampText);
    }

    [Fact]
    public void Objects_AreKeptApart_AndNamesAreSafe()
    {
        var store = new ObjectVersionHistoryStore(_root);
        store.SaveCompile("reg1", "Db", "dbo", "A", null, "a");
        store.SaveCompile("reg1", "Db", "dbo", "B:x*?", null, "b");
        Assert.Single(store.List("reg1", "Db", "dbo", "A"));
        Assert.Single(store.List("reg1", "Db", "dbo", "B:x*?"));
        Assert.Empty(store.List("reg2", "Db", "dbo", "A"));
        Assert.StartsWith(_root, store.DirectoryFor("reg1", "Db", "dbo", "B:x*?"));
    }

    [Fact]
    public void Prune_KeepsTheLastCompiles()
    {
        var store = new ObjectVersionHistoryStore(_root, maxCompiles: 3);
        var t = new DateTime(2026, 3, 1, 8, 0, 0);
        for (var i = 0; i < 6; i++)
            store.SaveCompile("r", "Db", "dbo", "P", "old" + i, "new" + i, t.AddMinutes(i));
        var list = store.List("r", "Db", "dbo", "P");
        Assert.Equal(6, list.Count); // 3 compiles × (anterior + nova)
        Assert.Equal("new5", ObjectVersionHistoryStore.Read(list[0]));
        Assert.DoesNotContain(list, e => ObjectVersionHistoryStore.Read(e) is "new0" or "new1" or "new2");
    }

    [Fact]
    public void SameMillisecond_KeepsBoth()
    {
        var store = new ObjectVersionHistoryStore(_root);
        var t = new DateTime(2026, 3, 1, 8, 0, 0);
        store.SaveCompile("r", "Db", "dbo", "P", null, "first", t);
        var list = store.SaveCompile("r", "Db", "dbo", "P", null, "second", t);
        Assert.Equal(2, list.Count);
    }
}
