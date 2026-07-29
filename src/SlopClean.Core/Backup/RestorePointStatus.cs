namespace SlopClean.Core.Backup;

public enum RestorePointStatus
{
    Pending = 0,
    Committed = 1,
    Restored = 2,
    Failed = 3,
    Discarded = 4
}
