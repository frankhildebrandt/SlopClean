using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SlopClean.App.ViewModels;

namespace SlopClean.App.Pages;

public sealed partial class ReviewPlanPage : Page
{
    public ReviewPlanViewModel ViewModel { get; }

    public ReviewPlanPage()
    {
        ViewModel = App.Services.GetRequiredService<ReviewPlanViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Load();
    }
}
