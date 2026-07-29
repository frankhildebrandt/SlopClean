using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;

namespace SlopClean.Platform.Windows;

public static class DriverPackageElevatedOperations
{
    public static ApplyResult Execute(OptimizationAction action, IDriverStore driverStore)
    {
        if (action.Payload is null)
        {
            return ApplyResult.Failed(action.Id, action.FindingId, "Driver payload missing.");
        }

        return action.OperationCode switch
        {
            PrivilegedOperationCodes.DeleteDriverPackage => Delete(action, driverStore),
            PrivilegedOperationCodes.RestoreDriverPackage => Restore(action, driverStore),
            _ => ApplyResult.Failed(action.Id, action.FindingId, "Unsupported driver operation.")
        };
    }

    private static ApplyResult Delete(OptimizationAction action, IDriverStore driverStore)
    {
        var published = action.Payload![DriverPackagePayloadKeys.PublishedName];
        var expectedFingerprint = action.Payload[DriverPackagePayloadKeys.PackageFingerprint];
        var removalMode = action.Payload[DriverPackagePayloadKeys.RemovalMode];
        var allowInUse = action.Payload.TryGetValue(DriverPackagePayloadKeys.AllowInUse, out var allowText)
                         && bool.TryParse(allowText, out var allow)
                         && allow;

        var live = driverStore.FindPackage(published);
        if (live is null)
        {
            return ApplyResult.Skipped(action.Id, action.FindingId, "Driver package no longer exists.");
        }

        if (!string.Equals(live.PackageFingerprint, expectedFingerprint, StringComparison.Ordinal))
        {
            return ApplyResult.Skipped(action.Id, action.FindingId, "Driver package fingerprint changed since scan (TOCTOU).");
        }

        if (CriticalDriverClassGuids.IsDenied(live.ClassGuid) || live.IsBootCritical)
        {
            return ApplyResult.Skipped(action.Id, action.FindingId, "Driver package is protected.");
        }

        if (live.TotalDeviceCount > 0
            && !(allowInUse && removalMode.Equals(DriverPackagePayloadKeys.RemovalModeInUse, StringComparison.OrdinalIgnoreCase)))
        {
            return ApplyResult.Skipped(
                action.Id,
                action.FindingId,
                $"Driver package is associated with {live.TotalDeviceCount} device(s), including disconnected.");
        }

        if (!action.Payload.TryGetValue(DriverPackagePayloadKeys.RestorePayloadDirectory, out var packageDir)
            || string.IsNullOrWhiteSpace(packageDir))
        {
            return ApplyResult.Failed(action.Id, action.FindingId, "Restore payload directory missing for driver export.");
        }

        Directory.CreateDirectory(packageDir);
        var export = driverStore.ExportPackage(published, packageDir);
        if (!export.Succeeded)
        {
            return ApplyResult.Failed(action.Id, action.FindingId, $"Driver export failed: {export.Message}");
        }

        var identity = DriverPackageIdentity.Create(
            published,
            live.PackageFingerprint,
            live.ClassGuid.ToString("D"),
            live.Provider,
            packageDir);
        DriverPackageIdentity.Write(packageDir, identity);

        var uninstall = allowInUse
                        && removalMode.Equals(DriverPackagePayloadKeys.RemovalModeInUse, StringComparison.OrdinalIgnoreCase);
        var delete = driverStore.DeletePackage(published, uninstallFromDevices: uninstall);
        if (!delete.Succeeded)
        {
            return ApplyResult.Failed(action.Id, action.FindingId, $"Driver delete failed: {delete.Message}");
        }

        var message = uninstall
            ? "Driver package uninstalled and removed (restore is best-effort for device binding)."
            : "Orphan driver package removed.";
        if (delete.RebootRequired || export.RebootRequired)
        {
            return ApplyResult.SucceededRebootRequired(action.Id, action.FindingId, live.ApproximateSizeBytes, message + " Reboot required.");
        }

        return ApplyResult.Succeeded(action.Id, action.FindingId, live.ApproximateSizeBytes, message);
    }

    private static ApplyResult Restore(OptimizationAction action, IDriverStore driverStore)
    {
        var packageDir = action.Payload![DriverPackagePayloadKeys.RestorePayloadDirectory];
        var expectedFingerprint = action.Payload[DriverPackagePayloadKeys.PackageFingerprint];
        var published = action.Payload[DriverPackagePayloadKeys.PublishedName];

        var identity = DriverPackageIdentity.Read(packageDir);
        if (identity is null)
        {
            return ApplyResult.Failed(action.Id, action.FindingId, "Driver identity file missing; refusing restore.");
        }

        if (!string.Equals(identity.PublishedName, published, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(identity.PackageFingerprint, expectedFingerprint, StringComparison.Ordinal))
        {
            return ApplyResult.Failed(action.Id, action.FindingId, "Driver identity does not match restore manifest.");
        }

        if (!DriverPackageIdentity.Verify(packageDir, identity))
        {
            return ApplyResult.Failed(action.Id, action.FindingId, "Driver backup payload failed hash verification.");
        }

        var inf = Directory.EnumerateFiles(packageDir, "*.inf", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (inf is null)
        {
            return ApplyResult.Failed(action.Id, action.FindingId, "No INF found in driver backup payload.");
        }

        var add = driverStore.AddPackage(inf);
        if (!add.Succeeded)
        {
            return ApplyResult.Failed(action.Id, action.FindingId, $"Driver restore failed: {add.Message}");
        }

        var message = action.Payload.TryGetValue(DriverPackagePayloadKeys.BestEffortRestore, out var best)
                      && bool.TryParse(best, out var bestEffort)
                      && bestEffort
            ? "Driver package re-staged (best effort; device binding not guaranteed)."
            : "Driver package restored to the driver store.";

        return add.RebootRequired
            ? ApplyResult.SucceededRebootRequired(action.Id, action.FindingId, 0, message + " Reboot required.")
            : ApplyResult.Succeeded(action.Id, action.FindingId, 0, message);
    }
}
