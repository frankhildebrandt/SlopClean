using CommunityToolkit.Mvvm.ComponentModel;
using SlopClean.App.Helpers;
using SlopClean.Core.Models;

namespace SlopClean.App.ViewModels;

public partial class FindingItemViewModel : ObservableObject
{
    public FindingItemViewModel(ScanFinding finding)
    {
        Finding = finding;
        IsSelected = finding.IsActionable;
    }

    public ScanFinding Finding { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string Title => Finding.DisplayName;
    public string Details => Finding.Details;
    public string SizeText => ByteSizeFormatter.Format(Finding.SizeBytes);
    public string RiskText => Finding.Risk.ToString();
    public bool IsActionable => Finding.IsActionable;
}
