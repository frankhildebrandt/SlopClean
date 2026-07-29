using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using SlopClean.App.Services;
using SlopClean.Core.Safety;
using SlopClean.Core.Settings;
using Windows.Globalization;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SlopClean.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ThemeService _themeService;
    private readonly IAppSettingsStore _settingsStore;
    private bool _suppressSideEffects;

    public SettingsViewModel(ThemeService themeService, IAppSettingsStore settingsStore)
    {
        _themeService = themeService;
        _settingsStore = settingsStore;
        _suppressSideEffects = true;
        try
        {
            SelectedTheme = _themeService.CurrentTheme.ToString();
            SelectedLanguage = ApplicationLanguages.PrimaryLanguageOverride switch
            {
                "de-DE" => "Deutsch",
                "en-US" => "English",
                _ => "System"
            };
            BackupDirectory = _settingsStore.Current.BackupDirectory;
            if (string.IsNullOrWhiteSpace(BackupDirectory))
            {
                BackupDirectory = AppSettings.DefaultBackupDirectory;
            }
        }
        finally
        {
            _suppressSideEffects = false;
        }
    }

    public IReadOnlyList<string> Themes { get; } = ["Default", "Light", "Dark"];
    public IReadOnlyList<string> Languages { get; } = ["System", "Deutsch", "English"];

    [ObservableProperty]
    public partial string SelectedTheme { get; set; } = "Default";

    [ObservableProperty]
    public partial string SelectedLanguage { get; set; } = "System";

    [ObservableProperty]
    public partial string BackupDirectory { get; set; } = AppSettings.DefaultBackupDirectory;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    public async Task LoadAsync()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync();
            BackupDirectory = string.IsNullOrWhiteSpace(settings.BackupDirectory)
                ? AppSettings.DefaultBackupDirectory
                : settings.BackupDirectory;
        }
        catch (Exception ex)
        {
            BackupDirectory = AppSettings.DefaultBackupDirectory;
            StatusText = $"Failed to load settings: {ex.Message}";
        }
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (_suppressSideEffects || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (Enum.TryParse<ElementTheme>(value, out var theme))
        {
            _themeService.SetTheme(theme);
            StatusText = $"Theme: {value}";
        }
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        if (_suppressSideEffects || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        ApplicationLanguages.PrimaryLanguageOverride = value switch
        {
            "Deutsch" => "de-DE",
            "English" => "en-US",
            _ => string.Empty
        };
        StatusText = "Language will fully apply after restart.";
    }

    [RelayCommand]
    private async Task BrowseBackupFolderAsync()
    {
        try
        {
            var window = App.MainWindow;
            if (window is null)
            {
                StatusText = "Window is not ready.";
                return;
            }

            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");

            var hwnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                BackupDirectory = folder.Path;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Folder picker failed: {ex.Message}. Enter a path manually instead.";
        }
    }

    [RelayCommand]
    private async Task SaveBackupDirectoryAsync()
    {
        try
        {
            var path = (BackupDirectory ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusText = "Backup directory is required.";
                return;
            }

            path = Path.GetFullPath(path);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (string.Equals(path, windows, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, system, StringComparison.OrdinalIgnoreCase)
                || PathCanonicalizer.IsUnderRoot(path, PathCanonicalizer.Canonicalize(system)))
            {
                StatusText = "Backup directory cannot be under Windows or System32.";
                return;
            }

            Directory.CreateDirectory(path);
            var settings = _settingsStore.Current;
            settings.BackupDirectory = path;
            await _settingsStore.SaveAsync(settings);
            BackupDirectory = path;
            StatusText = $"Backup directory saved: {path}";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to save backup directory: {ex.Message}";
        }
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

    [RelayCommand]
    private void OpenBackupFolder()
    {
        var path = string.IsNullOrWhiteSpace(BackupDirectory)
            ? AppSettings.DefaultBackupDirectory
            : BackupDirectory;
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
