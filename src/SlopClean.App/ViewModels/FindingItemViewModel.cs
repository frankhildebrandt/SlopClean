using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using SlopClean.App.Helpers;
using SlopClean.Core.Models;

namespace SlopClean.App.ViewModels;

public partial class FindingItemViewModel : ObservableObject
{
    public FindingItemViewModel(ScanFinding finding)
    {
        Finding = finding;
        IsSelected = finding.IsActionable;
        GoToFolderLabel = ResolveGoToFolderLabel();
    }

    public ScanFinding Finding { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string Title => Finding.DisplayName;
    public string Details => Finding.Details;
    public string SizeText => ByteSizeFormatter.Format(Finding.SizeBytes);
    public string RiskText => Finding.Risk.ToString();
    public bool IsActionable => Finding.IsActionable;
    public string GoToFolderLabel { get; }
    public bool CanGoToFolder => ShellReveal.CanReveal(Finding.Path);

    [RelayCommand(CanExecute = nameof(CanGoToFolder))]
    private void GoToFolder()
    {
        if (Finding.Path is null || !CanGoToFolder)
        {
            return;
        }

        ShellReveal.OpenInExplorer(Finding.Path);
    }

    private static string ResolveGoToFolderLabel()
    {
        try
        {
            var label = new ResourceLoader().GetString("FindingList_GoToFolder");
            return string.IsNullOrWhiteSpace(label) ? "Go to Folder" : label;
        }
        catch
        {
            return "Go to Folder";
        }
    }
}
