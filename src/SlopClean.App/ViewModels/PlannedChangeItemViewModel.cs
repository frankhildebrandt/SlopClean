using SlopClean.Core.Models;

namespace SlopClean.App.ViewModels;

public sealed class PlannedChangeItemViewModel
{
    public PlannedChangeItemViewModel(PlannedChange change)
    {
        Change = change;
    }

    public PlannedChange Change { get; }

    public string Title => Change.DisplayName;
    public string Details => Change.Path ?? Change.Details ?? "";
    public string SizeText => FormatBytes(Change.SizeBytes);
    public string RiskText => Change.Risk.ToString();
    public string PrivilegeText => Change.RequiredPrivilege == RequiredPrivilege.Elevated ? "Admin" : "User";
    public string RestorableText => Change.IsRestorable ? "Backup" : "Not restorable";
    public string RestorableReason => Change.RestorableReason;

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var i = 0;
        while (value >= 1024 && i < suffixes.Length - 1)
        {
            value /= 1024;
            i++;
        }

        return $"{value:0.##} {suffixes[i]}";
    }
}
