using System.Runtime.CompilerServices;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;

namespace SlopClean.Modules;

public sealed class RecycleBinModule : IScannableModule, IApplicableModule
{
    public const string ModuleId = "recycle-bin";
    public const string EmptyOperation = PrivilegedOperationCodes.EmptyRecycleBin;

    private readonly IRecycleBinService _recycleBin;

    public RecycleBinModule(IRecycleBinService recycleBin)
    {
        _recycleBin = recycleBin;
    }

    public string Id => ModuleId;
    public string Name => "Recycle Bin";
    public string Description => "Shows Recycle Bin usage and empties it after confirmation.";
    public ModuleCategory Category => ModuleCategory.Cleanup;
    public IReadOnlyList<IModuleParameter> Parameters => [];

    public async IAsyncEnumerable<ScanFinding> ScanAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IProgress<ScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ScanProgress(ModuleId, "Querying Recycle Bin", 0, 1));
        var info = _recycleBin.Query();
        progress?.Report(new ScanProgress(ModuleId, "Recycle Bin queried", 1, 1, info.SizeBytes));

        if (info.ItemCount <= 0 && info.SizeBytes <= 0)
        {
            yield break;
        }

        yield return new ScanFinding(
            Id: $"{ModuleId}:contents",
            ModuleId: ModuleId,
            TargetId: "recycle-bin",
            DisplayName: "Recycle Bin contents",
            Path: null,
            SizeBytes: info.SizeBytes,
            Risk: FindingRisk.Low,
            Details: $"{info.ItemCount} item(s)",
            IsActionable: true,
            RequiredPrivilege: RequiredPrivilege.None,
            AllowedRoot: null,
            Metadata: new Dictionary<string, string>
            {
                [OptimizationAction.OperationCodeMetadataKey] = EmptyOperation
            });

        await Task.Yield();
    }

    public Task<ApplyResult> ApplyAsync(OptimizationAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var before = _recycleBin.Query();
            _recycleBin.Empty();
            return Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, before.SizeBytes, "Recycle Bin emptied."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ApplyResult.Failed(action.Id, action.FindingId, ex.Message));
        }
    }
}
