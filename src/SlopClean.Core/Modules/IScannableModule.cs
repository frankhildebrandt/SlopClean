using SlopClean.Core.Models;

namespace SlopClean.Core.Modules;

public interface IScannableModule : IModule
{
    IAsyncEnumerable<ScanFinding> ScanAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}
