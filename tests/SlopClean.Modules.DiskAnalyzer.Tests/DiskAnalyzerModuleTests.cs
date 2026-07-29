using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Modules.DiskAnalyzer;
using SlopClean.Modules.TestSupport.Fakes;

namespace SlopClean.Modules.DiskAnalyzer.Tests;

public class DiskAnalyzerModuleTests
{
    [Fact]
    public async Task Scan_returns_largest_files_only_and_is_not_applicable()
    {
        var fs = CreateFs();
        var root = @"C:\Data";
        fs.AddDirectory(root);
        fs.AddFile(Path.Combine(root, "small.bin"), 1024);
        fs.AddFile(Path.Combine(root, "big.bin"), 100L * 1024 * 1024);
        fs.AddFile(Path.Combine(root, "huge.bin"), 200L * 1024 * 1024);

        var module = new DiskAnalyzerModule(fs);
        Assert.DoesNotContain(module.GetType().GetInterfaces(), i => i.Name == nameof(Core.Modules.IApplicableModule));

        var findings = new List<ScanFinding>();
        await foreach (var finding in module.ScanAsync(
                           new Dictionary<string, object?>
                           {
                               ["RootPath"] = new[] { root },
                               ["TopN"] = 1,
                               ["MinSizeMb"] = 50
                           },
                           null,
                           CancellationToken.None))
        {
            findings.Add(finding);
        }

        var only = Assert.Single(findings);
        Assert.EndsWith("huge.bin", only.Path!, StringComparison.OrdinalIgnoreCase);
        Assert.False(only.IsActionable);
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
        return fs;
    }
}
