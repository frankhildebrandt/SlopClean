using SlopClean.Core.Models;

namespace SlopClean.Core.Backup;

public sealed class RestorePointManifest
{
    public string Id { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public string ActionId { get; set; } = "";
    public string FindingId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string OperationCode { get; set; } = "";
    public RestorePointKind Kind { get; set; }
    public RestorePointStatus Status { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public string? OriginalPath { get; set; }
    public string? AllowedRoot { get; set; }
    public string? PayloadRelativePath { get; set; }
    public RequiredPrivilege RequiredPrivilege { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
