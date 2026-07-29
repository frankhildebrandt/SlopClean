using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Serilog;
using Serilog.Extensions.Logging;
using SlopClean.App.Services;
using SlopClean.App.ViewModels;
using SlopClean.Core.Logging;
using SlopClean.Core.Safety;

namespace SlopClean.App;

public partial class App : Application
{
    private Window? _window;

    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlopClean",
            "logs");
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new RedactingFileSink(logDirectory))
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: true);
            builder.AddProvider(new RedactingLoggerProvider());
        });

        services.AddSlopCleanWindowsPlatform();
        services.AddSlopCleanModules();
        services.AddSingleton<SafetyPolicy>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ModuleDetailViewModel>();
        services.AddTransient<SettingsViewModel>();
        return services.BuildServiceProvider();
    }
}

internal sealed class RedactingLoggerProvider : ILoggerProvider
{
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
        => new RedactingLogger();

    public void Dispose()
    {
    }

    private sealed class RedactingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Serilog already writes; this provider exists as a hook for future UI log sinks.
            _ = LogRedactor.Redact(formatter(state, exception));
        }
    }
}
