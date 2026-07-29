using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
using SlopClean.Core.Tests.Fakes;

namespace SlopClean.Core.Tests;

public class SafetyPolicyTests
{
    private readonly FakeFileSystem _fs;
    private readonly SafetyPolicy _policy;

    public SafetyPolicyTests()
    {
        _fs = new FakeFileSystem();
        _fs.Folders[SpecialFolderKind.Windows] = @"C:\Windows";
        _fs.Folders[SpecialFolderKind.System] = @"C:\Windows\System32";
        _fs.Folders[SpecialFolderKind.UserProfile] = @"C:\Users\Test";
        _fs.Folders[SpecialFolderKind.ApplicationData] = @"C:\Users\Test\AppData\Roaming";
        _fs.Folders[SpecialFolderKind.LocalApplicationData] = @"C:\Users\Test\AppData\Local";
        _fs.Folders[SpecialFolderKind.CommonApplicationData] = @"C:\ProgramData";
        _fs.Folders[SpecialFolderKind.UserTemp] = @"C:\Users\Test\AppData\Local\Temp";
        _fs.Folders[SpecialFolderKind.WindowsTemp] = @"C:\Windows\Temp";
        _fs.AddDirectory(@"C:\Users\Test\AppData\Local\Temp");
        _policy = new SafetyPolicy(_fs);
    }

    [Fact]
    public void Denies_protected_roots()
    {
        var action = new OptimizationAction(
            "1", "temp", "f1", PrivilegedOperationCodes.DeleteFile,
            @"C:\Windows", @"C:\Windows", RequiredPrivilege.None);

        var result = _policy.ValidateAction(action);
        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void Denies_files_inside_system32_even_with_matching_allowed_root()
    {
        _fs.AddFile(@"C:\Windows\System32\kernel32.dll");
        var action = new OptimizationAction(
            "1", "temp", "f1", PrivilegedOperationCodes.DeleteFile,
            @"C:\Windows\System32\kernel32.dll",
            @"C:\Windows\System32",
            RequiredPrivilege.None);

        Assert.False(_policy.ValidateAction(action).IsAllowed);
    }

    [Fact]
    public void Allows_windows_temp_despite_being_under_windows()
    {
        _fs.AddFile(@"C:\Windows\Temp\a.tmp");
        var action = new OptimizationAction(
            "1", "temp", "f1", PrivilegedOperationCodes.DeleteFile,
            @"C:\Windows\Temp\a.tmp",
            @"C:\Windows\Temp",
            RequiredPrivilege.None);

        Assert.True(_policy.ValidateAction(action).IsAllowed);
    }

    [Fact]
    public void Denies_path_outside_allowed_root()
    {
        _fs.AddFile(@"C:\Users\Test\AppData\Local\Temp\a.txt");
        var action = new OptimizationAction(
            "1", "temp", "f1", PrivilegedOperationCodes.DeleteFile,
            @"C:\Users\Test\AppData\Local\Temp\a.txt",
            @"C:\Other",
            RequiredPrivilege.None);

        Assert.False(_policy.ValidateAction(action).IsAllowed);
    }

    [Fact]
    public void Allows_temp_file_under_allowed_root()
    {
        _fs.AddFile(@"C:\Users\Test\AppData\Local\Temp\a.txt");
        var action = new OptimizationAction(
            "1", "temp", "f1", PrivilegedOperationCodes.DeleteFile,
            @"C:\Users\Test\AppData\Local\Temp\a.txt",
            @"C:\Users\Test\AppData\Local\Temp",
            RequiredPrivilege.None);

        Assert.True(_policy.ValidateAction(action).IsAllowed);
    }

    [Fact]
    public void Denies_reparse_points()
    {
        _fs.AddFile(@"C:\Users\Test\AppData\Local\Temp\link.txt");
        _fs.ReparsePoints.Add(Path.GetFullPath(@"C:\Users\Test\AppData\Local\Temp\link.txt"));
        var action = new OptimizationAction(
            "1", "temp", "f1", PrivilegedOperationCodes.DeleteFile,
            @"C:\Users\Test\AppData\Local\Temp\link.txt",
            @"C:\Users\Test\AppData\Local\Temp",
            RequiredPrivilege.None);

        Assert.False(_policy.ValidateAction(action).IsAllowed);
    }

    [Fact]
    public void Denies_unknown_operation_code()
    {
        var action = new OptimizationAction(
            "1", "temp", "f1", "format-disk",
            @"C:\Users\Test\AppData\Local\Temp\a.txt",
            @"C:\Users\Test\AppData\Local\Temp",
            RequiredPrivilege.Elevated);

        Assert.False(_policy.ValidateAction(action).IsAllowed);
    }

    [Fact]
    public void Allows_uninstall_registry_cleanup_keys()
    {
        var action = new OptimizationAction(
            "1", "uninstall-cleanup", "f1", PrivilegedOperationCodes.DeleteRegistryKey,
            Path: null,
            AllowedRoot: null,
            RequiredPrivilege: RequiredPrivilege.None,
            Payload: new Dictionary<string, string>
            {
                ["hive"] = "CurrentUser",
                ["subKey"] = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Foo"
            });

        Assert.True(_policy.ValidateAction(action).IsAllowed);
    }

    [Fact]
    public void Denies_registry_outside_allowlist()
    {
        var action = new OptimizationAction(
            "1", "uninstall-cleanup", "f1", PrivilegedOperationCodes.DeleteRegistryKey,
            Path: null,
            AllowedRoot: null,
            RequiredPrivilege: RequiredPrivilege.None,
            Payload: new Dictionary<string, string>
            {
                ["hive"] = "LocalMachine",
                ["subKey"] = @"SYSTEM\CurrentControlSet\Services\Foo"
            });

        Assert.False(_policy.ValidateAction(action).IsAllowed);
    }

    [Fact]
    public void Allows_appdata_leftover_folder_under_local_root_but_not_the_root()
    {
        _fs.AddDirectory(@"C:\Users\Test\AppData\Local\GoneApp");

        var leftover = new OptimizationAction(
            "1", "uninstall-cleanup", "f1", PrivilegedOperationCodes.DeleteDirectory,
            @"C:\Users\Test\AppData\Local\GoneApp",
            @"C:\Users\Test\AppData\Local",
            RequiredPrivilege.None);
        Assert.True(_policy.ValidateAction(leftover).IsAllowed);

        var root = new OptimizationAction(
            "2", "uninstall-cleanup", "f2", PrivilegedOperationCodes.DeleteDirectory,
            @"C:\Users\Test\AppData\Local",
            @"C:\Users\Test\AppData\Local",
            RequiredPrivilege.None);
        Assert.False(_policy.ValidateAction(root).IsAllowed);
    }
}
