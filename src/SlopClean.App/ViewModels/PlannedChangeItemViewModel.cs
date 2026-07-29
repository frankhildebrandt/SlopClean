using SlopClean.App.Helpers;
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
    public string SizeText => ByteSizeFormatter.Format(Change.SizeBytes);
    public string RiskText => Change.Risk.ToString();
    public string PrivilegeText => Change.RequiredPrivilege == RequiredPrivilege.Elevated ? "Admin" : "User";
    public string RestorableText => Change.IsRestorable ? "Backup" : "Not restorable";
    public string RestorableReason => Change.RestorableReason;
}
