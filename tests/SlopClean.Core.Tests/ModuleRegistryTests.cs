using SlopClean.Core.Engine;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;

namespace SlopClean.Core.Tests;

public class ModuleRegistryTests
{
    [Fact]
    public void Detects_capabilities()
    {
        var registry = new ModuleRegistry([new SampleScanOnly(), new SampleApplicable()]);
        Assert.Single(registry.Scannable, m => m.Id == "scan");
        Assert.Contains(registry.Applicable, m => m.Id == "apply");
        Assert.IsType<SampleApplicable>(registry.GetRequired<IApplicableModule>("apply"));
    }

    private sealed class SampleScanOnly : IScannableModule
    {
        public string Id => "scan";
        public string Name => "Scan";
        public string Description => "";
        public ModuleCategory Category => ModuleCategory.Analysis;
        public IReadOnlyList<IModuleParameter> Parameters => [];
        public async IAsyncEnumerable<ScanFinding> ScanAsync(
            IReadOnlyDictionary<string, object?> parameters,
            IProgress<ScanProgress>? progress,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class SampleApplicable : IApplicableModule
    {
        public string Id => "apply";
        public string Name => "Apply";
        public string Description => "";
        public ModuleCategory Category => ModuleCategory.Cleanup;
        public IReadOnlyList<IModuleParameter> Parameters => [];
        public Task<ApplyResult> ApplyAsync(OptimizationAction action, CancellationToken cancellationToken)
            => Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, 0, "ok"));
    }
}
