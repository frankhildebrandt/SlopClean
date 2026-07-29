using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Modules;

namespace SlopClean.Modules.Tests;

public class RecycleBinModuleTests
{
    [Fact]
    public async Task Scan_returns_no_findings_when_empty()
    {
        var module = new RecycleBinModule(new FakeRecycleBin(0, 0));
        var findings = new List<ScanFinding>();
        await foreach (var finding in module.ScanAsync(new Dictionary<string, object?>(), null, CancellationToken.None))
        {
            findings.Add(finding);
        }

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Apply_empties_bin_and_reports_bytes()
    {
        var bin = new FakeRecycleBin(3, 999);
        var module = new RecycleBinModule(bin);
        var action = OptimizationAction.FromFinding(new ScanFinding(
            "recycle-bin:contents",
            RecycleBinModule.ModuleId,
            "recycle-bin",
            "Recycle Bin contents",
            null,
            999,
            FindingRisk.Low,
            "3 item(s)",
            true,
            RequiredPrivilege.None,
            null,
            new Dictionary<string, string>
            {
                [OptimizationAction.OperationCodeMetadataKey] = PrivilegedOperationCodes.EmptyRecycleBin
            }));

        var result = await module.ApplyAsync(action, CancellationToken.None);
        Assert.Equal(ApplyOutcome.Succeeded, result.Outcome);
        Assert.Equal(999, result.BytesFreed);
        Assert.True(bin.Emptied);
    }

    private sealed class FakeRecycleBin(long count, long size) : IRecycleBinService
    {
        public bool Emptied { get; private set; }
        public RecycleBinInfo Query() => new(count, size);
        public void Empty() => Emptied = true;
    }
}
