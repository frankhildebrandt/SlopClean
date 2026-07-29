using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SlopClean.App.Helpers;
using SlopClean.App.Pages;
using SlopClean.App.Services;
using SlopClean.Core.Planning;

namespace SlopClean.App.ViewModels;

public partial class ReviewPlanViewModel : ObservableObject
{
    private readonly IOptimizationPlanSession _planSession;
    private readonly ICleanTaskSession _cleanTasks;
    private readonly INavigationService _navigation;
    private readonly ILogger<ReviewPlanViewModel> _logger;

    public ReviewPlanViewModel(
        IOptimizationPlanSession planSession,
        ICleanTaskSession cleanTasks,
        INavigationService navigation,
        ILogger<ReviewPlanViewModel> logger)
    {
        _planSession = planSession;
        _cleanTasks = cleanTasks;
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
    public partial bool CanStartCleanup { get; set; }

    public void Load()
    {
        Changes.Clear();
        var plan = _planSession.Current;
        if (plan is null || plan.Changes.Count == 0)
        {
            Title = "Review planned changes";
            SummaryText = "No planned changes.";
            StatusText = "Go back and select items to review.";
            CanStartCleanup = false;
            return;
        }

        Title = $"Review — {plan.ModuleName}";
        foreach (var change in plan.Changes)
        {
            Changes.Add(new PlannedChangeItemViewModel(change));
        }

        var size = ByteSizeFormatter.Format(plan.TotalSizeBytes);
        SummaryText = string.IsNullOrEmpty(size)
            ? $"{plan.Changes.Count} change(s), {plan.RestorableCount} with backup."
            : $"{plan.Changes.Count} change(s), {size} total, {plan.RestorableCount} with backup.";
        StatusText = _cleanTasks.IsRunning
            ? "A cleanup is already running. Open Clean Tasks to watch progress."
            : "Review the planned changes, then start cleanup on the Clean Tasks page.";
        CanStartCleanup = !_cleanTasks.IsRunning;
    }

    [RelayCommand]
    private void StartCleanup()
    {
        var plan = _planSession.Current;
        if (plan is null || plan.Changes.Count == 0)
        {
            StatusText = "No planned changes to run.";
            return;
        }

        if (_cleanTasks.IsRunning)
        {
            StatusText = "A cleanup is already running.";
            _navigation.Navigate(typeof(CleanTasksPage));
            return;
        }

        try
        {
            _cleanTasks.QueuePlan(plan);
            _planSession.Clear();
            CanStartCleanup = false;
            StatusText = "Queued on Clean Tasks…";
            _navigation.Navigate(typeof(CleanTasksPage));
            // Fire-and-forget: run off the UI thread inside CleanTaskSession.
            _ = _cleanTasks.StartAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to start cleanup: {ex.Message}";
            CanStartCleanup = true;
            _logger.LogError(ex, "Failed to queue cleanup for {ModuleId}", plan.ModuleId);
        }
    }

    [RelayCommand]
    private void Back() => _navigation.GoBack();
}
