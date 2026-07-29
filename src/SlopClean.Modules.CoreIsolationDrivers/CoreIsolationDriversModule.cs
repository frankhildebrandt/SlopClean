using System.Runtime.CompilerServices;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;
using SlopClean.Core.Safety;

namespace SlopClean.Modules.CoreIsolationDrivers;

public sealed class CoreIsolationDriversModule : IScannableModule, IApplicableModule, IModuleIllustration
{
    public const string ModuleId = "core-isolation-drivers";
    private static readonly TimeSpan CiLookback = TimeSpan.FromDays(30);
    private readonly IDriverStore _driverStore;
    private readonly IDeviceGuardStatus _deviceGuard;
    private readonly ICodeIntegrityInspector _codeIntegrity;
    private readonly IHvciCompatibilityInspector _hvci;
    private readonly BoolParameter _includeDebugDetails;
    private readonly BoolParameter _allowRemoveInUseBlockers;
    private readonly BoolParameter _includeOrphanOemPackages;

    public CoreIsolationDriversModule(
        IDriverStore driverStore,
        IDeviceGuardStatus deviceGuard,
        ICodeIntegrityInspector codeIntegrity,
        IHvciCompatibilityInspector hvci)
    {
        _driverStore = driverStore;
        _deviceGuard = deviceGuard;
        _codeIntegrity = codeIntegrity;
        _hvci = hvci;
        _includeDebugDetails = new BoolParameter(
            "IncludeDebugDetails",
            "Debug details",
            "Include published name, class GUID, fingerprint, device counts, and CI event IDs in finding details.",
            defaultValue: true);
        _allowRemoveInUseBlockers = new BoolParameter(
            "AllowRemoveInUseBlockers",
            "Allow remove in-use blockers",
            "WARNING: Removes CI/HVCI-reported packages still bound to devices. Risk of BSOD or broken hardware. Prefer vendor updates. Restore re-stages the package only (device binding not guaranteed).",
            defaultValue: false);
        _includeOrphanOemPackages = new BoolParameter(
            "IncludeOrphanOemPackages",
            "Include orphan OEM packages",
            "Optional broader cleanup: also list OEM packages with no associated devices. Off by default so results stay close to Windows Memory Integrity incompatible drivers.",
            defaultValue: false);
    }

    public string Id => ModuleId;
    public string Name => "Core Isolation Drivers";
    public string Description =>
        "Finds OEM driver packages incompatible with Memory Integrity via Code Integrity events and local HVCI heuristics (e.g. writable+executable sections). Optional orphan OEM cleanup is opt-in; in-use blockers require explicit allow.";
    public ModuleCategory Category => ModuleCategory.Cleanup;
    public IReadOnlyList<IModuleParameter> Parameters =>
    [
        _includeDebugDetails,
        _allowRemoveInUseBlockers,
        _includeOrphanOemPackages
    ];

    public Stream OpenIllustration() => EmbeddedResourceStreams.OpenModuleIllustration(typeof(CoreIsolationDriversModule));

    public async IAsyncEnumerable<ScanFinding> ScanAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IProgress<ScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var includeDebug = _includeDebugDetails.Resolve(parameters);
        var allowInUse = _allowRemoveInUseBlockers.Resolve(parameters);
        var includeOrphans = _includeOrphanOemPackages.Resolve(parameters);

        progress?.Report(new ScanProgress(ModuleId, "Reading Device Guard status…", 0, 4));
        var status = _deviceGuard.GetSnapshot();
        yield return CoreIsolationDriverFindingBuilder.BuildStatusFinding(status, includeDebug);

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

        progress?.Report(new ScanProgress(ModuleId, "Enumerating OEM driver packages…", 1, 4));
        if (!_driverStore.IsEnumerationAvailable)
        {
            yield return CoreIsolationDriverFindingBuilder.Degraded(
                "enumeration-unavailable",
                "Driver store enumeration is unavailable on this system. No driver packages can be removed.");
            yield break;
        }

        var enumeration = _driverStore.EnumerateOemPackages();
        if (!enumeration.IsAuthoritative)
        {
            yield return CoreIsolationDriverFindingBuilder.Degraded(
                "enumeration-failed",
                $"Authoritative driver enumeration failed ({enumeration.FailureReason}). No packages will be marked for removal.");
            yield break;
        }

        progress?.Report(new ScanProgress(ModuleId, "Reading Code Integrity signals…", 2, 4));
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
                Details: $"{ci.FailureReason} Continuing with local Memory Integrity heuristics on OEM driver images.",
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
                var owners = CoreIsolationDriverPackageMatcher.FindExactPackageOwners(enumeration.Packages, signal);
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
                        Details: CoreIsolationDriverFindingBuilder.BuildUnmatchedDetails(signal, owners.Count, includeDebug),
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

                yield return CoreIsolationDriverFindingBuilder.BuildBlockerFinding(
                    package,
                    signal,
                    allowInUse,
                    includeDebug);
            }
        }

        progress?.Report(new ScanProgress(ModuleId, "Analyzing OEM driver images for HVCI issues…", 3, 4));
        var analyzed = 0;
        foreach (var package in enumeration.Packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            analyzed++;
            if (analyzed % 10 == 0)
            {
                progress?.Report(new ScanProgress(
                    ModuleId,
                    $"Analyzing {package.PublishedName}",
                    analyzed,
                    enumeration.Packages.Count));
            }

            if (matchedPublished.Contains(package.PublishedName))
            {
                continue;
            }

            if (CriticalDriverClassGuids.IsDenied(package.ClassGuid)
                || package.IsBootCritical
                || package.Provider.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryFindHvciIncompatibleImage(package, out var imageName, out var reason))
            {
                continue;
            }

            matchedPublished.Add(package.PublishedName);
            var signal = new CodeIntegritySignal(
                EventId: 0,
                TimestampUtc: DateTimeOffset.UtcNow,
                ImageFileName: imageName,
                Publisher: package.Provider,
                RawMessage: reason);
            yield return CoreIsolationDriverFindingBuilder.BuildBlockerFinding(
                package,
                signal,
                allowInUse,
                includeDebug);
            await Task.Yield();
        }

        if (!includeOrphans)
        {
            yield break;
        }

        var completed = 0;
        foreach (var package in enumeration.Packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            completed++;
            progress?.Report(new ScanProgress(
                ModuleId,
                $"Classifying orphans {package.PublishedName}",
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

            yield return CoreIsolationDriverFindingBuilder.BuildOrphanFinding(
                package,
                includeDebug,
                allowInUse);
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

    private bool TryFindHvciIncompatibleImage(
        OemDriverPackage package,
        out string imageName,
        out string reason)
    {
        imageName = "";
        reason = "";
        foreach (var image in package.ImageFileNames)
        {
            foreach (var path in DriverImagePathResolver.CandidatePaths(image))
            {
                var analysis = _hvci.AnalyzeDriverImage(path);
                if (!analysis.Analyzed || !analysis.IsIncompatibleWithMemoryIntegrity)
                {
                    continue;
                }

                imageName = image;
                reason = analysis.Reason
                         ?? "Local analysis reports the driver image as incompatible with Memory Integrity.";
                return true;
            }
        }

        return false;
    }
}
