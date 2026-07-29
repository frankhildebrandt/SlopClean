using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using SlopClean.Core.Engine;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Presets;

namespace SlopClean.App.ViewModels;

public partial class ModuleDetailViewModel : ObservableObject
{
    private readonly OptimizationEngine _engine;
    private readonly ModuleRegistry _registry;
    private readonly PresetStore _presets = new();
    private readonly ILogger<ModuleDetailViewModel> _logger;
    private CancellationTokenSource? _cts;
    private IModule? _module;

    public ModuleDetailViewModel(
        OptimizationEngine engine,
        ModuleRegistry registry,
        ILogger<ModuleDetailViewModel> logger)
    {
        _engine = engine;
        _registry = registry;
        _logger = logger;
    }

    public ObservableCollection<ParameterItemViewModel> Parameters { get; } = [];
    public ObservableCollection<FindingItemViewModel> Findings { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string Description { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial bool CanApply { get; set; }

    public async Task InitializeAsync(string moduleId)
    {
        _module = _registry.GetRequired(moduleId);
        Title = _module.Name;
        Description = _module.Description;
        Parameters.Clear();
        Findings.Clear();

        var saved = await _presets.LoadAsync(moduleId, CancellationToken.None);
        foreach (var parameter in _module.Parameters)
        {
            var vm = new ParameterItemViewModel(parameter);
            if (saved is not null && saved.TryGetValue(parameter.Id, out var value) && value is bool or int or string)
            {
                vm.Value = value;
            }

            Parameters.Add(vm);
        }

        CanApply = _module is IApplicableModule;
        StatusText = "Ready";
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (_module is null)
        {
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        Findings.Clear();
        StatusText = "Scanning…";

        var values = Parameters.ToDictionary(p => p.Id, p => p.Value);
        await _presets.SaveAsync(_module.Id, values, CancellationToken.None);

        try
        {
            var dispatcher = DispatcherQueue.GetForCurrentThread();
            var progress = new Progress<ScanProgress>(p =>
            {
                dispatcher.TryEnqueue(() => StatusText = p.Message);
            });

            await foreach (var finding in _engine.ScanModuleAsync(_module.Id, values, progress, _cts.Token))
            {
                Findings.Add(new FindingItemViewModel(finding));
            }

            StatusText = $"Found {Findings.Count} item(s)";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
            _logger.LogError(ex, "Module scan failed for {ModuleId}", _module.Id);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplySelectedAsync()
    {
        if (_module is not IApplicableModule)
        {
            return;
        }

        var selected = Findings.Where(f => f.IsSelected && f.IsActionable).Select(f => f.Finding).ToArray();
        if (selected.Length == 0)
        {
            StatusText = "No actionable items selected";
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        StatusText = "Applying…";

        try
        {
            var actions = selected.Select(f => OptimizationAction.FromFinding(f)).ToArray();
            var results = await _engine.ApplySelectedAsync(actions, _cts.Token);
            var freed = results.Sum(r => r.BytesFreed);
            var succeeded = results.Count(r => r.Outcome == ApplyOutcome.Succeeded);
            StatusText = $"Done — {succeeded}/{results.Count} succeeded, freed {freed} bytes";
            await ScanAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Apply cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Apply failed: {ex.Message}";
            _logger.LogError(ex, "Apply failed for {ModuleId}", _module?.Id);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Findings.Where(f => f.IsActionable))
        {
            item.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var item in Findings)
        {
            item.IsSelected = false;
        }
    }
}
