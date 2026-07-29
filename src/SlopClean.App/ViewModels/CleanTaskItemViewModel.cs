using CommunityToolkit.Mvvm.ComponentModel;
using SlopClean.Core.Models;

namespace SlopClean.App.ViewModels;

public partial class CleanTaskItemViewModel : ObservableObject
{
    public CleanTaskItemViewModel(PlannedChange change)
    {
        ActionId = change.Action.Id;
        FindingId = change.Id;
        Title = change.DisplayName;
        Details = change.Path ?? change.Details ?? "";
        SizeText = FormatBytes(change.SizeBytes);
        ModuleId = change.Action.ModuleId;
        State = ApplyItemState.Pending;
        Change = change;
    }

    public PlannedChange Change { get; }
    public string ActionId { get; }
    public string FindingId { get; }
    public string Title { get; }
    public string Details { get; }
    public string SizeText { get; }
    public string ModuleId { get; }

    [ObservableProperty]
    public partial ApplyItemState State { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = "Pending";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Pending";

    public string Glyph => State switch
    {
        ApplyItemState.Pending => "\uE8DF",      // Checkbox
        ApplyItemState.Running => "\uE895",      // Sync
        ApplyItemState.Succeeded => "\uE73E",    // CheckMark
        ApplyItemState.Skipped => "\uE7BA",      // Warning
        ApplyItemState.Failed => "\uE783",       // ErrorBadge
        ApplyItemState.Cancelled => "\uE711",    // Cancel
        _ => "\uE8DF"
    };

    partial void OnStateChanged(ApplyItemState value)
    {
        StatusText = value.ToString();
        OnPropertyChanged(nameof(Glyph));
    }

    public void ApplyProgress(ApplyProgress progress)
    {
        State = progress.State;
        Message = progress.Message ?? progress.State.ToString();
    }

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
