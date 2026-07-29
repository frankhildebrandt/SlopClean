using Microsoft.UI.Xaml;

namespace SlopClean.App.Services;

public sealed class ThemeService
{
    private const string SettingsFileName = "ui-settings.json";

    public ElementTheme CurrentTheme { get; private set; } = ElementTheme.Default;

    public ThemeService()
    {
        CurrentTheme = Load();
    }

    public void Apply(FrameworkElement root)
    {
        root.RequestedTheme = CurrentTheme;
    }

    public void SetTheme(ElementTheme theme, FrameworkElement? root = null)
    {
        CurrentTheme = theme;
        if (root is not null)
        {
            root.RequestedTheme = theme;
        }

        Save();
    }

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlopClean",
            SettingsFileName);

    private ElementTheme Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return ElementTheme.Default;
            }

            var text = File.ReadAllText(SettingsPath).Trim();
            return Enum.TryParse<ElementTheme>(text, out var theme) ? theme : ElementTheme.Default;
        }
        catch
        {
            return ElementTheme.Default;
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, CurrentTheme.ToString());
        }
        catch
        {
            // ignore persistence failures
        }
    }
}
