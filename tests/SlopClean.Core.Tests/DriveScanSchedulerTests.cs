using SlopClean.Core.Engine;

namespace SlopClean.Core.Tests;

public class DriveScanSchedulerTests
{
    [Fact]
    public async Task Serializes_same_drive()
    {
        await using var scheduler = new DriveScanScheduler();
        var first = await scheduler.AcquireAsync(@"C:\", CancellationToken.None);
        var secondTask = scheduler.AcquireAsync(@"C:\", CancellationToken.None);
        Assert.False(secondTask.IsCompleted);
        first.Dispose();
        using var second = await secondTask;
        Assert.True(secondTask.IsCompletedSuccessfully);
    }
}
