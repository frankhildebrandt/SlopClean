using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SlopClean.App.Helpers;
using SlopClean.App.ViewModels;
using SlopClean.Core.Engine;

namespace SlopClean.App.Pages;

public sealed partial class ModulesPage : Page
{
    public ObservableCollection<ModuleSummaryViewModel> Modules { get; }

    public ModulesPage()
    {
        var registry = App.Services.GetRequiredService<ModuleRegistry>();
        Modules = new ObservableCollection<ModuleSummaryViewModel>(
            registry.All.Select(m =>
            {
                var (name, description) = ModuleLocalization.Resolve(m);
                return new ModuleSummaryViewModel(m.Id, name, description, m.Category.ToString());
            }));
        InitializeComponent();
    }

    private void ModuleCard_OpenRequested(object sender, string moduleId)
    {
        App.Services.GetRequiredService<MainWindow>().NavigateToModule(moduleId);
    }
}
