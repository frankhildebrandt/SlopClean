namespace SlopClean.Core.Models;

public sealed record OptimizationAction(
    string Id,
    string ModuleId,
    string FindingId,
    string OperationCode,
    string? Path,
    string? AllowedRoot,
    RequiredPrivilege RequiredPrivilege,
    IReadOnlyDictionary<string, string>? Payload = null)
{
    public const string OperationCodeMetadataKey = "operationCode";

    public static OptimizationAction FromFinding(
        ScanFinding finding,
        string? operationCode = null,
        IReadOnlyDictionary<string, string>? payload = null)
    {
        if (!finding.IsActionable)
        {
            throw new InvalidOperationException($"Finding '{finding.Id}' is not actionable.");
        }

        var code = operationCode
            ?? (finding.Metadata is not null
                && finding.Metadata.TryGetValue(OperationCodeMetadataKey, out var fromMeta)
                    ? fromMeta
                    : null)
            ?? throw new InvalidOperationException($"Finding '{finding.Id}' has no operation code.");

        return new OptimizationAction(
            Id: Guid.NewGuid().ToString("N"),
            ModuleId: finding.ModuleId,
            FindingId: finding.Id,
            OperationCode: code,
            Path: finding.Path,
            AllowedRoot: finding.AllowedRoot,
            RequiredPrivilege: finding.RequiredPrivilege,
            Payload: payload ?? finding.Metadata);
    }
}
