namespace SlopClean.Core.Models;

public sealed record OemDriverAssociatedDevice(
    string InstanceId,
    string? FriendlyName,
    string? Description,
    bool IsPresent)
{
    public string DisplayName
        => !string.IsNullOrWhiteSpace(FriendlyName)
            ? FriendlyName
            : !string.IsNullOrWhiteSpace(Description)
                ? Description!
                : InstanceId;
}
