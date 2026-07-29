using SlopClean.Core.Abstractions;

namespace SlopClean.Core.Models;

public sealed record OptimizationPlan(
    string Id,
    string ModuleId,
    string ModuleName,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<PlannedChange> Changes)
{
    public long TotalSizeBytes => Changes.Sum(c => c.SizeBytes);

    public int RestorableCount => Changes.Count(c => c.IsRestorable);

    public static bool IsRestorableOperation(string operationCode)
        => operationCode is PrivilegedOperationCodes.DeleteFile
            or PrivilegedOperationCodes.DeleteDirectory
            or PrivilegedOperationCodes.DeleteRegistryKey
            or PrivilegedOperationCodes.DeleteRegistryValue
            or PrivilegedOperationCodes.DisableStartupShortcut;

    public static string RestorableReason(string operationCode)
        => operationCode switch
        {
            PrivilegedOperationCodes.EmptyRecycleBin => "Emptying the Recycle Bin cannot be restored.",
            _ when IsRestorableOperation(operationCode) => "A backup will be created before applying.",
            _ => "This action cannot be restored."
        };

    public static OptimizationPlan FromFindings(
        string moduleId,
        string moduleName,
        IEnumerable<ScanFinding> findings)
    {
        var changes = findings
            .Where(f => f.IsActionable)
            .Select(f =>
            {
                var action = OptimizationAction.FromFinding(f);
                var restorable = IsRestorableOperation(action.OperationCode);
                return new PlannedChange(
                    Id: f.Id,
                    DisplayName: f.DisplayName,
                    Path: f.Path,
                    Details: f.Details,
                    SizeBytes: f.SizeBytes,
                    Risk: f.Risk,
                    RequiredPrivilege: f.RequiredPrivilege,
                    IsRestorable: restorable,
                    RestorableReason: RestorableReason(action.OperationCode),
                    Action: action);
            })
            .ToArray();

        return new OptimizationPlan(
            Id: Guid.NewGuid().ToString("N"),
            ModuleId: moduleId,
            ModuleName: moduleName,
            CreatedUtc: DateTimeOffset.UtcNow,
            Changes: changes);
    }
}
