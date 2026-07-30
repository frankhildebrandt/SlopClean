using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Parameters;
using SlopClean.Modules.DiskAnalyzer;
using SlopClean.Modules.TestSupport.Fakes;

namespace SlopClean.Modules.DiskAnalyzer.Tests;

public class DiskAnalyzerModuleTests
{
    private const long OneMb = 1024L * 1024;

    [Fact]
    public void Default_root_is_user_profile_not_drive_root()
    {
        var fs = CreateFs();
        var module = new DiskAnalyzerModule(fs);
        var root = Assert.IsType<PathListParameter>(
            Assert.Single(module.Parameters, p => p.Id == "RootPath"));
        var defaults = Assert.IsAssignableFrom<IReadOnlyList<string>>(root.DefaultValue);
        Assert.Equal([@"C:\Users\Test"], defaults);
    }

    [Fact]
    public void Default_mode_is_largest_files()
    {
        var module = new DiskAnalyzerModule(CreateFs());
        var mode = Assert.IsType<EnumParameter>(Assert.Single(module.Parameters, p => p.Id == "Mode"));
        Assert.Equal("LargestFiles", mode.DefaultValue);
        Assert.Contains("Duplicates", mode.AllowedValues);
    }

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
        Assert.Equal("large-file", only.TargetId);
    }

    [Fact]
    public async Task Scan_keeps_only_top_n_by_size_and_reports_progress()
    {
        var fs = CreateFs();
        var root = @"C:\Data";
        fs.AddDirectory(root);
        for (var i = 1; i <= 10; i++)
        {
            fs.AddFile(Path.Combine(root, $"f{i}.bin"), i * 60L * 1024 * 1024);
        }

        var module = new DiskAnalyzerModule(fs);
        var progress = new List<ScanProgress>();
        var findings = new List<ScanFinding>();
        await foreach (var finding in module.ScanAsync(
                           new Dictionary<string, object?>
                           {
                               ["RootPath"] = new[] { root },
                               ["TopN"] = 3,
                               ["MinSizeMb"] = 50
                           },
                           new Progress<ScanProgress>(progress.Add),
                           CancellationToken.None))
        {
            findings.Add(finding);
        }

        Assert.Equal(3, findings.Count);
        Assert.Equal(
            ["f10.bin", "f9.bin", "f8.bin"],
            findings.Select(f => f.DisplayName).ToArray());
        Assert.Contains(progress, p => p.CompletedItems > 0);
    }

    [Fact]
    public async Task Duplicates_reports_same_size_and_same_content()
    {
        var fs = CreateFs();
        var root = @"C:\Data";
        fs.AddDirectory(root);
        var content = CreateContent(OneMb, seed: 1);
        fs.AddFile(Path.Combine(root, "a.bin"), content);
        fs.AddFile(Path.Combine(root, "b.bin"), content);
        fs.AddFile(Path.Combine(root, "unique.bin"), CreateContent(OneMb + 1024, seed: 2));

        var findings = await ScanDuplicates(fs, root, topN: 10);

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f =>
        {
            Assert.Equal("duplicate-file", f.TargetId);
            Assert.False(f.IsActionable);
            Assert.NotNull(f.Metadata);
            Assert.True(f.Metadata.ContainsKey("sha1"));
            Assert.True(f.Metadata.ContainsKey("duplicateGroupId"));
        });
        Assert.Equal(
            findings[0].Metadata!["sha1"],
            findings[1].Metadata!["sha1"]);
        Assert.Contains(findings, f => f.DisplayName == "a.bin");
        Assert.Contains(findings, f => f.DisplayName == "b.bin");
    }

    [Fact]
    public async Task Duplicates_ignores_same_size_different_content()
    {
        var fs = CreateFs();
        var root = @"C:\Data";
        fs.AddDirectory(root);
        fs.AddFile(Path.Combine(root, "a.bin"), CreateContent(OneMb, seed: 1));
        fs.AddFile(Path.Combine(root, "b.bin"), CreateContent(OneMb, seed: 2));

        var findings = await ScanDuplicates(fs, root, topN: 10);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Duplicates_ignores_unique_sizes()
    {
        var fs = CreateFs();
        var root = @"C:\Data";
        fs.AddDirectory(root);
        fs.AddFile(Path.Combine(root, "a.bin"), CreateContent(OneMb, seed: 1));
        fs.AddFile(Path.Combine(root, "b.bin"), CreateContent(OneMb + 1, seed: 1));

        var findings = await ScanDuplicates(fs, root, topN: 10);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Duplicates_top_n_limits_group_count()
    {
        var fs = CreateFs();
        var root = @"C:\Data";
        fs.AddDirectory(root);

        var groupA = CreateContent(3 * OneMb, seed: 10);
        var groupB = CreateContent(2 * OneMb, seed: 20);
        var groupC = CreateContent(OneMb, seed: 30);
        fs.AddFile(Path.Combine(root, "a1.bin"), groupA);
        fs.AddFile(Path.Combine(root, "a2.bin"), groupA);
        fs.AddFile(Path.Combine(root, "b1.bin"), groupB);
        fs.AddFile(Path.Combine(root, "b2.bin"), groupB);
        fs.AddFile(Path.Combine(root, "c1.bin"), groupC);
        fs.AddFile(Path.Combine(root, "c2.bin"), groupC);

        var findings = await ScanDuplicates(fs, root, topN: 2);

        var groups = findings
            .Select(f => f.Metadata!["duplicateGroupId"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(2, groups.Length);
        Assert.Equal(4, findings.Count);
        Assert.All(findings, f => Assert.True(f.SizeBytes >= 2 * OneMb));
    }

    private static async Task<List<ScanFinding>> ScanDuplicates(FakeFileSystem fs, string root, int topN)
    {
        var module = new DiskAnalyzerModule(fs);
        var findings = new List<ScanFinding>();
        await foreach (var finding in module.ScanAsync(
                           new Dictionary<string, object?>
                           {
                               ["Mode"] = "Duplicates",
                               ["RootPath"] = new[] { root },
                               ["TopN"] = topN,
                               ["MinSizeMb"] = 1
                           },
                           null,
                           CancellationToken.None))
        {
            findings.Add(finding);
        }

        return findings;
    }

    private static byte[] CreateContent(long size, byte seed)
    {
        var bytes = new byte[size];
        bytes.AsSpan().Fill(seed);
        return bytes;
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
