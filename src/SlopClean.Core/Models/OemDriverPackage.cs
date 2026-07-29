namespace SlopClean.Core.Models;

public sealed record OemDriverPackage(
    string PublishedName,
    string OriginalName,
    string Provider,
    Guid ClassGuid,
    string PackageFingerprint,
    IReadOnlyList<string> AssociatedDeviceInstanceIds,
    int ConnectedDeviceCount,
    int DisconnectedDeviceCount,
    bool IsBootCritical,
    long ApproximateSizeBytes = 0,
    string? ClassName = null,
    string? DriverVersion = null,
    DateOnly? DriverDate = null,
    DateTimeOffset? InfLastWriteUtc = null,
    IReadOnlyList<OemDriverAssociatedDevice>? AssociatedDevices = null)
{
    public int TotalDeviceCount => ConnectedDeviceCount + DisconnectedDeviceCount;

    public bool IsOrphanCandidate => TotalDeviceCount == 0 && !IsBootCritical;

    public bool IsCurrentlyInUse => ConnectedDeviceCount > 0;

    public IReadOnlyList<OemDriverAssociatedDevice> Devices
        => AssociatedDevices ?? [];
}
