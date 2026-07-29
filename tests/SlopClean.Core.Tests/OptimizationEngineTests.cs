using SlopClean.Core.Abstractions;
using SlopClean.Core.Backup;
using SlopClean.Core.Engine;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;
using SlopClean.Core.Safety;
using SlopClean.Core.Settings;
using SlopClean.Core.Tests.Fakes;

namespace SlopClean.Core.Tests;

public class OptimizationEngineTests
{
    [Fact]
    public async Task Apply_skips_action_blocked_by_safety_policy()
    {
        var fs = CreateFs();
        var module = new DeletingModule(fs);
        var engine = CreateEngine(fs, module, new FakeBroker());

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
        var engine = CreateEngine(fs, module, broker);

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
        Assert.Equal(1, broker.SessionCount);
        Assert.Equal(ApplyOutcome.Succeeded, result[0].Outcome);
        Assert.False(module.Applied);
    }

    [Fact]
    public async Task Apply_reuses_one_elevated_session_for_multiple_elevated_actions()
    {
        var fs = CreateFs();
        var module = new DeletingModule(fs);
        var broker = new FakeBroker();
        var engine = CreateEngine(fs, module, broker);

        fs.AddFile(@"C:\Windows\Temp\a.tmp", 10);
        fs.AddFile(@"C:\Windows\Temp\b.tmp", 10);
        var result = await engine.ApplySelectedAsync(
        [
            new OptimizationAction(
                "a1",
                module.Id,
                "f1",
                PrivilegedOperationCodes.DeleteFile,
                @"C:\Windows\Temp\a.tmp",
                @"C:\Windows\Temp",
                RequiredPrivilege.Elevated),
            new OptimizationAction(
                "a2",
                module.Id,
                "f2",
                PrivilegedOperationCodes.DeleteFile,
                @"C:\Windows\Temp\b.tmp",
                @"C:\Windows\Temp",
                RequiredPrivilege.Elevated)
        ], CancellationToken.None);

        Assert.Equal(1, broker.SessionCount);
        Assert.Equal(2, broker.ExecuteCount);
        Assert.All(result, r => Assert.Equal(ApplyOutcome.Succeeded, r.Outcome));
    }

    [Fact]
    public async Task Apply_sticky_elevated_session_failure_attempts_begin_exactly_once()
    {
        var fs = CreateFs();
        var module = new DeletingModule(fs);
        const string sticky = "Elevated helper failed to start (missing files or runtime). Path: 'x'.";
        var broker = new FakeBroker(beginError: sticky);
        var engine = CreateEngine(fs, module, broker);

        fs.AddFile(@"C:\Windows\Temp\a.tmp", 10);
        fs.AddFile(@"C:\Windows\Temp\b.tmp", 10);
        fs.AddFile(@"C:\Users\Test\AppData\Local\Temp\c.tmp", 10);

        var results = await engine.ApplySelectedAsync(
        [
            new OptimizationAction(
                "a1",
                module.Id,
                "f1",
                PrivilegedOperationCodes.DeleteFile,
                @"C:\Windows\Temp\a.tmp",
                @"C:\Windows\Temp",
                RequiredPrivilege.Elevated),
            new OptimizationAction(
                "a2",
                module.Id,
                "f2",
                PrivilegedOperationCodes.DeleteFile,
                @"C:\Users\Test\AppData\Local\Temp\c.tmp",
                @"C:\Users\Test\AppData\Local\Temp",
                RequiredPrivilege.None),
            new OptimizationAction(
                "a3",
                module.Id,
                "f3",
                PrivilegedOperationCodes.DeleteFile,
                @"C:\Windows\Temp\b.tmp",
                @"C:\Windows\Temp",
                RequiredPrivilege.Elevated)
        ], CancellationToken.None);

        Assert.Equal(1, broker.SessionCount);
        Assert.Equal(0, broker.ExecuteCount);
        Assert.Equal(ApplyOutcome.Failed, results[0].Outcome);
        Assert.Equal(sticky, results[0].Message);
        Assert.Equal(ApplyOutcome.Succeeded, results[1].Outcome);
        Assert.Equal(ApplyOutcome.Failed, results[2].Outcome);
        Assert.Equal(sticky, results[2].Message);
        Assert.True(module.Applied);
    }

