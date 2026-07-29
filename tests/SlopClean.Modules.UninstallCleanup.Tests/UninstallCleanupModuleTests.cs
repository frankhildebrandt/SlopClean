using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
using SlopClean.Modules.UninstallCleanup;
using FakeFileSystem = SlopClean.Modules.TestSupport.Fakes.FakeFileSystem;

namespace SlopClean.Modules.UninstallCleanup.Tests;

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

        var module = CreateModule(registry, CreateFs());
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

        var module = CreateModule(registry, CreateFs());
        var findings = await CollectAsync(module);

        Assert.Contains(findings, f => f.TargetId == "orphaned-uninstall" && f.DisplayName == "Gone App" && f.IsActionable);
    }

    [Fact]
    public async Task Scan_finds_appdata_leftovers_with_size_and_actionable()
    {
        var registry = OrphanedUninstall("GoneApp");
        var fs = CreateFs();
        fs.AddFile(@"C:\Users\Test\AppData\Local\GoneApp\cache.dat", 2048);
        fs.AddFile(@"C:\Users\Test\AppData\Roaming\GoneApp\settings.json", 512);
        fs.AddDirectory(@"C:\ProgramData\GoneApp");

        var findings = await CollectAsync(CreateModule(registry, fs));
        var leftovers = findings.Where(f => f.TargetId == "appdata-leftover").ToList();

        Assert.Equal(3, leftovers.Count);
        Assert.All(leftovers, f =>
        {
            Assert.True(f.IsActionable);
            Assert.Equal(PrivilegedOperationCodes.DeleteDirectory, f.Metadata![OptimizationAction.OperationCodeMetadataKey]);
            Assert.False(string.IsNullOrWhiteSpace(f.AllowedRoot));
            Assert.False(string.IsNullOrWhiteSpace(f.Path));
        });

        var local = Assert.Single(leftovers, f => f.Path!.EndsWith(@"\Local\GoneApp", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2048, local.SizeBytes);
        Assert.Equal(RequiredPrivilege.None, local.RequiredPrivilege);

        var programData = Assert.Single(leftovers, f => f.Path!.StartsWith(@"C:\ProgramData\", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(RequiredPrivilege.Elevated, programData.RequiredPrivilege);
    }

    [Fact]
    public async Task Apply_deletes_selected_appdata_leftover_directory()
    {
        var registry = OrphanedUninstall("GoneApp");
        var fs = CreateFs();
        fs.AddFile(@"C:\Users\Test\AppData\Local\GoneApp\cache.dat", 100);

        var module = CreateModule(registry, fs);
        var finding = (await CollectAsync(module)).Single(f => f.TargetId == "appdata-leftover");
        var action = OptimizationAction.FromFinding(finding);

        var result = await module.ApplyAsync(action, CancellationToken.None);

        Assert.Equal(ApplyOutcome.Succeeded, result.Outcome);
        Assert.Equal(100, result.BytesFreed);
        Assert.False(fs.DirectoryExists(@"C:\Users\Test\AppData\Local\GoneApp"));
        Assert.False(fs.FileExists(@"C:\Users\Test\AppData\Local\GoneApp\cache.dat"));
    }

    [Fact]
    public async Task Apply_does_not_delete_appdata_root()
    {
        var fs = CreateFs();
        var module = CreateModule(new FakeRegistry(), fs);
        var action = new OptimizationAction(
            Id: "bad",
            ModuleId: UninstallCleanupModule.ModuleId,
            FindingId: "bad",
            OperationCode: PrivilegedOperationCodes.DeleteDirectory,
            Path: @"C:\Users\Test\AppData\Local",
            AllowedRoot: @"C:\Users\Test\AppData\Local",
            RequiredPrivilege: RequiredPrivilege.None);

        var result = await module.ApplyAsync(action, CancellationToken.None);

        Assert.NotEqual(ApplyOutcome.Succeeded, result.Outcome);
        Assert.True(fs.DirectoryExists(@"C:\Users\Test\AppData\Local"));
    }

    private static FakeRegistry OrphanedUninstall(string name)
    {
        var registry = new FakeRegistry();
        var root = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        registry.SubKeys[(RegistryHiveKind.CurrentUser, root)] = [name];
        var sub = $"{root}\\{name}";
        registry.StringValues[(RegistryHiveKind.CurrentUser, sub, "DisplayName")] = name;
        registry.StringValues[(RegistryHiveKind.CurrentUser, sub, "UninstallString")] = @"C:\Gone\uninstall.exe";
        return registry;
    }

    private static UninstallCleanupModule CreateModule(IRegistryStore registry, FakeFileSystem fs)
        => new(registry, fs, new SafetyPolicy(fs));

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
