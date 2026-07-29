namespace SlopClean.Core.Models;

public sealed record PlannedChange(
    string Id,
    string DisplayName,
    string? Path,
    string? Details,
    long SizeBytes,
    FindingRisk Risk,
    RequiredPrivilege RequiredPrivilege,
    bool IsRestorable,
    string RestorableReason,
    OptimizationAction Action);
