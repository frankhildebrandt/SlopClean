using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SlopClean.Core.Backup;
using SlopClean.Core.Engine;
using SlopClean.Core.Models;
using SlopClean.Core.Settings;

namespace SlopClean.App.ViewModels;

public partial class RestoreViewModel : ObservableObject
{
    private readonly OptimizationEngine _engine;
    private readonly IRestorePointStore _store;
    private readonly IAppSettingsStore _settings;
    private readonly ILogger<RestoreViewModel> _logger;
    private CancellationTokenSource? _cts;

    public RestoreViewModel(
        OptimizationEngine engine,
        IRestorePointStore store,
        IAppSettingsStore settings,
        ILogger<RestoreViewModel> logger)
    {
        _engine = engine;
        _store = store;
        _settings = settings;
        _logger = logger;
    }

    public ObservableCollection<RestorePointItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial string BackupPathText { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public async Task LoadAsync()
    {
        await _settings.LoadAsync();
        BackupPathText = _settings.Current.BackupDirectory;
        Items.Clear();
        IsBusy = true;
        StatusText = "Loading restore points…";
        try
        {
            var points = await _store.ListCommittedAsync();
            foreach (var point in points)
            {
                Items.Add(new RestorePointItemViewModel(point));
            }

            StatusText = Items.Count == 0
                ? "No restore points available."
                : $"{Items.Count} restore point(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load restore points: {ex.Message}";
            _logger.LogError(ex, "Failed to list restore points");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private async Task RestoreSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            StatusText = "Select one or more restore points.";
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        StatusText = "Restoring…";
        try
        {
            var succeeded = 0;
            foreach (var item in selected)
            {
                var result = await _engine.RestoreAsync(item.Id, _cts.Token);
                if (result.IsSuccessful)
                {
                    succeeded++;
                }
            }

            StatusText = $"Restored {succeeded}/{selected.Length}.";
            await LoadAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Restore cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Restore failed: {ex.Message}";
            _logger.LogError(ex, "Restore failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
