using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
using SlopClean.Modules.CoreIsolationDrivers;

namespace SlopClean.Modules.CoreIsolationDrivers.Tests;

public class CoreIsolationDriversModuleTests
{
    private static readonly Guid MediaClass = new("4d36e96c-e325-11ce-bfc1-08002be10318");

    [Fact]
    public async Task Default_scan_ignores_orphans_without_ci_signal()
    {
        var module = CreateModule(packages: [Orphan("oem10.inf", "contoso.inf", "Contoso")]);

        var findings = await ScanAsync(module);

        Assert.DoesNotContain(findings, f => f.Id.Contains("orphan", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, f => f.IsActionable);
    }

    [Fact]
    public async Task Orphan_packages_are_actionable_only_when_include_orphans_enabled()
    {
        var module = CreateModule(packages: [Orphan("oem10.inf", "contoso.inf", "Contoso")]);

        var findings = await ScanAsync(module, includeOrphans: true);

        var orphan = Assert.Single(findings, f => f.Id.Contains("orphan", StringComparison.Ordinal));
        Assert.True(orphan.IsActionable);
        Assert.Equal(RequiredPrivilege.Elevated, orphan.RequiredPrivilege);
        Assert.Contains("Hardware:", orphan.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            PrivilegedOperationCodes.DeleteDriverPackage,
            orphan.Metadata![OptimizationAction.OperationCodeMetadataKey]);
    }

    [Fact]
    public async Task Finding_details_include_hardware_usage_version_and_age()
    {
        var package = new OemDriverPackage(
            PublishedName: "oem40.inf",
            OriginalName: "contoso.inf",
            Provider: "Contoso",
            ClassGuid: MediaClass,
            PackageFingerprint: "fp-40",
            AssociatedDeviceInstanceIds: ["USB\\VID_1234&PID_5678\\1"],
            ConnectedDeviceCount: 1,
            DisconnectedDeviceCount: 0,
            IsBootCritical: false,
            ApproximateSizeBytes: 2048,
            ClassName: "Media",
            DriverVersion: "1.2.3.4",
            DriverDate: new DateOnly(2022, 6, 15),
            InfLastWriteUtc: new DateTimeOffset(2022, 6, 16, 0, 0, 0, TimeSpan.Zero),
            AssociatedDevices:
            [
                new OemDriverAssociatedDevice(
                    "USB\\VID_1234&PID_5678\\1",
                    "Contoso USB Sound Adapter",
                    "USB Audio Device",
                    IsPresent: true)
            ]);

        var signal = new CodeIntegritySignal(
            3089,
            DateTimeOffset.UtcNow,
            "contoso.sys",
            null,
            "oem40.inf blocked for contoso.sys");

        var module = CreateModule([package], [signal]);
        var findings = await ScanAsync(module, allowInUse: false, includeDebug: false);
        var blocker = Assert.Single(findings, f => f.Id.Contains("blocker", StringComparison.Ordinal));

        Assert.Contains("Hardware:", blocker.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Contoso USB Sound Adapter", blocker.Details, StringComparison.Ordinal);
        Assert.Contains("In use:", blocker.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("yes", blocker.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Version:", blocker.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.2.3.4", blocker.Details, StringComparison.Ordinal);
        Assert.Contains("2022-06-15", blocker.Details, StringComparison.Ordinal);
        Assert.Contains("Age:", blocker.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Disconnected_device_prevents_orphan_classification()
    {
        var module = CreateModule(
            packages:
            [
                new OemDriverPackage(
                    "oem11.inf",
                    "dock.inf",
                    "DockCo",
                    MediaClass,
                    "fp-dock",
                    ["USB\\VID_1234&PID_5678\\1"],
                    ConnectedDeviceCount: 0,
                    DisconnectedDeviceCount: 1,
                    IsBootCritical: false)
            ]);

        var findings = await ScanAsync(module);
        Assert.DoesNotContain(findings, f => f.IsActionable);
    }

    [Fact]
    public async Task Orphan_action_respects_allow_in_use_parameter()
    {
        var module = CreateModule(packages: [Orphan("oem36.inf", "contoso.inf", "Contoso")]);

        var without = await ScanAsync(module, allowInUse: false, includeOrphans: true);
        var orphanWithout = Assert.Single(without, f => f.Id.Contains("orphan", StringComparison.Ordinal));
        Assert.Equal("false", orphanWithout.Metadata![DriverPackagePayloadKeys.AllowInUse]);

        var with = await ScanAsync(module, allowInUse: true, includeOrphans: true);
        var orphanWith = Assert.Single(with, f => f.Id.Contains("orphan", StringComparison.Ordinal));
        Assert.Equal("true", orphanWith.Metadata![DriverPackagePayloadKeys.AllowInUse]);
        Assert.Equal("true", orphanWith.Metadata[DriverPackagePayloadKeys.BestEffortRestore]);
    }

    [Fact]
    public async Task Bound_package_without_ci_signal_is_never_actionable()
    {
        var bound = new OemDriverPackage(
            "oem36.inf",
            "contoso.inf",
            "Contoso",
            MediaClass,
            "fp-36",
            ["USB\\VID_1234&PID_5678\\1"],
            ConnectedDeviceCount: 1,
            DisconnectedDeviceCount: 0,
            IsBootCritical: false,
            AssociatedDevices:
            [
                new OemDriverAssociatedDevice(
                    "USB\\VID_1234&PID_5678\\1",
                    "Contoso USB Sound Adapter",
                    "USB Audio Device",
                    IsPresent: true)
            ]);

        var module = CreateModule([bound]);

        var without = await ScanAsync(module, allowInUse: false);
        Assert.DoesNotContain(without, f => f.IsActionable);

        var with = await ScanAsync(module, allowInUse: true);
        Assert.DoesNotContain(with, f => f.IsActionable);
        Assert.DoesNotContain(with, f => f.Id.Contains("bound", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Blocker_display_name_leads_with_hardware_or_driver_file()
    {
        var package = new OemDriverPackage(
            "oem55.inf",
            "lgjoyhid.inf",
            "Logitech",
            MediaClass,
            "fp-55",
            ["USB\\VID_046D&PID_C21F\\1"],
            ConnectedDeviceCount: 0,
            DisconnectedDeviceCount: 1,
            IsBootCritical: false,
            ClassName: "HIDClass",
            DriverVersion: "8.85.75.0",
            DriverDate: new DateOnly(2016, 6, 13),
            AssociatedDevices:
            [
                new OemDriverAssociatedDevice(
                    "USB\\VID_046D&PID_C21F\\1",
                    "Logitech Gaming Software",
                    "Logitech HID device",
                    IsPresent: false)
            ],
            ReferencedImageFileNames: ["LGJoyXICore.sys"]);

        var signal = new CodeIntegritySignal(
            3089,
            DateTimeOffset.UtcNow,
            "LGJoyXICore.sys",
            null,
            @"C:\Windows\System32\drivers\LGJoyXICore.sys blocked");

        var module = CreateModule([package], [signal]);
        var findings = await ScanAsync(module, allowInUse: true, includeDebug: false);
        var blocker = Assert.Single(findings, f => f.Id.Contains("blocker", StringComparison.Ordinal));

        Assert.StartsWith("Logitech Gaming Software", blocker.DisplayName, StringComparison.Ordinal);
        Assert.Contains("LGJoyXICore.sys", blocker.DisplayName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hardware:", blocker.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Logitech Gaming Software", blocker.Details, StringComparison.Ordinal);
        Assert.False(blocker.Details.StartsWith("What:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Critical_class_never_actionable()
    {
        var module = CreateModule(
            packages:
            [
                new OemDriverPackage(
                    "oem12.inf",
                    "disk.inf",
                    "DiskCo",
                    CriticalDriverClassGuids.DiskDrive,
                    "fp-disk",
                    [],
                    0,
                    0,
                    IsBootCritical: true)
            ]);

        var findings = await ScanAsync(module);
        Assert.DoesNotContain(findings, f => f.IsActionable);
    }

    [Fact]
    public async Task In_use_blocker_actionable_only_with_opt_in()
    {
        var package = new OemDriverPackage(
            "oem20.inf",
            "badfilter.inf",
            "FilterCo",
            MediaClass,
            "fp-20",
            ["ACPI\\BAD\\1"],
            ConnectedDeviceCount: 1,
            DisconnectedDeviceCount: 0,
            IsBootCritical: false);

        var signal = new CodeIntegritySignal(
            3089,
            DateTimeOffset.UtcNow,
            "badfilter.sys",
            null,
            "Code Integrity found oem20.inf incompatible with HVCI for badfilter.sys");

        var module = CreateModule([package], [signal]);

        var without = await ScanAsync(module, allowInUse: false);
        var blocker = Assert.Single(without, f => f.Id.Contains("blocker", StringComparison.Ordinal));
        Assert.False(blocker.IsActionable);
        Assert.Contains("Allow remove in-use blockers", blocker.Details, StringComparison.OrdinalIgnoreCase);

        var with = await ScanAsync(module, allowInUse: true);
        var actionable = Assert.Single(with, f => f.Id.Contains("blocker", StringComparison.Ordinal));
        Assert.True(actionable.IsActionable);
        Assert.Contains("best-effort", actionable.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("true", actionable.Metadata![DriverPackagePayloadKeys.BestEffortRestore]);
    }

    [Fact]
    public async Task Unmatched_ci_signal_is_informational()
    {
        var module = CreateModule(
            packages: [Orphan("oem30.inf", "other.inf", "Other")],
            signals:
            [
                new CodeIntegritySignal(3089, DateTimeOffset.UtcNow, "mystery.sys", null, "mystery.sys blocked")
            ]);

        var findings = await ScanAsync(module);
        var unmatched = Assert.Single(findings, f => f.Id.Contains("ci-unmatched", StringComparison.Ordinal));
        Assert.False(unmatched.IsActionable);
    }

    [Fact]
    public async Task Failed_enumeration_yields_no_actionable_findings()
    {
        var module = CreateModule(enumerationFailed: true);
        var findings = await ScanAsync(module);
        Assert.DoesNotContain(findings, f => f.IsActionable);
        Assert.Contains(findings, f => f.Details.Contains("Authoritative driver enumeration failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_rejects_local_driver_operations()
    {
        var module = CreateModule([]);
        var action = new OptimizationAction(
            "a1",
            CoreIsolationDriversModule.ModuleId,
            "f1",
            PrivilegedOperationCodes.DeleteDriverPackage,
            null,
            null,
            RequiredPrivilege.Elevated,
            new Dictionary<string, string>
            {
                [DriverPackagePayloadKeys.PublishedName] = "oem1.inf",
                [DriverPackagePayloadKeys.ClassGuid] = MediaClass.ToString("D"),
                [DriverPackagePayloadKeys.PackageFingerprint] = "fp",
                [DriverPackagePayloadKeys.RemovalMode] = DriverPackagePayloadKeys.RemovalModeOrphan,
                [DriverPackagePayloadKeys.AllowInUse] = "false",
                [DriverPackagePayloadKeys.IsBootCritical] = "false"
            });

        var result = await module.ApplyAsync(action, CancellationToken.None);
        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.Contains("elevated", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<ScanFinding>> ScanAsync(
        CoreIsolationDriversModule module,
        bool allowInUse = false,
        bool includeDebug = true,
        bool includeOrphans = false)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["IncludeDebugDetails"] = includeDebug,
            ["AllowRemoveInUseBlockers"] = allowInUse,
            ["IncludeOrphanOemPackages"] = includeOrphans
        };
        var findings = new List<ScanFinding>();
        await foreach (var finding in module.ScanAsync(parameters, null, CancellationToken.None))
        {
            findings.Add(finding);
        }

        return findings;
    }

    private static CoreIsolationDriversModule CreateModule(
        IReadOnlyList<OemDriverPackage>? packages = null,
        IReadOnlyList<CodeIntegritySignal>? signals = null,
        bool enumerationFailed = false)
        => new(
            new FakeDriverStore(packages ?? [], enumerationFailed),
            new FakeDeviceGuard(),
            new FakeCodeIntegrity(signals ?? []));

    private static OemDriverPackage Orphan(string published, string original, string provider)
        => new(published, original, provider, MediaClass, $"fp-{published}", [], 0, 0, false, 1234);

    private sealed class FakeDriverStore(IReadOnlyList<OemDriverPackage> packages, bool fail) : IDriverStore
    {
        public bool IsEnumerationAvailable => true;

        public DriverStoreEnumerationResult EnumerateOemPackages()
            => fail
                ? DriverStoreEnumerationResult.Failed("simulated failure")
                : DriverStoreEnumerationResult.Succeeded(packages);

        public OemDriverPackage? FindPackage(string publishedName)
            => packages.FirstOrDefault(p => p.PublishedName.Equals(publishedName, StringComparison.OrdinalIgnoreCase));

        public DriverPackageMutationResult ExportPackage(string publishedName, string destinationDirectory)
            => DriverPackageMutationResult.Ok("exported");

        public DriverPackageMutationResult DeletePackage(string publishedName, bool uninstallFromDevices)
            => DriverPackageMutationResult.Ok("deleted");

        public DriverPackageMutationResult AddPackage(string infPath)
            => DriverPackageMutationResult.Ok("added");
    }

    private sealed class FakeDeviceGuard : IDeviceGuardStatus
    {
        public DeviceGuardSnapshot GetSnapshot()
            => new(
                DeviceGuardFeatureState.Configured,
                DeviceGuardFeatureState.Unavailable,
                DeviceGuardFeatureState.Unknown,
                DeviceGuardFeatureState.Unknown,
                "test status");
    }

    private sealed class FakeCodeIntegrity(IReadOnlyList<CodeIntegritySignal> signals) : ICodeIntegrityInspector
    {
        public CodeIntegrityInspectionResult ReadObservedSignals(TimeSpan lookback, CancellationToken cancellationToken = default)
            => CodeIntegrityInspectionResult.Available(signals);
    }
}
