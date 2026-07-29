using System.Runtime.CompilerServices;
using System.Text;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;
using SlopClean.Core.Safety;

namespace SlopClean.Modules.CoreIsolationDrivers;

public sealed class CoreIsolationDriversModule : IScannableModule, IApplicableModule
{
    public const string ModuleId = "core-isolation-drivers";
    private static readonly TimeSpan CiLookback = TimeSpan.FromDays(30);
    private readonly IDriverStore _driverStore;
    private readonly IDeviceGuardStatus _deviceGuard;
    private readonly ICodeIntegrityInspector _codeIntegrity;
    private readonly BoolParameter _includeDebugDetails;
    private readonly BoolParameter _allowRemoveInUseBlockers;

    public CoreIsolationDriversModule(
        IDriverStore driverStore,
        IDeviceGuardStatus deviceGuard,
        ICodeIntegrityInspector codeIntegrity)
    {
        _driverStore = driverStore;
        _deviceGuard = deviceGuard;
        _codeIntegrity = codeIntegrity;
        _includeDebugDetails = new BoolParameter(
            "IncludeDebugDetails",
            "Debug details",
            "Include published name, class GUID, fingerprint, device counts, and CI event IDs in finding details.",
            defaultValue: true);
        _allowRemoveInUseBlockers = new BoolParameter(
            "AllowRemoveInUseBlockers",
            "Allow remove in-use blockers",
            "WARNING: Removes driver packages still bound to devices. Risk of BSOD or broken hardware. Prefer vendor updates. Restore re-stages the package only (device binding not guaranteed).",
            defaultValue: false);
    }

    public string Id => ModuleId;
    public string Name => "Core Isolation Drivers";
    public string Description =>
        "Finds orphan OEM driver packages and observed Code Integrity / HVCI signals that may block Memory Integrity. Removes orphans by default; in-use blockers only with explicit opt-in.";
    public ModuleCategory Category => ModuleCategory.Cleanup;
    public IReadOnlyList<IModuleParameter> Parameters => [_includeDebugDetails, _allowRemoveInUseBlockers];

    public async IAsyncEnumerable<ScanFinding> ScanAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IProgress<ScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var includeDebug = _includeDebugDetails.Resolve(parameters);
        var allowInUse = _allowRemoveInUseBlockers.Resolve(parameters);

        progress?.Report(new ScanProgress(ModuleId, "Reading Device Guard status…", 0, 3));
        var status = _deviceGuard.GetSnapshot();
        yield return BuildStatusFinding(status, includeDebug);

        if (status.StandardHardwareSecurity == DeviceGuardFeatureState.Unavailable)
        {
            yield return new ScanFinding(
                Id: $"{ModuleId}:hardware-security",
                ModuleId: ModuleId,
                TargetId: "hardware",
                DisplayName: "Standard hardware security not supported",
                Path: null,
                SizeBytes: 0,
                Risk: FindingRisk.Informational,
                Details: "Windows reports that standard hardware security is unavailable. Cleaning drivers alone may not enable Memory Integrity.",
                IsActionable: false,
                RequiredPrivilege: RequiredPrivilege.None,
                AllowedRoot: null);
        }

        progress?.Report(new ScanProgress(ModuleId, "Enumerating OEM driver packages…", 1, 3));
        if (!_driverStore.IsEnumerationAvailable)
        {
            yield return Degraded("enumeration-unavailable", "Driver store enumeration is unavailable on this system. No driver packages can be removed.");
            yield break;
        }

        var enumeration = _driverStore.EnumerateOemPackages();
        if (!enumeration.IsAuthoritative)
        {
            yield return Degraded(
                "enumeration-failed",
                $"Authoritative driver enumeration failed ({enumeration.FailureReason}). No packages will be marked for removal.");
            yield break;
        }

        progress?.Report(new ScanProgress(ModuleId, "Reading Code Integrity signals…", 2, 3));
        var ci = _codeIntegrity.ReadObservedSignals(CiLookback, cancellationToken);
        if (!ci.IsAvailable)
        {
            yield return new ScanFinding(
                Id: $"{ModuleId}:ci-unavailable",
                ModuleId: ModuleId,
                TargetId: "code-integrity",
                DisplayName: "Code Integrity signals unavailable",
                Path: null,
                SizeBytes: 0,
                Risk: FindingRisk.Informational,
                Details: $"{ci.FailureReason} Blocker detection is limited to orphan package analysis. This is not a complete Memory Integrity incompatibility scan.",
                IsActionable: false,
                RequiredPrivilege: RequiredPrivilege.None,
                AllowedRoot: null);
        }

