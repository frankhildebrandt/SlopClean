using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace SlopClean.Elevated;

public sealed partial class MainWindow : Window
{
    private readonly string _pipeName;
    private readonly string _sessionNonce;

    public int ExitCode { get; private set; } = 1;

    public MainWindow(string pipeName, string sessionNonce)
    {
        _pipeName = pipeName;
        _sessionNonce = sessionNonce;
        InitializeComponent();
        Title = ElevatedUiStrings.WindowTitle;
        HeadingText.Text = ElevatedUiStrings.Heading;
        StatusLabelText.Text = ElevatedUiStrings.StatusLabel;
        JobLabelText.Text = ElevatedUiStrings.JobLabel;
        StatusText.Text = ElevatedUiStrings.StatusWaiting;
        JobText.Text = ElevatedUiStrings.JobNone;
        ConfigureWindowChrome();
    }

    public async Task RunHostAsync()
    {
        var host = new ElevatedHost(
            _pipeName,
            _sessionNonce,
            DispatcherQueue,
            OnJobChanged);

        ExitCode = await host.RunAsync().ConfigureAwait(true);
        BusyBar.IsIndeterminate = false;
        BusyBar.Visibility = Visibility.Collapsed;
        Close();
    }

    private void OnJobChanged(string status, string job)
    {
        StatusText.Text = status;
        JobText.Text = string.IsNullOrWhiteSpace(job) ? ElevatedUiStrings.JobNone : job;
        BusyBar.IsIndeterminate = !string.Equals(status, ElevatedUiStrings.StatusFinished, StringComparison.Ordinal)
            && !string.Equals(status, ElevatedUiStrings.StatusFailed, StringComparison.Ordinal);
    }

    private void ConfigureWindowChrome()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(520, 240));
        appWindow.Title = ElevatedUiStrings.WindowTitle;

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
        }
    }
}
