using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Planning;

namespace SlopClean.Core.Tests;

public class OptimizationPlanSessionTests
{
    [Fact]
    public void Session_stores_and_clears_plan()
    {
        var session = new OptimizationPlanSession();
        var finding = new ScanFinding(
            "f1", "temp-cleaner", "t", "file",
            @"C:\Temp\a.tmp", 10, FindingRisk.Low, "d",
            true, RequiredPrivilege.None, @"C:\Temp",
            new Dictionary<string, string>
            {
                [OptimizationAction.OperationCodeMetadataKey] = PrivilegedOperationCodes.DeleteFile
            });

        var plan = OptimizationPlan.FromFindings("temp-cleaner", "Temp Cleaner", [finding]);
        session.Set(plan);

        Assert.Same(plan, session.Current);
        Assert.True(plan.Changes[0].IsRestorable);

        session.Clear();
        Assert.Null(session.Current);
    }

    [Fact]
    public void Recycle_bin_plan_is_not_restorable()
    {
        var finding = new ScanFinding(
            "f1", "recycle-bin", "t", "bin",
            null, 10, FindingRisk.Low, "d",
            true, RequiredPrivilege.None, null,
            new Dictionary<string, string>
            {
                [OptimizationAction.OperationCodeMetadataKey] = PrivilegedOperationCodes.EmptyRecycleBin
            });

        var plan = OptimizationPlan.FromFindings("recycle-bin", "Recycle Bin", [finding]);
        Assert.False(plan.Changes[0].IsRestorable);
        Assert.Equal(0, plan.RestorableCount);
    }
}
