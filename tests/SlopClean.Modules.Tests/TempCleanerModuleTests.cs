using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
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
        return fs;
    }
}
