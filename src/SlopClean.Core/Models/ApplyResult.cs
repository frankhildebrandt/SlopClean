namespace SlopClean.Core.Models;

public sealed record ApplyResult(
    string ActionId,
    string FindingId,
    ApplyOutcome Outcome,
    long BytesFreed,
    string Message,
    string? RestoreTokenId = null)
{
    public static ApplyResult Succeeded(string actionId, string findingId, long bytesFreed, string message, string? restoreTokenId = null)
        => new(actionId, findingId, ApplyOutcome.Succeeded, bytesFreed, message, restoreTokenId);

    public static ApplyResult Skipped(string actionId, string findingId, string message)
        => new(actionId, findingId, ApplyOutcome.Skipped, 0, message);

    public static ApplyResult Failed(string actionId, string findingId, string message)
        => new(actionId, findingId, ApplyOutcome.Failed, 0, message);
}
