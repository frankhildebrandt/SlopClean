namespace SlopClean.Core.Backup;

public interface IRestorePointStore
{
    string BackupRoot { get; }

    Task<RestorePointManifest> CreatePendingAsync(
        RestorePointManifest manifest,
        CancellationToken cancellationToken = default);

    Task SaveAsync(RestorePointManifest manifest, CancellationToken cancellationToken = default);

    Task CommitAsync(string restoreId, CancellationToken cancellationToken = default);

    Task DiscardAsync(string restoreId, CancellationToken cancellationToken = default);

    Task MarkRestoredAsync(string restoreId, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(string restoreId, CancellationToken cancellationToken = default);

    Task<RestorePointManifest?> GetAsync(string restoreId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RestorePointManifest>> ListCommittedAsync(CancellationToken cancellationToken = default);

    string GetPointDirectory(string restoreId);

    string GetPayloadDirectory(string restoreId);
}
