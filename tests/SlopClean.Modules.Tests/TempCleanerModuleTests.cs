using SlopClean.Core.Abstractions;
using SlopClean.Core.Backup;
using SlopClean.Core.Engine;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
using SlopClean.Core.Settings;
using SlopClean.Modules;
using SlopClean.Modules.Tests.Fakes;

namespace SlopClean.Modules.Tests;

public class TempCleanerModuleTests
{
    [Fact]
    public async Task Scan_finds_user_temp_files()
    {
        var fs = CreateFs();
        fs.AddFile(@"C:\Users\Test\AppData\Local\Temp\old.tmp", 1234);
        var module = new TempCleanerModule(fs, new SafetyPolicy(fs));

        var findings = new List<ScanFinding>();
        await foreach (var finding in module.ScanAsync(
                           new Dictionary<string, object?>
                           {
                               ["IncludeUserTemp"] = true,
                               ["IncludeWindowsTemp"] = false,
                               ["MinAgeDays"] = 0
                           },
                           null,
                           CancellationToken.None))
        {
            findings.Add(finding);
        }

        Assert.Contains(findings, f => f.Path!.EndsWith("old.tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Apply_deletes_file()
    {
        var fs = CreateFs();
        fs.AddFile(@"C:\Users\Test\AppData\Local\Temp\old.tmp", 50);
        var module = new TempCleanerModule(fs, new SafetyPolicy(fs));
        var action = new OptimizationAction(
            "a", TempCleanerModule.ModuleId, "f",
            PrivilegedOperationCodes.DeleteFile,
            @"C:\Users\Test\AppData\Local\Temp\old.tmp",
            @"C:\Users\Test\AppData\Local\Temp",
            RequiredPrivilege.None);

        var result = await module.ApplyAsync(action, CancellationToken.None);
        Assert.Equal(ApplyOutcome.Succeeded, result.Outcome);
        Assert.False(fs.FileExists(@"C:\Users\Test\AppData\Local\Temp\old.tmp"));
    }

    [Fact]
    public async Task Engine_apply_backs_up_and_restores_temp_file()
    {
        var fs = CreateFs();
        fs.AddFile(@"C:\Users\Test\AppData\Local\Temp\old.tmp", 50);
        var module = new TempCleanerModule(fs, new SafetyPolicy(fs));
        var settings = new InMemorySettingsStore(@"C:\Backups");
        var store = new RestorePointStore(fs, settings);
        var registry = new NullRegistry();
        var backup = new BackupService(fs, registry, store, new SafetyPolicy(fs));
        var engine = new OptimizationEngine(
            new ModuleRegistry([module]),
            new SafetyPolicy(fs),
            new NoBroker(),
            backupService: backup,
            restorePointStore: store);

        var action = new OptimizationAction(
            "a", TempCleanerModule.ModuleId, "f",
            PrivilegedOperationCodes.DeleteFile,
            @"C:\Users\Test\AppData\Local\Temp\old.tmp",
            @"C:\Users\Test\AppData\Local\Temp",
            RequiredPrivilege.None);

        var results = await engine.ApplySelectedAsync([action], CancellationToken.None);
        Assert.Equal(ApplyOutcome.Succeeded, results[0].Outcome);
        Assert.False(fs.FileExists(action.Path!));

        var restore = await engine.RestoreAsync(results[0].RestoreTokenId!, CancellationToken.None);
        Assert.Equal(ApplyOutcome.Succeeded, restore.Outcome);
        Assert.True(fs.FileExists(action.Path!));
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
        fs.AddDirectory(@"C:\Users\Test\AppData\Local\Temp");
        fs.AddDirectory(@"C:\Windows\Temp");
        fs.AddDirectory(@"C:\Backups");
        return fs;
    }

    private sealed class NoBroker : IPrivilegeBroker
    {
        public Task<IElevatedPrivilegeSession> BeginElevatedSessionAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("Elevation is not available in tests.");

        public Task<ApplyResult> ExecuteElevatedAsync(OptimizationAction action, CancellationToken cancellationToken)
            => Task.FromResult(ApplyResult.Failed(action.Id, action.FindingId, "not expected"));
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

    private sealed class NullRegistry : IRegistryStore
    {
        public IReadOnlyList<RegistryValueInfo> GetValues(RegistryHiveKind hive, string subKey) => [];
        public IReadOnlyList<string> GetSubKeyNames(RegistryHiveKind hive, string subKey) => [];
        public string? GetStringValue(RegistryHiveKind hive, string subKey, string valueName) => null;
        public void DeleteValue(RegistryHiveKind hive, string subKey, string valueName) { }
        public void DeleteSubKeyTree(RegistryHiveKind hive, string subKey) { }
        public void SetStringValue(RegistryHiveKind hive, string subKey, string valueName, string value) { }
        public string ExportKey(RegistryHiveKind hive, string subKey, string destinationFile) => destinationFile;
    }
}
