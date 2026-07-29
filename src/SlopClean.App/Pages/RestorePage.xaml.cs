using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SlopClean.App.ViewModels;

namespace SlopClean.App.Pages;

public sealed partial class RestorePage : Page
{
    public RestoreViewModel ViewModel { get; }

    public RestorePage()
    {
        ViewModel = App.Services.GetRequiredService<RestoreViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }
}
