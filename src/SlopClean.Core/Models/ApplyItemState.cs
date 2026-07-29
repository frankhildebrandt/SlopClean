namespace SlopClean.Core.Models;

public enum ApplyItemState
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Skipped = 3,
    Failed = 4,
    Cancelled = 5
}
