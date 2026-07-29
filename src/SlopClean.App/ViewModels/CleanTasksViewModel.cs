using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlopClean.App.Services;

namespace SlopClean.App.ViewModels;

public partial class CleanTasksViewModel : ObservableObject
{
    private readonly ICleanTaskSession _session;

    public CleanTasksViewModel(ICleanTaskSession session)
    {
        _session = session;
        _session.Changed += (_, _) => RefreshFromSession();
        RefreshFromSession();
    }

    public ObservableCollection<CleanTaskItemViewModel> Tasks => _session.Tasks;

    [ObservableProperty]
    public partial string SummaryText { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool CanStart { get; set; }

    [ObservableProperty]
    public partial bool CanClear { get; set; }

    public void Load() => RefreshFromSession();

    [RelayCommand]
    private async Task StartAsync()
    {
        await _session.StartAsync();
        RefreshFromSession();
    }

    [RelayCommand]
    private void Cancel() => _session.Cancel();

    [RelayCommand]
    private void ClearCompleted()
    {
        _session.ClearCompleted();
        RefreshFromSession();
    }

    private void RefreshFromSession()
    {
        SummaryText = _session.SummaryText;
        StatusText = _session.StatusText;
        IsBusy = _session.IsRunning;
        CanStart = !_session.IsRunning && _session.Tasks.Any(t => t.State == Core.Models.ApplyItemState.Pending);
        CanClear = !_session.IsRunning && _session.Tasks.Any(t =>
            t.State is Core.Models.ApplyItemState.Succeeded
                or Core.Models.ApplyItemState.Skipped
                or Core.Models.ApplyItemState.Failed
                or Core.Models.ApplyItemState.Cancelled);
    }
}
