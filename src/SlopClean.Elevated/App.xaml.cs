using Microsoft.UI.Xaml;

namespace SlopClean.Elevated;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow(LaunchArgs.PipeName, LaunchArgs.SessionNonce);
        _window = window;
        window.Activate();
        await window.RunHostAsync().ConfigureAwait(true);
        Environment.Exit(window.ExitCode);
    }
}
