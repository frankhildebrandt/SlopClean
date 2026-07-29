using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SlopClean.App.Controls;
using SlopClean.App.ViewModels;

namespace SlopClean.App.Pages;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        InitializeComponent();
    }

    private void ModuleCard_OpenRequested(object sender, string moduleId)
    {
        if (App.Services.GetRequiredService<MainWindow>() is { } window)
        {
            window.NavigateToModule(moduleId);
        }
    }
}
