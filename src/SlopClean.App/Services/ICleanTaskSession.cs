using System.Collections.ObjectModel;
using SlopClean.App.ViewModels;
using SlopClean.Core.Models;

namespace SlopClean.App.Services;

public interface ICleanTaskSession
{
    ObservableCollection<CleanTaskItemViewModel> Tasks { get; }

    bool IsRunning { get; }

    string StatusText { get; }

    string SummaryText { get; }

    event EventHandler? Changed;

    void QueuePlan(OptimizationPlan plan);

    Task StartAsync();

    void Cancel();

    void ClearCompleted();
}
