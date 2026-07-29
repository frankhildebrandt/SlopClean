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
    long ApproximateSizeBytes = 0)
{
    public int TotalDeviceCount => ConnectedDeviceCount + DisconnectedDeviceCount;

    public bool IsOrphanCandidate => TotalDeviceCount == 0 && !IsBootCritical;
}
