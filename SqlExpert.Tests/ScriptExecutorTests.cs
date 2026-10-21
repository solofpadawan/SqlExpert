using SqlExpert.Core.Services;

namespace SqlExpert.Tests;

public class ScriptExecutorTests
{
    [Fact]
    public void SplitBatches_SplitsOnGo()
    {
        var script = "SELECT 1;\nGO\nSELECT 2;\ngo\nSELECT 3;";
        var batches = ScriptExecutor.SplitBatches(script);
        Assert.Equal(3, batches.Count);
        Assert.Contains("SELECT 1", batches[0]);
        Assert.Contains("SELECT 2", batches[1]);
        Assert.Contains("SELECT 3", batches[2]);
    }

    [Fact]
    public void SplitBatches_IgnoresEmpty()
    {
        var batches = ScriptExecutor.SplitBatches("GO\n\nGO\n");
        Assert.Empty(batches);
    }
}
