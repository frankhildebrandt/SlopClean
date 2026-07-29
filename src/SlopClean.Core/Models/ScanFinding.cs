namespace SlopClean.Core.Models;

public sealed record ScanFinding(
    string Id,
    string ModuleId,
    string TargetId,
    string DisplayName,
    string? Path,
    long SizeBytes,
    FindingRisk Risk,
    string Details,
    bool IsActionable,
    RequiredPrivilege RequiredPrivilege,
    string? AllowedRoot,
    IReadOnlyDictionary<string, string>? Metadata = null);
