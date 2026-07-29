using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SlopClean.App.ViewModels;

namespace SlopClean.App.Pages;

public sealed partial class CleanTasksPage : Page
{
    public CleanTasksViewModel ViewModel { get; }

    public CleanTasksPage()
    {
        ViewModel = App.Services.GetRequiredService<CleanTasksViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Load();
    }
}