        var matchedPublished = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (ci.IsAvailable)
        {
            foreach (var signal in ci.Signals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var owners = FindExactPackageOwners(enumeration.Packages, signal);
                if (owners.Count != 1)
                {
                    var target = signal.ImageFileName ?? $"event-{signal.EventId}";
                    yield return new ScanFinding(
                        Id: $"{ModuleId}:ci-unmatched:{signal.EventId}:{target}",
                        ModuleId: ModuleId,
                        TargetId: target,
                        DisplayName: $"Observed CI signal: {target}",
                        Path: signal.ImageFileName,
                        SizeBytes: 0,
                        Risk: FindingRisk.Informational,
                        Details: BuildUnmatchedDetails(signal, owners.Count, includeDebug),
                        IsActionable: false,
                        RequiredPrivilege: RequiredPrivilege.None,
                        AllowedRoot: null);
                    continue;
                }

                var package = owners[0];
                if (!matchedPublished.Add(package.PublishedName))
                {
                    continue;
                }

                yield return BuildBlockerFinding(package, signal, allowInUse, includeDebug);
            }
        }

        var completed = 0;
        foreach (var package in enumeration.Packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            completed++;
            progress?.Report(new ScanProgress(
                ModuleId,
                $"Classifying {package.PublishedName}",
                completed,
                enumeration.Packages.Count));

            if (matchedPublished.Contains(package.PublishedName))
            {
                continue;
            }

            if (CriticalDriverClassGuids.IsDenied(package.ClassGuid) || package.IsBootCritical)
            {
                continue;
            }

            if (!package.IsOrphanCandidate)
            {
                continue;
            }

            yield return BuildOrphanFinding(package, includeDebug);
            await Task.Yield();
        }
    }

    public Task<ApplyResult> ApplyAsync(OptimizationAction action, CancellationToken cancellationToken)
    {
        if (action.OperationCode is PrivilegedOperationCodes.DeleteDriverPackage
            or PrivilegedOperationCodes.RestoreDriverPackage)
        {
            return Task.FromResult(ApplyResult.Failed(
                action.Id,
                action.FindingId,
                "Driver-package operations must run through the elevated helper."));
        }

        return Task.FromResult(ApplyResult.Failed(action.Id, action.FindingId, "Unsupported operation for this module."));
    }

    private static ScanFinding BuildStatusFinding(DeviceGuardSnapshot status, bool includeDebug)
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

    private static ScanFinding BuildOrphanFinding(OemDriverPackage package, bool includeDebug)
    {
        var details = new StringBuilder();
        details.Append("What: OEM package ").Append(package.PublishedName)
            .Append(" (").Append(package.Provider).Append("). ");
        details.Append("Why: No associated devices (connected or disconnected/phantom). Safe orphan candidate for driver-store cleanup that can help Memory Integrity readiness.");
        if (includeDebug)
        {
            AppendDebug(details, package, null);
        }

        return new ScanFinding(
            Id: $"{ModuleId}:orphan:{package.PublishedName}",
            ModuleId: ModuleId,
            TargetId: package.PublishedName,
            DisplayName: $"Orphan driver: {package.OriginalName}",
            Path: package.PublishedName,
            SizeBytes: package.ApproximateSizeBytes,
            Risk: FindingRisk.Medium,
            Details: details.ToString(),
            IsActionable: true,
            RequiredPrivilege: RequiredPrivilege.Elevated,
            AllowedRoot: null,
            Metadata: BuildActionMetadata(package, orphan: true, allowInUse: false));
    }

    private static ScanFinding BuildBlockerFinding(
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
            DisplayName: $"Observed CI blocker: {package.OriginalName}",
            Path: package.PublishedName,
            SizeBytes: package.ApproximateSizeBytes,
            Risk: FindingRisk.High,
            Details: details.ToString(),
            IsActionable: actionable,
            RequiredPrivilege: RequiredPrivilege.Elevated,
            AllowedRoot: null,
            Metadata: actionable ? BuildActionMetadata(package, orphan: false, allowInUse: true) : null);
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

    private static string BuildUnmatchedDetails(CodeIntegritySignal signal, int ownerCount, bool includeDebug)
    {
        var details =
            $"What: Observed CI event for '{signal.ImageFileName}'. Why: Could not map uniquely to one OEM package (owners={ownerCount}). Informational only.";
        if (includeDebug)
        {
            details += $" [debug] eventId={signal.EventId}; utc={signal.TimestampUtc:u}";
        }

        return details;
    }

    private static ScanFinding Degraded(string targetId, string details)
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

    private static List<OemDriverPackage> FindExactPackageOwners(
        IReadOnlyList<OemDriverPackage> packages,
        CodeIntegritySignal signal)
    {
        var haystack = $"{signal.ImageFileName}\n{signal.RawMessage}";
        var hits = new List<OemDriverPackage>();
        foreach (var package in packages)
        {
            // Exact ownership tokens only — published OEM name or original INF name in the event text.
            if (haystack.Contains(package.PublishedName, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(package.OriginalName)
                    && !package.OriginalName.Equals(package.PublishedName, StringComparison.OrdinalIgnoreCase)
                    && haystack.Contains(package.OriginalName, StringComparison.OrdinalIgnoreCase)))
            {
                hits.Add(package);
            }
        }

        return hits;
    }
}
