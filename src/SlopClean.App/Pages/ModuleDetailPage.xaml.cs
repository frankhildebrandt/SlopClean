using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SlopClean.App.ViewModels;

namespace SlopClean.App.Pages;

public sealed partial class ModuleDetailPage : Page
{
    public ModuleDetailViewModel ViewModel { get; }

    public ModuleDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<ModuleDetailViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not string moduleId)
        {
            return;
        }

        try
        {
            await ViewModel.InitializeAsync(moduleId);
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Failed to open module: {ex.Message}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }
}
