using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SlopClean.App.Helpers;
using SlopClean.Core.Engine;
using SlopClean.Core.Models;

namespace SlopClean.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly OptimizationEngine _engine;
    private readonly ModuleRegistry _registry;
    private readonly ILogger<DashboardViewModel> _logger;
    private CancellationTokenSource? _cts;

    public DashboardViewModel(OptimizationEngine engine, ModuleRegistry registry, ILogger<DashboardViewModel> logger)
    {
        _engine = engine;
        _registry = registry;
        _logger = logger;
        Modules = new ObservableCollection<ModuleSummaryViewModel>(
            _registry.All.Select(m =>
            {
                var (name, description) = ModuleLocalization.Resolve(m);
                return new ModuleSummaryViewModel(m.Id, name, description, m.Category.ToString());
            }));
    }

    public ObservableCollection<ModuleSummaryViewModel> Modules { get; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty]
    public partial string TotalSizeText { get; set; } = "—";

    [ObservableProperty]
    public partial int FindingCount { get; set; }

    [ObservableProperty]
    public partial long TotalBytes { get; set; }

    [RelayCommand]
    private async Task ScanAllAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        FindingCount = 0;
        TotalBytes = 0;
        StatusText = "Scanning…";

        try
        {
            var progress = new Progress<ScanProgress>(p => StatusText = $"{p.ModuleId}: {p.Message}");
            var findings = await _engine.ScanAllAsync(null, progress, _cts.Token);
            FindingCount = findings.Count;
            TotalBytes = findings.Sum(f => f.SizeBytes);
            TotalSizeText = ByteSizeFormatter.FormatOrDash(TotalBytes);
            var size = ByteSizeFormatter.Format(TotalBytes);
            StatusText = string.IsNullOrEmpty(size)
                ? $"Scan complete — {FindingCount} finding(s)"
                : $"Scan complete — {FindingCount} finding(s), {size}";
            _logger.LogInformation("Dashboard scan complete with {Count} findings", FindingCount);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
            _logger.LogError(ex, "Dashboard scan failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}

public sealed class ModuleSummaryViewModel
{
    public ModuleSummaryViewModel(string id, string name, string description, string category)
    {
        Id = id;
        Name = name;
        Description = description;
        Category = category;
    }

    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Category { get; set; }
}
