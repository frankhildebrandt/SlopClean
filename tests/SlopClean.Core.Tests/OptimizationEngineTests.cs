using SlopClean.Core.Abstractions;
using SlopClean.Core.Engine;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;
using SlopClean.Core.Safety;
using SlopClean.Core.Tests.Fakes;

namespace SlopClean.Core.Tests;

public class OptimizationEngineTests
{
    [Fact]
    public async Task Apply_skips_action_blocked_by_safety_policy()
    {
        var fs = CreateFs();
        var module = new DeletingModule(fs);
        var engine = new OptimizationEngine(
            new ModuleRegistry([module]),
            new SafetyPolicy(fs),
            new FakeBroker());

        var result = await engine.ApplySelectedAsync(
        [
            new OptimizationAction(
                "a1",
                module.Id,
                "f1",
                PrivilegedOperationCodes.DeleteFile,
                @"C:\Windows\System32\kernel32.dll",
                @"C:\Windows\System32",
                RequiredPrivilege.None)
        ], CancellationToken.None);

        Assert.Equal(ApplyOutcome.Skipped, result[0].Outcome);
        Assert.False(module.Applied);
    }

    [Fact]
    public async Task Apply_routes_elevated_actions_to_broker()
    {
        var fs = CreateFs();
        var module = new DeletingModule(fs);
        var broker = new FakeBroker();
        var engine = new OptimizationEngine(
            new ModuleRegistry([module]),
            new SafetyPolicy(fs),
            broker);

        fs.AddFile(@"C:\Windows\Temp\a.tmp", 10);
        var result = await engine.ApplySelectedAsync(
        [
            new OptimizationAction(
                "a1",
                module.Id,
                "f1",
                PrivilegedOperationCodes.DeleteFile,
                @"C:\Windows\Temp\a.tmp",
                @"C:\Windows\Temp",
                RequiredPrivilege.Elevated)
        ], CancellationToken.None);

        Assert.True(broker.Called);
        Assert.Equal(ApplyOutcome.Succeeded, result[0].Outcome);
        Assert.False(module.Applied);
    }

    [Fact]
    public async Task Scan_is_cancelled_when_token_cancelled()
    {
        var fs = CreateFs();
        var module = new SlowScanModule();
        var engine = new OptimizationEngine(
            new ModuleRegistry([module]),
            new SafetyPolicy(fs),
            new FakeBroker());

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in engine.ScanModuleAsync(module.Id, null, null, cts.Token))
            {
            }
        });
    }

    private static FakeFileSystem CreateFs()
    {
        var fs = new FakeFileSystem();
        fs.Folders[SpecialFolderKind.Windows] = @"C:\Windows";
        fs.Folders[SpecialFolderKind.System] = @"C:\Windows\System32";
        fs.Folders[SpecialFolderKind.UserProfile] = @"C:\Users\Test";
        fs.Folders[SpecialFolderKind.ApplicationData] = @"C:\Users\Test\AppData\Roaming";
        fs.Folders[SpecialFolderKind.LocalApplicationData] = @"C:\Users\Test\AppData\Local";
        fs.Folders[SpecialFolderKind.CommonApplicationData] = @"C:\ProgramData";
        fs.Folders[SpecialFolderKind.UserTemp] = @"C:\Users\Test\AppData\Local\Temp";
        fs.Folders[SpecialFolderKind.WindowsTemp] = @"C:\Windows\Temp";
        fs.AddDirectory(@"C:\Windows\Temp");
        return fs;
    }

    private sealed class FakeBroker : IPrivilegeBroker
    {
        public bool Called { get; private set; }

        public Task<ApplyResult> ExecuteElevatedAsync(OptimizationAction action, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, 1, "elevated"));
        }
    }

    private sealed class DeletingModule(IFileSystem fs) : IScannableModule, IApplicableModule
    {
        public bool Applied { get; private set; }
        public string Id => "deleting";
        public string Name => "Deleting";
        public string Description => "";
        public ModuleCategory Category => ModuleCategory.Cleanup;
        public IReadOnlyList<IModuleParameter> Parameters => [];

        public async IAsyncEnumerable<ScanFinding> ScanAsync(
            IReadOnlyDictionary<string, object?> parameters,
            IProgress<ScanProgress>? progress,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }

        public Task<ApplyResult> ApplyAsync(OptimizationAction action, CancellationToken cancellationToken)
        {
            Applied = true;
            fs.DeleteFile(action.Path!);
            return Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, 1, "ok"));
        }
    }

    private sealed class SlowScanModule : IScannableModule
    {
        public string Id => "slow";
        public string Name => "Slow";
        public string Description => "";
        public ModuleCategory Category => ModuleCategory.Analysis;
        public IReadOnlyList<IModuleParameter> Parameters => [];

        public async IAsyncEnumerable<ScanFinding> ScanAsync(
            IReadOnlyDictionary<string, object?> parameters,
            IProgress<ScanProgress>? progress,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
            yield break;
        }
    }
}
