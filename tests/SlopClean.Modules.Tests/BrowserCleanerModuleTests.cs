using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
using SlopClean.Modules;
using SlopClean.Modules.Tests.Fakes;

namespace SlopClean.Modules.Tests;

public class BrowserCleanerModuleTests
{
    [Fact]
    public async Task Scan_reports_running_browser_as_non_actionable()
    {
        var fs = CreateFs();
        var processes = new FakeProcesses(["chrome"]);
        var module = new BrowserCleanerModule(fs, processes, new SafetyPolicy(fs));

        var findings = await CollectAsync(module, new Dictionary<string, object?>
        {
            ["IncludeCache"] = true,
            ["IncludeCookies"] = false,
            ["IncludeHistory"] = false
        });

        var running = Assert.Single(findings, f => f.TargetId == "chrome" && !f.IsActionable);
        Assert.Contains("running", running.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scan_emits_cache_finding_when_browser_closed()
    {
        var fs = CreateFs();
        var cache = @"C:\Users\Test\AppData\Local\Google\Chrome\User Data\Default\Cache";
        fs.AddDirectory(cache);
        fs.AddFile(Path.Combine(cache, "data_1"), 2048);

        var module = new BrowserCleanerModule(fs, new FakeProcesses([]), new SafetyPolicy(fs));
        var findings = await CollectAsync(module, new Dictionary<string, object?>
        {
            ["IncludeCache"] = true,
            ["IncludeCookies"] = false,
            ["IncludeHistory"] = false
        });

        Assert.Contains(findings, f => f.TargetId == "chrome-cache" && f.IsActionable && f.SizeBytes >= 2048);
    }

    private static async Task<List<ScanFinding>> CollectAsync(
        BrowserCleanerModule module,
        IReadOnlyDictionary<string, object?> parameters)
    {
        var list = new List<ScanFinding>();
        await foreach (var finding in module.ScanAsync(parameters, null, CancellationToken.None))
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
        return fs;
    }

    private sealed class FakeProcesses(IEnumerable<string> running) : IProcessInspector
    {
        private readonly HashSet<string> _running = new(running, StringComparer.OrdinalIgnoreCase);

        public bool IsProcessRunning(string processName) => _running.Contains(processName);
        public IReadOnlyList<string> GetRunningProcessNames() => _running.ToArray();
    }
}
