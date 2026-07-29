using CommunityToolkit.Mvvm.ComponentModel;
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
    public string SizeText => FormatBytes(Finding.SizeBytes);
    public string RiskText => Finding.Risk.ToString();
    public bool IsActionable => Finding.IsActionable;

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
