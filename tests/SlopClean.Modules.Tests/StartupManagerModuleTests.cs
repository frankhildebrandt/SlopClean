using SlopClean.Core.Abstractions;
using SlopClean.Core.Backup;
using SlopClean.Core.Engine;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
using SlopClean.Core.Settings;
using SlopClean.Modules;
using SlopClean.Modules.Tests.Fakes;

namespace SlopClean.Modules.Tests;

public class StartupManagerModuleTests
{
    [Fact]
    public async Task Scan_lists_run_key_entries()
    {
        var registry = new FakeRegistry();
        registry.Values[(RegistryHiveKind.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")] =
        [
            new RegistryValueInfo("MyApp", @"C:\Apps\MyApp.exe", "String")
        ];

        var module = new StartupManagerModule(registry, CreateFs());
        var findings = new List<ScanFinding>();
        await foreach (var finding in module.ScanAsync(new Dictionary<string, object?>(), null, CancellationToken.None))
        {
            findings.Add(finding);
        }

        Assert.Contains(findings, f => f.DisplayName == "MyApp" && f.IsActionable);
    }

    [Fact]
    public async Task Disable_and_restore_registry_entry_roundtrips_via_engine_backup()
    {
        var fs = CreateFs();
        var registry = new FakeRegistry(fs);
        var key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        registry.Values[(RegistryHiveKind.CurrentUser, key)] =
        [
            new RegistryValueInfo("MyApp", @"C:\Apps\MyApp.exe", "String")
        ];

        var module = new StartupManagerModule(registry, fs);
        var settings = new InMemorySettingsStore(@"C:\Backups");
        var store = new RestorePointStore(fs, settings);
        var backup = new BackupService(fs, registry, store, new SafetyPolicy(fs));
        var engine = new OptimizationEngine(
            new ModuleRegistry([module]),
            new SafetyPolicy(fs),
            new NoBroker(),
            backupService: backup,
            restorePointStore: store);

        var action = new OptimizationAction(
            "a1",
            StartupManagerModule.ModuleId,
            "f1",
            PrivilegedOperationCodes.DeleteRegistryValue,
            null,
            null,
            RequiredPrivilege.None,
            new Dictionary<string, string>
            {
                ["hive"] = "CurrentUser",
                ["subKey"] = key,
                ["valueName"] = "MyApp",
                ["valueData"] = @"C:\Apps\MyApp.exe",
                ["kind"] = "registry"
            });

        var results = await engine.ApplySelectedAsync([action], CancellationToken.None);
        Assert.Equal(ApplyOutcome.Succeeded, results[0].Outcome);
        Assert.Empty(registry.GetValues(RegistryHiveKind.CurrentUser, key));

        var restore = await engine.RestoreAsync(results[0].RestoreTokenId!, CancellationToken.None);
        Assert.Equal(ApplyOutcome.Succeeded, restore.Outcome);
        Assert.Contains(registry.GetValues(RegistryHiveKind.CurrentUser, key), v => v.Name == "MyApp");
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
        fs.Folders[SpecialFolderKind.Startup] = @"C:\Users\Test\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup";
        fs.Folders[SpecialFolderKind.CommonStartup] = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup";
        fs.AddDirectory(fs.Folders[SpecialFolderKind.Startup]);
        fs.AddDirectory(fs.Folders[SpecialFolderKind.CommonStartup]);
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

    private sealed class FakeRegistry : IRegistryStore
    {
        private readonly IFileSystem? _fileSystem;

        public FakeRegistry(IFileSystem? fileSystem = null)
        {
            _fileSystem = fileSystem;
        }

        public Dictionary<(RegistryHiveKind Hive, string SubKey), List<RegistryValueInfo>> Values { get; } = new();
        public Dictionary<(RegistryHiveKind Hive, string SubKey), List<string>> SubKeys { get; } = new();

        public IReadOnlyList<RegistryValueInfo> GetValues(RegistryHiveKind hive, string subKey)
            => Values.TryGetValue((hive, subKey), out var list) ? list : [];

        public IReadOnlyList<string> GetSubKeyNames(RegistryHiveKind hive, string subKey)
            => SubKeys.TryGetValue((hive, subKey), out var list) ? list : [];

        public string? GetStringValue(RegistryHiveKind hive, string subKey, string valueName)
            => GetValues(hive, subKey).FirstOrDefault(v => v.Name == valueName)?.Data;

        public void DeleteValue(RegistryHiveKind hive, string subKey, string valueName)
        {
            if (Values.TryGetValue((hive, subKey), out var list))
            {
                list.RemoveAll(v => v.Name == valueName);
            }
        }

        public void DeleteSubKeyTree(RegistryHiveKind hive, string subKey) { }

        public void SetStringValue(RegistryHiveKind hive, string subKey, string valueName, string value)
        {
            if (!Values.TryGetValue((hive, subKey), out var list))
            {
                list = [];
                Values[(hive, subKey)] = list;
            }

            list.RemoveAll(v => v.Name == valueName);
            list.Add(new RegistryValueInfo(valueName, value, "String"));
        }

        public string ExportKey(RegistryHiveKind hive, string subKey, string destinationFile)
        {
            if (_fileSystem is not null)
            {
                _fileSystem.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                _fileSystem.WriteAllText(destinationFile, "Windows Registry Editor Version 5.00");
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                File.WriteAllText(destinationFile, "Windows Registry Editor Version 5.00");
            }

            return destinationFile;
        }
    }
}
