using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SlopClean.App.ViewModels;

namespace SlopClean.App.Pages;

public sealed partial class AboutPage : Page
{
    public AboutViewModel ViewModel { get; }

    public AboutPage()
    {
        ViewModel = App.Services.GetRequiredService<AboutViewModel>();
        InitializeComponent();
    }
}
