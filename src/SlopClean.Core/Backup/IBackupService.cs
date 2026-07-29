using SlopClean.Core.Models;

namespace SlopClean.Core.Backup;

public interface IBackupService
{
    bool CanCreateBackup(OptimizationAction action);

    Task<RestorePointManifest?> CreatePendingBackupAsync(
        OptimizationAction action,
        string? displayName = null,
        CancellationToken cancellationToken = default);

    Task<ApplyResult> RestoreAsync(
        string restoreId,
        CancellationToken cancellationToken = default);
}
