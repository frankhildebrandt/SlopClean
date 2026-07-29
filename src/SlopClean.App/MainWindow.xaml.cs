using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SlopClean.App.Pages;
using SlopClean.App.Services;

namespace SlopClean.App;

public sealed partial class MainWindow : Window
{
    private readonly ThemeService _themeService;
    private readonly INavigationService _navigation;

    public MainWindow(ThemeService themeService, INavigationService navigation)
    {
        _themeService = themeService;
        _navigation = navigation;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        _themeService.Apply(RootGrid);
        _navigation.Attach(NavFrame);
        NavFrame.Navigated += NavFrame_Navigated;
        _navigation.Navigate(typeof(DashboardPage));
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        // Keep selection in sync for top-level pages; leave unchanged for review.
        if (e.SourcePageType == typeof(DashboardPage))
        {
            SelectTag("dashboard");
        }
        else if (e.SourcePageType == typeof(ModulesPage))
        {
            SelectTag("modules");
        }
        else if (e.SourcePageType == typeof(CleanTasksPage))
        {
            SelectTag("clean-tasks");
        }
        else if (e.SourcePageType == typeof(RestorePage))
        {
            SelectTag("restore");
        }
        else if (e.SourcePageType == typeof(SettingsPage))
        {
            SelectTag("settings");
        }
        else if (e.SourcePageType == typeof(AboutPage))
        {
            SelectTag("about");
        }
    }

    private void SelectTag(string tag)
    {
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is string itemTag && itemTag == tag)
            {
                if (!ReferenceEquals(NavView.SelectedItem, item))
                {
                    NavView.SelectedItem = item;
                }

                break;
            }
        }
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
                _navigation.Navigate(typeof(DashboardPage));
                break;
            case "modules":
                _navigation.Navigate(typeof(ModulesPage));
                break;
            case "clean-tasks":
                _navigation.Navigate(typeof(CleanTasksPage));
                break;
            case "restore":
                _navigation.Navigate(typeof(RestorePage));
                break;
            case "settings":
                _navigation.Navigate(typeof(SettingsPage));
                break;
            case "about":
                _navigation.Navigate(typeof(AboutPage));
                break;
            case "module":
                if (item.DataContext is string moduleId)
                {
                    _navigation.Navigate(typeof(ModuleDetailPage), moduleId);
                }
                break;
        }
    }

    public void NavigateToModule(string moduleId)
    {
        _navigation.Navigate(typeof(ModuleDetailPage), moduleId);
    }
}
