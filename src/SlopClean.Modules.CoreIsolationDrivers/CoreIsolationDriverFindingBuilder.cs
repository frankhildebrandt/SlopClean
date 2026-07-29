using System.Text;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;

namespace SlopClean.Modules.CoreIsolationDrivers;

internal static class CoreIsolationDriverFindingBuilder
{
    private static string ModuleId => CoreIsolationDriversModule.ModuleId;

    public static ScanFinding BuildStatusFinding(DeviceGuardSnapshot status, bool includeDebug)
    {
        var details = new StringBuilder();
        details.Append("Memory Integrity configured: ").Append(status.MemoryIntegrityConfigured)
            .Append("; running: ").Append(status.MemoryIntegrityRunning)
            .Append("; VBS: ").Append(status.VirtualizationBasedSecurity)
            .Append(". ").Append(status.Summary);
        if (includeDebug)
        {
            details.Append(" [debug] hardware=").Append(status.StandardHardwareSecurity);
        }

        return new ScanFinding(
            Id: $"{ModuleId}:status",
            ModuleId: ModuleId,
            TargetId: "device-guard",
            DisplayName: "Core Isolation / Memory Integrity status",
            Path: null,
            SizeBytes: 0,
            Risk: FindingRisk.Informational,
            Details: details.ToString(),
            IsActionable: false,
            RequiredPrivilege: RequiredPrivilege.None,
            AllowedRoot: null);
    }

    public static ScanFinding BuildOrphanFinding(OemDriverPackage package, bool includeDebug)
    {
        var details = new StringBuilder();
        details.Append("What: OEM package ").Append(package.PublishedName)
            .Append(" (").Append(package.Provider).Append("). ");
        details.Append("Why: No associated devices (connected or disconnected/phantom). Safe orphan candidate for driver-store cleanup that can help Memory Integrity readiness.");
        CoreIsolationDriverIdentityFormatter.AppendDriverIdentity(details, package);
        if (includeDebug)
        {
            AppendDebug(details, package, null);
        }

        return new ScanFinding(
            Id: $"{ModuleId}:orphan:{package.PublishedName}",
            ModuleId: ModuleId,
            TargetId: package.PublishedName,
            DisplayName: CoreIsolationDriverIdentityFormatter.FormatPackageDisplayName("Orphan driver", package),
            Path: package.PublishedName,
            SizeBytes: package.ApproximateSizeBytes,
            Risk: FindingRisk.Medium,
            Details: details.ToString(),
            IsActionable: true,
            RequiredPrivilege: RequiredPrivilege.Elevated,
            AllowedRoot: null,
            Metadata: BuildActionMetadata(package, orphan: true, allowInUse: false));
    }

    public static ScanFinding BuildBlockerFinding(
        OemDriverPackage package,
        CodeIntegritySignal signal,
        bool allowInUse,
        bool includeDebug)
    {
        var details = new StringBuilder();
        details.Append("What: Package ").Append(package.PublishedName)
            .Append(" linked to observed CI image ").Append(signal.ImageFileName).Append(". ");
        details.Append("Why: Appears in Code Integrity Operational events (observed signal, not a complete HVCI incompatibility scan). ");
        if (package.TotalDeviceCount > 0)
        {
            details.Append("Bound to ")
                .Append(package.ConnectedDeviceCount).Append(" present and ")
                .Append(package.DisconnectedDeviceCount).Append(" disconnected device(s). ");
        }

        if (allowInUse)
        {
            details.Append("WARNING: Opt-in removal enabled. Restore is best-effort and may not rebind devices. Reboot may be required.");
        }
        else
        {
            details.Append("Not removable unless you enable 'Allow remove in-use blockers'.");
        }

        CoreIsolationDriverIdentityFormatter.AppendDriverIdentity(details, package);
        if (includeDebug)
        {
            AppendDebug(details, package, signal);
        }

        var actionable = allowInUse
                         && !CriticalDriverClassGuids.IsDenied(package.ClassGuid)
                         && !package.IsBootCritical
                         && !package.Provider.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);

