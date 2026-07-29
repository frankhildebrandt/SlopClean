using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SlopClean.App.Services;
using SlopClean.Core.Engine;
using SlopClean.Core.Models;
using SlopClean.Core.Planning;

namespace SlopClean.App.ViewModels;

public partial class ReviewPlanViewModel : ObservableObject
{
    private readonly OptimizationEngine _engine;
    private readonly IOptimizationPlanSession _planSession;
    private readonly INavigationService _navigation;
    private readonly ILogger<ReviewPlanViewModel> _logger;
    private CancellationTokenSource? _cts;

    public ReviewPlanViewModel(
        OptimizationEngine engine,
        IOptimizationPlanSession planSession,
        INavigationService navigation,
        ILogger<ReviewPlanViewModel> logger)
    {
        _engine = engine;
        _planSession = planSession;
        _navigation = navigation;
        _logger = logger;
    }

    public ObservableCollection<PlannedChangeItemViewModel> Changes { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "Review planned changes";

    [ObservableProperty]
    public partial string SummaryText { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool CanApply { get; set; }

    public void Load()
    {
        Changes.Clear();
        var plan = _planSession.Current;
        if (plan is null || plan.Changes.Count == 0)
        {
            Title = "Review planned changes";
            SummaryText = "No planned changes.";
            StatusText = "Go back and select items to review.";
            CanApply = false;
            return;
        }

        Title = $"Review — {plan.ModuleName}";
        foreach (var change in plan.Changes)
        {
            Changes.Add(new PlannedChangeItemViewModel(change));
        }

        SummaryText =
            $"{plan.Changes.Count} change(s), {FormatBytes(plan.TotalSizeBytes)} total, {plan.RestorableCount} with backup.";
        StatusText = "Review the planned changes, then apply.";
        CanApply = true;
    }

    [RelayCommand]
    private async Task ApplyPlanAsync()
    {
        var plan = _planSession.Current;
        if (plan is null || plan.Changes.Count == 0)
        {
            StatusText = "No planned changes to apply.";
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        CanApply = false;
        StatusText = "Applying…";

        try
        {
            var results = await _engine.ApplyPlanAsync(plan, _cts.Token);
            var succeeded = results.Count(r => r.Outcome == ApplyOutcome.Succeeded);
            var freed = results.Sum(r => r.BytesFreed);
            var backedUp = results.Count(r => !string.IsNullOrWhiteSpace(r.RestoreTokenId));
            StatusText = $"Done — {succeeded}/{results.Count} succeeded, freed {FormatBytes(freed)}, {backedUp} backup(s).";
            _planSession.Clear();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Apply cancelled";
            CanApply = true;
        }
        catch (Exception ex)
        {
            StatusText = $"Apply failed: {ex.Message}";
            CanApply = true;
            _logger.LogError(ex, "Apply plan failed for {ModuleId}", plan.ModuleId);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void Back() => _navigation.GoBack();

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var i = 0;
        while (value >= 1024 && i < suffixes.Length - 1)
        {
            value /= 1024;
            i++;
        }

        return $"{value:0.##} {suffixes[i]}";
    }
}
