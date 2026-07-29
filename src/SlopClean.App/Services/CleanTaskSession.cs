using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using SlopClean.App.Helpers;
using SlopClean.App.ViewModels;
using SlopClean.Core.Engine;
using SlopClean.Core.Models;

namespace SlopClean.App.Services;

public sealed class CleanTaskSession : ICleanTaskSession
{
    private readonly OptimizationEngine _engine;
    private readonly ILogger<CleanTaskSession> _logger;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private OptimizationPlan? _activePlan;
    private DispatcherQueue? _dispatcher;

    public CleanTaskSession(OptimizationEngine engine, ILogger<CleanTaskSession> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public ObservableCollection<CleanTaskItemViewModel> Tasks { get; } = [];

    public bool IsRunning { get; private set; }

    public string StatusText { get; private set; } = "No cleanup tasks queued.";

    public string SummaryText { get; private set; } = "Queue tasks from a module review, then start cleanup here.";

    public event EventHandler? Changed;

    public void QueuePlan(OptimizationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        lock (_gate)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("A cleanup run is already in progress.");
            }

            Tasks.Clear();
            foreach (var change in plan.Changes)
            {
                Tasks.Add(new CleanTaskItemViewModel(change));
            }

            _activePlan = plan;
            StatusText = $"{Tasks.Count} task(s) ready.";
            var size = ByteSizeFormatter.Format(plan.TotalSizeBytes);
            SummaryText = string.IsNullOrEmpty(size)
                ? $"{plan.ModuleName}: {plan.Changes.Count} task(s)."
                : $"{plan.ModuleName}: {plan.Changes.Count} task(s), {size}.";
        }

        RaiseChanged();
    }

    public Task StartAsync()
    {
        OptimizationPlan plan;
        lock (_gate)
        {
            if (IsRunning)
            {
                return Task.CompletedTask;
            }

            if (_activePlan is null || Tasks.Count == 0)
            {
                StatusText = "No tasks to run.";
                RaiseChanged();
                return Task.CompletedTask;
            }

            plan = _activePlan;
            IsRunning = true;
            StatusText = "Running cleanup…";
            _cts = new CancellationTokenSource();
            _dispatcher = DispatcherQueue.GetForCurrentThread();
        }

        RaiseChanged();

        // Run off the UI thread so registry export / elevation waits cannot freeze the window.
        return Task.Run(() => RunAsync(plan, _cts.Token));
    }

    private async Task RunAsync(OptimizationPlan plan, CancellationToken cancellationToken)
    {
        var progress = new Progress<ApplyProgress>(p => OnProgress(p));

        try
        {
            var results = await _engine.ApplyPlanAsync(plan, progress, cancellationToken).ConfigureAwait(false);
            var succeeded = results.Count(r => r.IsSuccessful);
            var freed = ByteSizeFormatter.Format(results.Sum(r => r.BytesFreed));
            await UpdateUiAsync(() =>
            {
                StatusText = string.IsNullOrEmpty(freed)
                    ? $"Done — {succeeded}/{results.Count} succeeded."
                    : $"Done — {succeeded}/{results.Count} succeeded, freed {freed}.";
                SummaryText = $"{plan.ModuleName}: finished {results.Count} task(s).";
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await UpdateUiAsync(() =>
            {
                foreach (var task in Tasks.Where(t => t.State is ApplyItemState.Pending or ApplyItemState.Running))
                {
                    task.State = ApplyItemState.Cancelled;
                    task.Message = "Cancelled";
                }

                StatusText = "Cleanup cancelled.";
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await UpdateUiAsync(() => StatusText = $"Cleanup failed: {ex.Message}").ConfigureAwait(false);
            _logger.LogError(ex, "Clean task run failed for {ModuleId}", plan.ModuleId);
        }
        finally
        {
            lock (_gate)
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
            }

            await UpdateUiAsync(RaiseChanged).ConfigureAwait(false);
        }
    }

    private void OnProgress(ApplyProgress progress)
    {
        void Apply()
        {
            var item = Tasks.FirstOrDefault(t => t.ActionId == progress.ActionId);
            item?.ApplyProgress(progress);
            StatusText = $"{progress.CompletedCount}/{progress.TotalCount} — {progress.DisplayName}: {progress.State}";
            RaiseChanged();
        }

        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            Apply();
        }
        else
        {
            dispatcher.TryEnqueue(Apply);
        }
    }

    private Task UpdateUiAsync(Action action)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        if (!dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
        {
            action();
            return Task.CompletedTask;
        }

        return tcs.Task;
    }

    public void Cancel() => _cts?.Cancel();

    public void ClearCompleted()
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }

            for (var i = Tasks.Count - 1; i >= 0; i--)
            {
                if (Tasks[i].State is ApplyItemState.Succeeded or ApplyItemState.Skipped
                    or ApplyItemState.Failed or ApplyItemState.Cancelled)
                {
                    Tasks.RemoveAt(i);
                }
            }

            if (Tasks.Count == 0)
            {
                _activePlan = null;
                StatusText = "No cleanup tasks queued.";
                SummaryText = "Queue tasks from a module review, then start cleanup here.";
            }
            else
            {
                StatusText = $"{Tasks.Count} task(s) remaining.";
            }
        }

        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
