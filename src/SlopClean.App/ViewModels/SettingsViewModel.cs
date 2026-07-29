using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using SlopClean.App.Services;
using Windows.Globalization;

namespace SlopClean.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ThemeService _themeService;

    public SettingsViewModel(ThemeService themeService)
    {
        _themeService = themeService;
        SelectedTheme = _themeService.CurrentTheme.ToString();
        SelectedLanguage = ApplicationLanguages.PrimaryLanguageOverride switch
        {
            "de-DE" => "Deutsch",
            "en-US" => "English",
            _ => "System"
        };
    }

    public IReadOnlyList<string> Themes { get; } = ["Default", "Light", "Dark"];
    public IReadOnlyList<string> Languages { get; } = ["System", "Deutsch", "English"];

    [ObservableProperty]
    public partial string SelectedTheme { get; set; }

    [ObservableProperty]
    public partial string SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    partial void OnSelectedThemeChanged(string value)
    {
        if (Enum.TryParse<ElementTheme>(value, out var theme))
        {
            _themeService.SetTheme(theme);
            StatusText = $"Theme: {value}";
        }
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        ApplicationLanguages.PrimaryLanguageOverride = value switch
        {
            "Deutsch" => "de-DE",
            "English" => "en-US",
            _ => string.Empty
        };
        StatusText = "Language will fully apply after restart.";
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlopClean",
            "logs");
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