        return new ScanFinding(
            Id: $"{ModuleId}:blocker:{package.PublishedName}",
            ModuleId: ModuleId,
            TargetId: package.PublishedName,
            DisplayName: CoreIsolationDriverIdentityFormatter.FormatPackageDisplayName("Observed CI blocker", package),
            Path: package.PublishedName,
            SizeBytes: package.ApproximateSizeBytes,
            Risk: FindingRisk.High,
            Details: details.ToString(),
            IsActionable: actionable,
            RequiredPrivilege: RequiredPrivilege.Elevated,
            AllowedRoot: null,
            Metadata: actionable ? BuildActionMetadata(package, orphan: false, allowInUse: true) : null);
    }

    public static ScanFinding Degraded(string targetId, string details)
        => new(
            Id: $"{ModuleId}:{targetId}",
            ModuleId: ModuleId,
            TargetId: targetId,
            DisplayName: "Driver scan degraded",
            Path: null,
            SizeBytes: 0,
            Risk: FindingRisk.Informational,
            Details: details,
            IsActionable: false,
            RequiredPrivilege: RequiredPrivilege.None,
            AllowedRoot: null);

    public static string BuildUnmatchedDetails(CodeIntegritySignal signal, int ownerCount, bool includeDebug)
    {
        var details =
            $"What: Observed CI event for '{signal.ImageFileName}'. Why: Could not map uniquely to one OEM package (owners={ownerCount}). Informational only.";
        if (includeDebug)
        {
            details += $" [debug] eventId={signal.EventId}; utc={signal.TimestampUtc:u}";
        }

        return details;
    }

    private static Dictionary<string, string> BuildActionMetadata(OemDriverPackage package, bool orphan, bool allowInUse)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            [OptimizationAction.OperationCodeMetadataKey] = PrivilegedOperationCodes.DeleteDriverPackage,
            [DriverPackagePayloadKeys.PublishedName] = package.PublishedName,
            [DriverPackagePayloadKeys.OriginalName] = package.OriginalName,
            [DriverPackagePayloadKeys.Provider] = package.Provider,
            [DriverPackagePayloadKeys.ClassGuid] = package.ClassGuid.ToString("D"),
            [DriverPackagePayloadKeys.PackageFingerprint] = package.PackageFingerprint,
            [DriverPackagePayloadKeys.RemovalMode] = orphan
                ? DriverPackagePayloadKeys.RemovalModeOrphan
                : DriverPackagePayloadKeys.RemovalModeInUse,
            [DriverPackagePayloadKeys.AllowInUse] = allowInUse ? "true" : "false",
            [DriverPackagePayloadKeys.IsBootCritical] = package.IsBootCritical ? "true" : "false",
            [DriverPackagePayloadKeys.IsMicrosoftProvider] =
                package.Provider.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ? "true" : "false",
            [DriverPackagePayloadKeys.BestEffortRestore] = orphan ? "false" : "true"
        };

    private static void AppendDebug(StringBuilder details, OemDriverPackage package, CodeIntegritySignal? signal)
    {
        details.Append(" [debug] published=").Append(package.PublishedName)
            .Append("; original=").Append(package.OriginalName)
            .Append("; provider=").Append(package.Provider)
            .Append("; classGuid=").Append(package.ClassGuid.ToString("D"))
            .Append("; fingerprint=").Append(package.PackageFingerprint)
            .Append("; connected=").Append(package.ConnectedDeviceCount)
            .Append("; disconnected=").Append(package.DisconnectedDeviceCount);
        if (package.AssociatedDeviceInstanceIds.Count > 0)
        {
            details.Append("; devices=")
                .Append(string.Join(',', package.AssociatedDeviceInstanceIds.Take(5)));
        }

        if (signal is not null)
        {
            details.Append("; eventId=").Append(signal.EventId)
                .Append("; eventUtc=").Append(signal.TimestampUtc.ToString("u"));
        }
    }
}
