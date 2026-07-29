using CommunityToolkit.Mvvm.ComponentModel;
using SlopClean.Core.Backup;

namespace SlopClean.App.ViewModels;

public partial class RestorePointItemViewModel : ObservableObject
{
    public RestorePointItemViewModel(RestorePointManifest manifest)
    {
        Manifest = manifest;
    }

    public RestorePointManifest Manifest { get; }

    public string Id => Manifest.Id;
    public string Title => string.IsNullOrWhiteSpace(Manifest.DisplayName) ? Manifest.FindingId : Manifest.DisplayName;
    public string Details => Manifest.OriginalPath
        ?? Manifest.Metadata.GetValueOrDefault("subKey")
        ?? Manifest.OperationCode;
    public string ModuleText => Manifest.ModuleId;
    public string CreatedText => Manifest.CreatedUtc.ToLocalTime().ToString("g");
    public string KindText => Manifest.Kind.ToString();

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
