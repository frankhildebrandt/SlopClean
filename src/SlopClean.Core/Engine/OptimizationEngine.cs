using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;
using SlopClean.Core.Safety;

namespace SlopClean.Core.Engine;

public sealed class OptimizationEngine
{
    private readonly ModuleRegistry _registry;
    private readonly SafetyPolicy _safetyPolicy;
    private readonly IPrivilegeBroker _privilegeBroker;
    private readonly DriveScanScheduler _scheduler;

    public OptimizationEngine(
        ModuleRegistry registry,
        SafetyPolicy safetyPolicy,
        IPrivilegeBroker privilegeBroker,
        DriveScanScheduler? scheduler = null)
    {
        _registry = registry;
        _safetyPolicy = safetyPolicy;
        _privilegeBroker = privilegeBroker;
        _scheduler = scheduler ?? new DriveScanScheduler();
    }

    public async IAsyncEnumerable<ScanFinding> ScanModuleAsync(
        string moduleId,
        IReadOnlyDictionary<string, object?>? parameters,
        IProgress<ScanProgress>? progress,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var module = _registry.GetRequired<IScannableModule>(moduleId);
        var resolved = ParameterValidator.WithDefaults(module.Parameters, parameters);

        string? driveHint = null;
        if (resolved.TryGetValue("RootPath", out var root))
        {
            if (root is string rootPath)
            {
                driveHint = PathCanonicalizer.GetDriveRoot(rootPath);
            }
            else if (root is IEnumerable<string> roots)
            {
                driveHint = PathCanonicalizer.GetDriveRoot(roots.First());
            }
        }

        await using var lease = await WaitLeaseAsync(driveHint, cancellationToken).ConfigureAwait(false);

        await foreach (var finding in module.ScanAsync(resolved, progress, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return finding;
        }
    }

    public async Task<IReadOnlyList<ScanFinding>> ScanAllAsync(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? moduleParameters,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var results = new List<ScanFinding>();
        foreach (var module in _registry.Scannable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyDictionary<string, object?>? parameters = null;
            moduleParameters?.TryGetValue(module.Id, out parameters);
            await foreach (var finding in ScanModuleAsync(module.Id, parameters, progress, cancellationToken)
                               .ConfigureAwait(false))
            {
                results.Add(finding);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<ApplyResult>> ApplySelectedAsync(
        IEnumerable<OptimizationAction> actions,
        CancellationToken cancellationToken)
    {
        var results = new List<ApplyResult>();
        foreach (var action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ApplyOneAsync(action, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<ApplyResult> ApplyOneAsync(OptimizationAction action, CancellationToken cancellationToken)
    {
        var validation = _safetyPolicy.ValidateAction(action);
        if (!validation.IsAllowed)
        {
            return ApplyResult.Skipped(action.Id, action.FindingId, validation.Reason ?? "Blocked by safety policy.");
        }

        try
        {
            string? restoreId = null;
            var module = _registry.GetRequired(action.ModuleId);
            if (module is IReversibleModule reversible)
            {
                var token = await reversible.CreateRestoreAsync(action, cancellationToken).ConfigureAwait(false);
                restoreId = token.Id;
            }

            ApplyResult result;
            if (action.RequiredPrivilege == RequiredPrivilege.Elevated)
            {
                result = await _privilegeBroker.ExecuteElevatedAsync(action, cancellationToken).ConfigureAwait(false);
            }
            else if (module is IApplicableModule applicable)
            {
                result = await applicable.ApplyAsync(action, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                return ApplyResult.Failed(action.Id, action.FindingId, "Module cannot apply actions.");
            }

            return result with { RestoreTokenId = restoreId ?? result.RestoreTokenId };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ApplyResult.Failed(action.Id, action.FindingId, ex.Message);
        }
    }

    private async Task<IAsyncDisposable> WaitLeaseAsync(string? driveHint, CancellationToken cancellationToken)
    {
        var releaser = await _scheduler.AcquireAsync(driveHint, cancellationToken).ConfigureAwait(false);
        return new AsyncReleaser(releaser);
    }

    private sealed class AsyncReleaser(IDisposable inner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
