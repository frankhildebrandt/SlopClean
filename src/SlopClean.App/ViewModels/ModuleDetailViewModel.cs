using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using SlopClean.App.Helpers;
using SlopClean.App.Pages;
using SlopClean.App.Services;
using SlopClean.Core.Engine;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;
using SlopClean.Core.Planning;
using SlopClean.Core.Presets;

namespace SlopClean.App.ViewModels;

public partial class ModuleDetailViewModel : ObservableObject
{
    private readonly OptimizationEngine _engine;
    private readonly ModuleRegistry _registry;
    private readonly IOptimizationPlanSession _planSession;
    private readonly INavigationService _navigation;
    private readonly PresetStore _presets = new();
    private readonly ILogger<ModuleDetailViewModel> _logger;
    private CancellationTokenSource? _cts;
    private IModule? _module;

    public ModuleDetailViewModel(
        OptimizationEngine engine,
        ModuleRegistry registry,
        IOptimizationPlanSession planSession,
        INavigationService navigation,
        ILogger<ModuleDetailViewModel> logger)
    {
        _engine = engine;
        _registry = registry;
        _planSession = planSession;
        _navigation = navigation;
        _logger = logger;
    }

    public ObservableCollection<ParameterItemViewModel> Parameters { get; } = [];
    public ObservableCollection<FindingItemViewModel> Findings { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string Description { get; set; } = "";

    [ObservableProperty]
    public partial ImageSource Illustration { get; set; } = ModuleImagery.BrandMark;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial bool CanReview { get; set; }

    public async Task InitializeAsync(string moduleId)
    {
        _module = _registry.GetRequired(moduleId);
        var (name, description) = ModuleLocalization.Resolve(_module);
        Title = name;
        Description = description;
        Illustration = ModuleImagery.Load(_module);
        Parameters.Clear();
        Findings.Clear();

        // Load off the UI thread, then apply on the captured dispatcher to avoid
        // ObservableCollection updates after ConfigureAwait(false) inside PresetStore.
        var saved = await _presets.LoadAsync(moduleId, CancellationToken.None).ConfigureAwait(true);
        foreach (var parameter in _module.Parameters)
        {
            var vm = new ParameterItemViewModel(parameter);
            if (saved is not null && saved.TryGetValue(parameter.Id, out var value))
            {
                // JSON presets deserialize numbers as JsonElement/long — coerce safely.
                vm.Value = CoercePresetValue(parameter, value);
            }

            Parameters.Add(vm);
        }

        CanReview = _module is IApplicableModule;
        StatusText = "Ready";
    }

    private static object? CoercePresetValue(IModuleParameter parameter, object? value)
        => ParameterValueCoercion.CoercePreset(parameter, value);

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

        var values = Parameters.ToDictionary(p => p.Id, p => p.TypedValue);
        await _presets.SaveAsync(_module.Id, values, CancellationToken.None);

        try
        {
            var dispatcher = DispatcherQueue.GetForCurrentThread();
            var progress = new Progress<ScanProgress>(p =>
            {
                dispatcher.TryEnqueue(() =>
                {
                    StatusText = p.CompletedItems > 0
                        ? $"{p.Message} ({p.CompletedItems:N0})"
                        : p.Message;
                });
            });

            await foreach (var finding in _engine.ScanModuleAsync(_module.Id, values, progress, _cts.Token)
                               .ConfigureAwait(true))
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
    private void ReviewSelected()
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

        var plan = OptimizationPlan.FromFindings(_module.Id, Title, selected);
        _planSession.Set(plan);
        _navigation.Navigate(typeof(ReviewPlanPage));
        StatusText = $"Reviewing {plan.Changes.Count} change(s)";
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
