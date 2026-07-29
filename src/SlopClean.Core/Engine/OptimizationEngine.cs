using System.Runtime.CompilerServices;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Backup;
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
    private readonly IBackupService? _backupService;
    private readonly IRestorePointStore? _restorePointStore;

    public OptimizationEngine(
        ModuleRegistry registry,
        SafetyPolicy safetyPolicy,
        IPrivilegeBroker privilegeBroker,
        DriveScanScheduler? scheduler = null,
        IBackupService? backupService = null,
        IRestorePointStore? restorePointStore = null)
    {
        _registry = registry;
        _safetyPolicy = safetyPolicy;
        _privilegeBroker = privilegeBroker;
        _scheduler = scheduler ?? new DriveScanScheduler();
        _backupService = backupService;
        _restorePointStore = restorePointStore;
    }

    public async IAsyncEnumerable<ScanFinding> ScanModuleAsync(
        string moduleId,
        IReadOnlyDictionary<string, object?>? parameters,
        IProgress<ScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
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

    public Task<IReadOnlyList<ApplyResult>> ApplySelectedAsync(
        IEnumerable<OptimizationAction> actions,
        CancellationToken cancellationToken)
        => ApplyActionsAsync(actions, displayNames: null, progress: null, cancellationToken);

    public Task<IReadOnlyList<ApplyResult>> ApplyPlanAsync(
        OptimizationPlan plan,
        CancellationToken cancellationToken)
        => ApplyPlanAsync(plan, progress: null, cancellationToken);

    public Task<IReadOnlyList<ApplyResult>> ApplyPlanAsync(
        OptimizationPlan plan,
        IProgress<ApplyProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var actions = plan.Changes.Select(c => c.Action);
        var names = plan.Changes.ToDictionary(c => c.Action.Id, c => c.DisplayName);
        return ApplyActionsAsync(actions, names, progress, cancellationToken);
    }

    public async Task<ApplyResult> RestoreAsync(string restoreId, CancellationToken cancellationToken)
    {
        if (_backupService is null)
        {
            return ApplyResult.Failed(restoreId, restoreId, "Backup service is not configured.");
        }

        return await _backupService.RestoreAsync(restoreId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ApplyResult>> ApplyActionsAsync(
        IEnumerable<OptimizationAction> actions,
        IReadOnlyDictionary<string, string>? displayNames,
        IProgress<ApplyProgress>? progress,
        CancellationToken cancellationToken)
    {
        var list = actions.ToArray();
        var results = new List<ApplyResult>(list.Length);
        var completed = 0;

        foreach (var action in list)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? displayName = null;
            displayNames?.TryGetValue(action.Id, out displayName);
            displayName ??= action.Path ?? action.FindingId;

            progress?.Report(new ApplyProgress(
                action.Id,
                action.FindingId,
                displayName,
                ApplyItemState.Running,
                completed,
                list.Length,
                action.RequiredPrivilege == RequiredPrivilege.Elevated
                    ? "Working (may prompt for admin)…"
                    : "Working…"));

            ApplyResult result;
            try
            {
                result = await ApplyOneAsync(action, displayName, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                progress?.Report(new ApplyProgress(
                    action.Id,
                    action.FindingId,
                    displayName,
                    ApplyItemState.Cancelled,
                    completed,
                    list.Length,
                    "Cancelled"));
                throw;
            }

            completed++;
            var state = result.Outcome switch
            {
                ApplyOutcome.Succeeded => ApplyItemState.Succeeded,
                ApplyOutcome.Skipped => ApplyItemState.Skipped,
                _ => ApplyItemState.Failed
            };
            progress?.Report(new ApplyProgress(
                action.Id,
                action.FindingId,
                displayName,
                state,
                completed,
                list.Length,
                result.Message,
                result.BytesFreed,
                result.RestoreTokenId));
            results.Add(result);
        }

        return results;
    }

    private async Task<ApplyResult> ApplyOneAsync(
        OptimizationAction action,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var validation = _safetyPolicy.ValidateAction(action);
        if (!validation.IsAllowed)
        {
            return ApplyResult.Skipped(action.Id, action.FindingId, validation.Reason ?? "Blocked by safety policy.");
        }

        RestorePointManifest? restorePoint = null;
        try
        {
            if (_backupService is not null && _backupService.CanCreateBackup(action))
            {
                restorePoint = await _backupService
                    .CreatePendingBackupAsync(action, displayName, cancellationToken)
                    .ConfigureAwait(false);
            }

            var module = _registry.GetRequired(action.ModuleId);
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
                result = ApplyResult.Failed(action.Id, action.FindingId, "Module cannot apply actions.");
            }

            if (restorePoint is not null && _restorePointStore is not null)
            {
                if (result.Outcome == ApplyOutcome.Succeeded)
                {
                    await _restorePointStore.CommitAsync(restorePoint.Id, cancellationToken).ConfigureAwait(false);
                    return result with { RestoreTokenId = restorePoint.Id };
                }

                await _restorePointStore.DiscardAsync(restorePoint.Id, cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            if (restorePoint is not null && _restorePointStore is not null)
            {
                await _restorePointStore.DiscardAsync(restorePoint.Id, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception ex)
        {
            if (restorePoint is not null && _restorePointStore is not null)
            {
                await _restorePointStore.DiscardAsync(restorePoint.Id, CancellationToken.None).ConfigureAwait(false);
            }

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
