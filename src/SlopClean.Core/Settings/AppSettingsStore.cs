using System.Text.Json;

namespace SlopClean.Core.Settings;

public sealed class AppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;
    private readonly object _gate = new();
    private AppSettings _current = new();

    public AppSettingsStore(string? settingsFilePath = null)
    {
        _settingsPath = settingsFilePath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SlopClean",
                "app-settings.json");
    }

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return Clone(_current);
            }
        }
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            var defaults = new AppSettings();
            lock (_gate)
            {
                _current = defaults;
            }

            return Clone(defaults);
        }

        await using var stream = File.OpenRead(_settingsPath);
        var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? new AppSettings();

        if (string.IsNullOrWhiteSpace(loaded.BackupDirectory))
        {
            loaded.BackupDirectory = AppSettings.DefaultBackupDirectory;
        }

        lock (_gate)
        {
            _current = loaded;
        }

        return Clone(loaded);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.BackupDirectory))
        {
            throw new ArgumentException("Backup directory is required.", nameof(settings));
        }

        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = _settingsPath + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Copy(temp, _settingsPath, overwrite: true);
        File.Delete(temp);

        lock (_gate)
        {
            _current = Clone(settings);
        }
    }

    private static AppSettings Clone(AppSettings settings)
        => new() { BackupDirectory = settings.BackupDirectory };
}
