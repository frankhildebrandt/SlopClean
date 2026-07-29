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

    public static ScanFinding BuildOrphanFinding(OemDriverPackage package, bool includeDebug, bool allowInUse)
    {
        var details = new StringBuilder();
        CoreIsolationDriverIdentityFormatter.AppendHumanReadableSummary(details, package, imageFileName: null);
        details.Append(" Why: Optional orphan OEM package with no associated devices.");
        if (allowInUse)
        {
            details.Append(" Opt-in in-use removal is enabled: delete will uninstall if devices still reference it.");
        }

        if (includeDebug)
        {
            AppendDebug(details, package, null);
        }

        return new ScanFinding(
            Id: $"{ModuleId}:orphan:{package.PublishedName}",
            ModuleId: ModuleId,
            TargetId: package.PublishedName,
            DisplayName: CoreIsolationDriverIdentityFormatter.FormatBlockerDisplayName(package, imageFileName: null),
            Path: package.PublishedName,
            SizeBytes: package.ApproximateSizeBytes,
            Risk: FindingRisk.Medium,
            Details: details.ToString(),
            IsActionable: true,
            RequiredPrivilege: RequiredPrivilege.Elevated,
            AllowedRoot: null,
            Metadata: BuildActionMetadata(package, orphan: true, allowInUse: allowInUse));
    }

    public static ScanFinding BuildBlockerFinding(
        OemDriverPackage package,
        CodeIntegritySignal signal,
        bool allowInUse,
        bool includeDebug)
    {
        var details = new StringBuilder();
        CoreIsolationDriverIdentityFormatter.AppendHumanReadableSummary(details, package, signal.ImageFileName);
        if (signal.EventId == 0)
        {
            details.Append(" Why: ").Append(signal.RawMessage)
                .Append(" (local Memory Integrity heuristic; not a full Microsoft hvciscan).");
        }
        else
        {
            details.Append(" Why: Code Integrity reports this driver as incompatible with Memory Integrity (observed signal, not a full HVCI scan).");
        }

        var bound = package.TotalDeviceCount > 0;
        if (bound && allowInUse)
        {
            details.Append(" WARNING: Opt-in removal enabled. Restore is best-effort and may not rebind devices. Reboot may be required.");
        }
        else if (bound)
        {
            details.Append(" Not removable unless you enable 'Allow remove in-use blockers'.");
        }

        if (includeDebug)
        {
            AppendDebug(details, package, signal);
        }

        var protectedPackage = CriticalDriverClassGuids.IsDenied(package.ClassGuid)
                               || package.IsBootCritical
                               || package.Provider.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);
        var actionable = !protectedPackage && (!bound || allowInUse);

        return new ScanFinding(
            Id: $"{ModuleId}:blocker:{package.PublishedName}",
            ModuleId: ModuleId,
            TargetId: package.PublishedName,
            DisplayName: CoreIsolationDriverIdentityFormatter.FormatBlockerDisplayName(package, signal.ImageFileName),
            Path: package.PublishedName,
            SizeBytes: package.ApproximateSizeBytes,
            Risk: FindingRisk.High,
            Details: details.ToString(),
            IsActionable: actionable,
            RequiredPrivilege: RequiredPrivilege.Elevated,
            AllowedRoot: null,
            Metadata: actionable
                ? BuildActionMetadata(package, orphan: !bound, allowInUse: bound && allowInUse)
                : null);
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
            $"Driver file: {signal.ImageFileName ?? "unknown"}. Why: Could not map uniquely to one OEM package (owners={ownerCount}). Informational only.";
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
            [DriverPackagePayloadKeys.BestEffortRestore] = orphan && !allowInUse ? "false" : "true"
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