    [Fact]
    public async Task Apply_commits_restore_point_only_on_success()
    {
        var fs = CreateFs();
        fs.AddFile(@"C:\Users\Test\AppData\Local\Temp\a.tmp", 10);
        var module = new DeletingModule(fs);
        var settings = new InMemorySettingsStore(@"C:\Backups");
        var store = new RestorePointStore(fs, settings);
        var backup = new BackupService(fs, new FakeRegistryStore(fs), store, new SafetyPolicy(fs));
        var engine = new OptimizationEngine(
            new ModuleRegistry([module]),
            new SafetyPolicy(fs),
            new FakeBroker(),
            backupService: backup,
            restorePointStore: store);

        var result = await engine.ApplySelectedAsync(
        [
            new OptimizationAction(
                "a1",
                module.Id,
                "f1",
                PrivilegedOperationCodes.DeleteFile,
                @"C:\Users\Test\AppData\Local\Temp\a.tmp",
                @"C:\Users\Test\AppData\Local\Temp",
                RequiredPrivilege.None)
        ], CancellationToken.None);

        Assert.Equal(ApplyOutcome.Succeeded, result[0].Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result[0].RestoreTokenId));
        var committed = await store.ListCommittedAsync();
        Assert.Contains(committed, m => m.Id == result[0].RestoreTokenId);

