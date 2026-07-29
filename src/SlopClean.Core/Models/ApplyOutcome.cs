namespace SlopClean.Core.Models;

public enum ApplyOutcome
{
    Succeeded = 0,
    Skipped = 1,
    Failed = 2,
    /// <summary>Appended (not inserted) so numeric IPC values for Skipped/Failed stay stable.</summary>
    SucceededRebootRequired = 3
}
