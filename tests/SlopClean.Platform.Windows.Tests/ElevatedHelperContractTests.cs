using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
using SlopClean.Platform.Windows;

namespace SlopClean.Platform.Windows.Tests;

public class ElevatedHelperContractTests
{
    [Fact]
    public void Safety_policy_rejects_unknown_operation_before_helper()
    {
        var fs = new WindowsFileSystem();
        var policy = new SafetyPolicy(fs);
        var action = new OptimizationAction(
            "1",
            "x",
            "y",
            "rm-rf-system",
            Path.GetTempPath(),
            Path.GetTempPath(),
            RequiredPrivilege.Elevated);

        Assert.False(policy.ValidateAction(action).IsAllowed);
        Assert.DoesNotContain("rm-rf-system", PrivilegedOperationCodes.All);
    }

    [Fact]
    public async Task Broker_fails_when_helper_missing()
    {
        var broker = new ElevatedPrivilegeBroker(helperPath: Path.Combine(Path.GetTempPath(), "missing-helper.exe"));
        var action = new OptimizationAction(
            "1",
            "temp-cleaner",
            "f",
            PrivilegedOperationCodes.DeleteFile,
            Path.Combine(Path.GetTempPath(), "nope.txt"),
            Path.GetTempPath(),
            RequiredPrivilege.Elevated);

        var result = await broker.ExecuteElevatedAsync(action, CancellationToken.None);
        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
