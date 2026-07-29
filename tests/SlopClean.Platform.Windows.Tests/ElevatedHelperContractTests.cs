using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
using SlopClean.Platform.Windows;

namespace SlopClean.Platform.Windows.Tests;

public class ElevatedHelperContractTests
{
    [Fact]
    public void Pipe_security_allows_current_user_and_administrators()
    {
        var security = ElevatedPrivilegeBroker.CreatePipeSecurity();
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>()
            .Select(r => (SecurityIdentifier)r.IdentityReference)
            .ToArray();

        var user = WindowsIdentity.GetCurrent().User!;
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        Assert.Contains(user, rules);
        Assert.Contains(admins, rules);
    }

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

    [Fact]
    public async Task Broker_fails_quickly_when_helper_exits_without_ready_ipc()
    {
        var helper = Path.Combine(Path.GetTempPath(), $"SlopClean.FakeHelper.{Guid.NewGuid():N}.cmd");
        await File.WriteAllTextAsync(helper, "@echo off\r\nexit /b 1\r\n");
        try
        {
            var broker = new ElevatedPrivilegeBroker(helperPath: helper, elevate: false);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => broker.BeginElevatedSessionAsync(cts.Token));

            Assert.True(
                ex.Message.Contains("IPC", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("exited", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("ready", StringComparison.OrdinalIgnoreCase),
                ex.Message);
        }
        finally
        {
            File.Delete(helper);
        }
    }

    [Fact]
    public async Task Broker_completes_ready_handshake_with_unelevated_helper()
    {
        var helperPath = FindBuiltHelper();
        Assert.True(helperPath is not null, "Build SlopClean.Elevated before running this test.");

        var broker = new ElevatedPrivilegeBroker(helperPath: helperPath, elevate: false);
        await using var session = await broker.BeginElevatedSessionAsync(CancellationToken.None);

        var missing = Path.Combine(Path.GetTempPath(), $"slopclean-missing-{Guid.NewGuid():N}.tmp");
        var result = await session.ExecuteAsync(
            new OptimizationAction(
                "1",
                "temp-cleaner",
                "f",
                PrivilegedOperationCodes.DeleteFile,
                missing,
                Path.GetTempPath(),
                RequiredPrivilege.Elevated),
            CancellationToken.None);

        Assert.Equal(ApplyOutcome.Skipped, result.Outcome);
    }

    private static string? FindBuiltHelper()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "SlopClean.Elevated", "bin", "Release", "net10.0-windows10.0.19041.0", "SlopClean.Elevated.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "SlopClean.Elevated", "bin", "x64", "Release", "net10.0-windows10.0.19041.0", "SlopClean.Elevated.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "SlopClean.Elevated", "bin", "Debug", "net10.0-windows10.0.19041.0", "SlopClean.Elevated.exe")),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
