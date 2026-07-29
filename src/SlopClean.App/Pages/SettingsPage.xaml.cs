using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SlopClean.App.ViewModels;

namespace SlopClean.App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
        BackupDirectoryBox.Text = ViewModel.BackupDirectory;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string theme })
        {
            ViewModel.SelectedTheme = theme;
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string language })
        {
            ViewModel.SelectedLanguage = language;
        }
    }

    private void BackupDirectoryBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            ViewModel.BackupDirectory = box.Text ?? string.Empty;
        }
    }
}
