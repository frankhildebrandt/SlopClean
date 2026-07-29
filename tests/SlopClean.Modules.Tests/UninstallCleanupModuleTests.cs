using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Modules;
using FakeFileSystem = SlopClean.Modules.Tests.Fakes.FakeFileSystem;

namespace SlopClean.Modules.Tests;

public class UninstallCleanupModuleTests
{
    [Fact]
    public async Task Scan_ignores_windows_installer_product_codes()
    {
        var registry = new FakeRegistry();
        var root = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        registry.SubKeys[(RegistryHiveKind.CurrentUser, root)] = ["{12345678-1234-1234-1234-1234567890AB}"];
        registry.StringValues[(RegistryHiveKind.CurrentUser, $"{root}\\{{12345678-1234-1234-1234-1234567890AB}}", "DisplayName")] = "Msi App";
        registry.StringValues[(RegistryHiveKind.CurrentUser, $"{root}\\{{12345678-1234-1234-1234-1234567890AB}}", "WindowsInstaller")] = "1";
        registry.StringValues[(RegistryHiveKind.CurrentUser, $"{root}\\{{12345678-1234-1234-1234-1234567890AB}}", "UninstallString")] = @"C:\Missing\uninstall.exe";

        var module = new UninstallCleanupModule(registry, CreateFs(), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var findings = await CollectAsync(module);
        Assert.DoesNotContain(findings, f => f.TargetId == "orphaned-uninstall");
    }

    [Fact]
    public async Task Scan_finds_orphaned_uninstall_when_paths_missing()
    {
        var registry = new FakeRegistry();
        var root = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        registry.SubKeys[(RegistryHiveKind.CurrentUser, root)] = ["GoneApp"];
        var sub = $"{root}\\GoneApp";
        registry.StringValues[(RegistryHiveKind.CurrentUser, sub, "DisplayName")] = "Gone App";
        registry.StringValues[(RegistryHiveKind.CurrentUser, sub, "UninstallString")] = @"C:\Gone\uninstall.exe";
        registry.StringValues[(RegistryHiveKind.CurrentUser, sub, "InstallLocation")] = @"C:\Gone";

        var module = new UninstallCleanupModule(registry, CreateFs(), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var findings = await CollectAsync(module);

        Assert.Contains(findings, f => f.TargetId == "orphaned-uninstall" && f.DisplayName == "Gone App" && f.IsActionable);
    }

    [Fact]
    public async Task AppData_hints_are_never_actionable()
    {
        var registry = new FakeRegistry();
        var root = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        registry.SubKeys[(RegistryHiveKind.CurrentUser, root)] = ["GoneApp"];
        var sub = $"{root}\\GoneApp";
        registry.StringValues[(RegistryHiveKind.CurrentUser, sub, "DisplayName")] = "GoneApp";
        registry.StringValues[(RegistryHiveKind.CurrentUser, sub, "UninstallString")] = @"C:\Gone\uninstall.exe";

        var fs = CreateFs();
        fs.AddDirectory(@"C:\Users\Test\AppData\Local\GoneApp");

        var module = new UninstallCleanupModule(registry, fs, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var findings = await CollectAsync(module);

        Assert.Contains(findings, f => f.TargetId == "appdata-hint" && !f.IsActionable);
    }

    private static async Task<List<ScanFinding>> CollectAsync(UninstallCleanupModule module)
    {
        var list = new List<ScanFinding>();
        await foreach (var finding in module.ScanAsync(new Dictionary<string, object?>(), null, CancellationToken.None))
        {
            list.Add(finding);
        }

        return list;
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
        fs.AddDirectory(@"C:\Users\Test\AppData\Local");
        fs.AddDirectory(@"C:\Users\Test\AppData\Roaming");
        fs.AddDirectory(@"C:\ProgramData");
        return fs;
    }

    private sealed class FakeRegistry : IRegistryStore
    {
        public Dictionary<(RegistryHiveKind Hive, string SubKey), List<string>> SubKeys { get; } = new();
        public Dictionary<(RegistryHiveKind Hive, string SubKey, string Name), string> StringValues { get; } = new();

        public IReadOnlyList<RegistryValueInfo> GetValues(RegistryHiveKind hive, string subKey)
            => StringValues
                .Where(kv => kv.Key.Hive == hive && kv.Key.SubKey == subKey)
                .Select(kv => new RegistryValueInfo(kv.Key.Name, kv.Value, "String"))
                .ToArray();

        public IReadOnlyList<string> GetSubKeyNames(RegistryHiveKind hive, string subKey)
            => SubKeys.TryGetValue((hive, subKey), out var list) ? list : [];

        public string? GetStringValue(RegistryHiveKind hive, string subKey, string valueName)
            => StringValues.TryGetValue((hive, subKey, valueName), out var value) ? value : null;

        public void DeleteValue(RegistryHiveKind hive, string subKey, string valueName)
            => StringValues.Remove((hive, subKey, valueName));

        public void DeleteSubKeyTree(RegistryHiveKind hive, string subKey)
        {
            foreach (var key in StringValues.Keys.Where(k => k.Hive == hive && k.SubKey.StartsWith(subKey, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                StringValues.Remove(key);
            }
        }

        public void SetStringValue(RegistryHiveKind hive, string subKey, string valueName, string value)
            => StringValues[(hive, subKey, valueName)] = value;

        public string ExportKey(RegistryHiveKind hive, string subKey, string destinationFile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.WriteAllText(destinationFile, "Windows Registry Editor Version 5.00");
            return destinationFile;
        }
    }
}
