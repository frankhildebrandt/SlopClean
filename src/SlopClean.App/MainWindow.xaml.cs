using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SlopClean.App.Pages;
using SlopClean.App.Services;

namespace SlopClean.App;

public sealed partial class MainWindow : Window
{
    private readonly ThemeService _themeService;

    public MainWindow(ThemeService themeService)
    {
        _themeService = themeService;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        _themeService.Apply(RootGrid);
        NavFrame.Navigate(typeof(DashboardPage));
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        switch (tag)
        {
            case "dashboard":
                NavFrame.Navigate(typeof(DashboardPage));
                break;
            case "modules":
                NavFrame.Navigate(typeof(ModulesPage));
                break;
            case "settings":
                NavFrame.Navigate(typeof(SettingsPage));
                break;
            case "about":
                NavFrame.Navigate(typeof(AboutPage));
                break;
            case "module":
                if (item.DataContext is string moduleId)
                {
                    NavFrame.Navigate(typeof(ModuleDetailPage), moduleId);
                }
                break;
        }
    }

    public void NavigateToModule(string moduleId)
    {
        NavFrame.Navigate(typeof(ModuleDetailPage), moduleId);
    }
}