        var restore = await engine.RestoreAsync(result[0].RestoreTokenId!, CancellationToken.None);
        Assert.Equal(ApplyOutcome.Succeeded, restore.Outcome);
        Assert.True(fs.FileExists(@"C:\Users\Test\AppData\Local\Temp\a.tmp"));
    }

    [Fact]
    public async Task ApplyPlan_discards_backup_when_apply_fails()
    {
        var fs = CreateFs();
        fs.AddFile(@"C:\Users\Test\AppData\Local\Temp\a.tmp", 10);
        var module = new FailingModule();
        var settings = new InMemorySettingsStore(@"C:\Backups");
        var store = new RestorePointStore(fs, settings);
        var backup = new BackupService(fs, new FakeRegistryStore(fs), store, new SafetyPolicy(fs));
        var engine = new OptimizationEngine(
            new ModuleRegistry([module]),
            new SafetyPolicy(fs),
            new FakeBroker(),
            backupService: backup,
            restorePointStore: store);

        var finding = new ScanFinding(
            "f1", module.Id, "t", "a.tmp",
            @"C:\Users\Test\AppData\Local\Temp\a.tmp", 10, FindingRisk.Low, "x",
            true, RequiredPrivilege.None, @"C:\Users\Test\AppData\Local\Temp",
            new Dictionary<string, string>
            {
                [OptimizationAction.OperationCodeMetadataKey] = PrivilegedOperationCodes.DeleteFile
            });
        var plan = OptimizationPlan.FromFindings(module.Id, module.Name, [finding]);
        var result = await engine.ApplyPlanAsync(plan, CancellationToken.None);

        Assert.Equal(ApplyOutcome.Failed, result[0].Outcome);
        Assert.Empty(await store.ListCommittedAsync());
    }

    [Fact]
    public async Task ApplyPlan_reports_running_then_terminal_progress_per_item()
    {
        var fs = CreateFs();
        fs.AddFile(@"C:\Users\Test\AppData\Local\Temp\a.tmp", 10);
        fs.AddFile(@"C:\Users\Test\AppData\Local\Temp\b.tmp", 20);
        var module = new DeletingModule(fs);
        var engine = CreateEngine(fs, module, new FakeBroker());
        var reports = new List<ApplyProgress>();
        var progress = new Progress<ApplyProgress>(reports.Add);

        var findings = new[]
        {
            new ScanFinding(
                "f1", module.Id, "t", "a.tmp",
                @"C:\Users\Test\AppData\Local\Temp\a.tmp", 10, FindingRisk.Low, "a",
                true, RequiredPrivilege.None, @"C:\Users\Test\AppData\Local\Temp",
                new Dictionary<string, string>
                {
                    [OptimizationAction.OperationCodeMetadataKey] = PrivilegedOperationCodes.DeleteFile
                }),
            new ScanFinding(
                "f2", module.Id, "t", "b.tmp",
                @"C:\Users\Test\AppData\Local\Temp\b.tmp", 20, FindingRisk.Low, "b",
                true, RequiredPrivilege.None, @"C:\Users\Test\AppData\Local\Temp",
                new Dictionary<string, string>
                {
                    [OptimizationAction.OperationCodeMetadataKey] = PrivilegedOperationCodes.DeleteFile
                })
        };
        var plan = OptimizationPlan.FromFindings(module.Id, module.Name, findings);
        var results = await engine.ApplyPlanAsync(plan, progress, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Contains(reports, r => r.DisplayName == "a.tmp" && r.State == ApplyItemState.Running);
        Assert.Contains(reports, r => r.DisplayName == "a.tmp" && r.State == ApplyItemState.Succeeded);
        Assert.Contains(reports, r => r.DisplayName == "b.tmp" && r.State == ApplyItemState.Succeeded);
        Assert.Equal(2, reports.Last(r => r.State == ApplyItemState.Succeeded).CompletedCount);
    }

    [Fact]
    public async Task Scan_is_cancelled_when_token_cancelled()
    {
        var fs = CreateFs();
        var module = new SlowScanModule();
        var engine = CreateEngine(fs, module, new FakeBroker());

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in engine.ScanModuleAsync(module.Id, null, null, cts.Token))
            {
            }
        });
    }

    private static OptimizationEngine CreateEngine(IFileSystem fs, IModule module, IPrivilegeBroker broker)
        => new(new ModuleRegistry([module]), new SafetyPolicy(fs), broker);

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
        fs.AddDirectory(@"C:\Users\Test\AppData\Local\Temp");
        fs.AddDirectory(@"C:\Backups");
        return fs;
    }

    private sealed class InMemorySettingsStore(string backupDirectory) : IAppSettingsStore
    {
        public AppSettings Current { get; private set; } = new() { BackupDirectory = backupDirectory };

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBroker(string? beginError = null) : IPrivilegeBroker
    {
        public bool Called { get; private set; }
        public int SessionCount { get; private set; }
        public int ExecuteCount { get; private set; }

        public async Task<ApplyResult> ExecuteElevatedAsync(OptimizationAction action, CancellationToken cancellationToken)
        {
            await using var session = await BeginElevatedSessionAsync(cancellationToken);
            return await session.ExecuteAsync(action, cancellationToken);
        }

        public Task<IElevatedPrivilegeSession> BeginElevatedSessionAsync(CancellationToken cancellationToken)
        {
            Called = true;
            SessionCount++;
            if (beginError is not null)
            {
                throw new InvalidOperationException(beginError);
            }

            return Task.FromResult<IElevatedPrivilegeSession>(new FakeSession(this));
        }

        private sealed class FakeSession(FakeBroker owner) : IElevatedPrivilegeSession
        {
            public Task<ApplyResult> ExecuteAsync(OptimizationAction action, CancellationToken cancellationToken)
            {
                owner.ExecuteCount++;
                return Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, 1, "elevated"));
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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

    private sealed class FailingModule : IScannableModule, IApplicableModule
    {
        public string Id => "failing";
        public string Name => "Failing";
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
            => Task.FromResult(ApplyResult.Failed(action.Id, action.FindingId, "boom"));
    }
}
