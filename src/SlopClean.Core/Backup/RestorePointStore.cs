using System.Text.Json;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Settings;

namespace SlopClean.Core.Backup;

public sealed class RestorePointStore : IRestorePointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IFileSystem _fileSystem;
    private readonly IAppSettingsStore _settings;

    public RestorePointStore(IFileSystem fileSystem, IAppSettingsStore settings)
    {
        _fileSystem = fileSystem;
        _settings = settings;
    }

    public string BackupRoot
    {
        get
        {
            var root = _settings.Current.BackupDirectory;
            if (string.IsNullOrWhiteSpace(root))
            {
                root = AppSettings.DefaultBackupDirectory;
            }

            return _fileSystem.GetFullPath(root);
        }
    }

    public Task<RestorePointManifest> CreatePendingAsync(
        RestorePointManifest manifest,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            manifest.Id = Guid.NewGuid().ToString("N");
        }

        manifest.Status = RestorePointStatus.Pending;
        manifest.CreatedUtc = DateTimeOffset.UtcNow;

        _fileSystem.CreateDirectory(GetPointDirectory(manifest.Id));
        _fileSystem.CreateDirectory(GetPayloadDirectory(manifest.Id));
        WriteManifest(manifest);
        return Task.FromResult(manifest);
    }

    public Task SaveAsync(RestorePointManifest manifest, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(manifest);
        WriteManifest(manifest);
        return Task.CompletedTask;
    }

    public Task CommitAsync(string restoreId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = ReadManifest(restoreId)
            ?? throw new InvalidOperationException($"Restore point '{restoreId}' was not found.");
        manifest.Status = RestorePointStatus.Committed;
        WriteManifest(manifest);
        return Task.CompletedTask;
    }

    public Task DiscardAsync(string restoreId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pointDir = GetPointDirectory(restoreId);
        if (_fileSystem.DirectoryExists(pointDir))
        {
            _fileSystem.DeleteDirectory(pointDir, recursive: true);
        }

        return Task.CompletedTask;
    }

    public Task MarkRestoredAsync(string restoreId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = ReadManifest(restoreId)
            ?? throw new InvalidOperationException($"Restore point '{restoreId}' was not found.");
        manifest.Status = RestorePointStatus.Restored;
        WriteManifest(manifest);
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(string restoreId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = ReadManifest(restoreId)
            ?? throw new InvalidOperationException($"Restore point '{restoreId}' was not found.");
        manifest.Status = RestorePointStatus.Failed;
        WriteManifest(manifest);
        return Task.CompletedTask;
    }

    public Task<RestorePointManifest?> GetAsync(string restoreId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadManifest(restoreId));
    }

    public Task<IReadOnlyList<RestorePointManifest>> ListCommittedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = BackupRoot;
        if (!_fileSystem.DirectoryExists(root))
        {
            return Task.FromResult<IReadOnlyList<RestorePointManifest>>([]);
        }

        var list = new List<RestorePointManifest>();
        foreach (var dir in _fileSystem.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(dir);
            var manifest = ReadManifest(id);
            if (manifest is { Status: RestorePointStatus.Committed })
            {
                list.Add(manifest);
            }
        }

        return Task.FromResult<IReadOnlyList<RestorePointManifest>>(
            list.OrderByDescending(m => m.CreatedUtc).ToArray());
    }

    public string GetPointDirectory(string restoreId)
        => Path.Combine(BackupRoot, restoreId);

    public string GetPayloadDirectory(string restoreId)
        => Path.Combine(GetPointDirectory(restoreId), "payload");

    private void WriteManifest(RestorePointManifest manifest)
    {
        var path = ManifestPath(manifest.Id);
        _fileSystem.CreateDirectory(Path.GetDirectoryName(path)!);
        _fileSystem.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private RestorePointManifest? ReadManifest(string restoreId)
    {
        var path = ManifestPath(restoreId);
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<RestorePointManifest>(_fileSystem.ReadAllText(path), JsonOptions);
    }

    private string ManifestPath(string restoreId)
        => Path.Combine(GetPointDirectory(restoreId), "manifest.json");
}
