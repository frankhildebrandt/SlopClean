namespace SlopClean.Core.Models;

public sealed record ApplyProgress(
    string ActionId,
    string FindingId,
    string DisplayName,
    ApplyItemState State,
    int CompletedCount,
    int TotalCount,
    string? Message = null,
    long BytesFreed = 0,
    string? RestoreTokenId = null);
