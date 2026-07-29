using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
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

        var module = new StartupManagerModule(registry, CreateFs(), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var findings = new List<ScanFinding>();
        await foreach (var finding in module.ScanAsync(new Dictionary<string, object?>(), null, CancellationToken.None))
        {
            findings.Add(finding);
        }

        Assert.Contains(findings, f => f.DisplayName == "MyApp" && f.IsActionable);
    }

    [Fact]
    public async Task Disable_and_restore_registry_entry_roundtrips()
    {
        var registry = new FakeRegistry();
        var key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        registry.Values[(RegistryHiveKind.CurrentUser, key)] =
        [
            new RegistryValueInfo("MyApp", @"C:\Apps\MyApp.exe", "String")
        ];

        var stateDir = Path.Combine(Path.GetTempPath(), "SlopCleanStartupTests", Guid.NewGuid().ToString("N"));
        var module = new StartupManagerModule(registry, CreateFs(), stateDir);
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

        var token = await module.CreateRestoreAsync(action, CancellationToken.None);
        var apply = await module.ApplyAsync(action, CancellationToken.None);
        Assert.Equal(ApplyOutcome.Succeeded, apply.Outcome);
        Assert.Empty(registry.GetValues(RegistryHiveKind.CurrentUser, key));

        var restore = await module.RestoreAsync(token, CancellationToken.None);
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
        return fs;
    }

    private sealed class FakeRegistry : IRegistryStore
    {
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
            File.WriteAllText(destinationFile, "Windows Registry Editor Version 5.00");
            return destinationFile;
        }
    }
}
