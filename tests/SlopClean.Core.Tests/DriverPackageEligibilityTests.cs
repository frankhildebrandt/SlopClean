using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
using SlopClean.Core.Tests.Fakes;

namespace SlopClean.Core.Tests;

public class DriverPackageEligibilityTests
{
    private readonly SafetyPolicy _policy = new(new FakeFileSystem());

    [Fact]
    public void Allows_orphan_oem_delete_with_complete_payload()
    {
        var result = _policy.ValidateAction(CreateDeleteAction(
            publishedName: "oem42.inf",
            classGuid: "4d36e96c-e325-11ce-bfc1-08002be10318", // Media
            removalMode: DriverPackagePayloadKeys.RemovalModeOrphan));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Denies_non_oem_published_name()
    {
        var result = _policy.ValidateAction(CreateDeleteAction(
            publishedName: "netkvm.inf",
            classGuid: "4d36e96c-e325-11ce-bfc1-08002be10318",
            removalMode: DriverPackagePayloadKeys.RemovalModeOrphan));

        Assert.False(result.IsAllowed);
        Assert.Contains("oemN.inf", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Denies_critical_class_guid()
    {
        var result = _policy.ValidateAction(CreateDeleteAction(
            publishedName: "oem1.inf",
            classGuid: CriticalDriverClassGuids.DiskDrive.ToString(),
            removalMode: DriverPackagePayloadKeys.RemovalModeOrphan));

        Assert.False(result.IsAllowed);
        Assert.Contains("protected", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Denies_in_use_without_allowInUse()
    {
        var result = _policy.ValidateAction(CreateDeleteAction(
            publishedName: "oem7.inf",
            classGuid: "4d36e96c-e325-11ce-bfc1-08002be10318",
            removalMode: DriverPackagePayloadKeys.RemovalModeInUse,
            allowInUse: false));

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void Allows_in_use_with_allowInUse()
    {
        var result = _policy.ValidateAction(CreateDeleteAction(
            publishedName: "oem7.inf",
            classGuid: "4d36e96c-e325-11ce-bfc1-08002be10318",
            removalMode: DriverPackagePayloadKeys.RemovalModeInUse,
            allowInUse: true));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Denies_microsoft_provider_in_use()
    {
        var result = _policy.ValidateAction(CreateDeleteAction(
            publishedName: "oem7.inf",
            classGuid: "4d36e96c-e325-11ce-bfc1-08002be10318",
            removalMode: DriverPackagePayloadKeys.RemovalModeInUse,
            allowInUse: true,
            provider: "Microsoft Corporation",
            isMicrosoft: true));

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void Denies_boot_critical()
    {
        var action = CreateDeleteAction(
            publishedName: "oem3.inf",
            classGuid: "4d36e96c-e325-11ce-bfc1-08002be10318",
            removalMode: DriverPackagePayloadKeys.RemovalModeOrphan);
        var payload = new Dictionary<string, string>(action.Payload!)
        {
            [DriverPackagePayloadKeys.IsBootCritical] = "true"
        };
        action = action with { Payload = payload };

        Assert.False(_policy.ValidateAction(action).IsAllowed);
    }

    [Fact]
    public void Denies_missing_fingerprint()
    {
        var action = CreateDeleteAction(
            publishedName: "oem3.inf",
            classGuid: "4d36e96c-e325-11ce-bfc1-08002be10318",
            removalMode: DriverPackagePayloadKeys.RemovalModeOrphan);
        var payload = new Dictionary<string, string>(action.Payload!);
        payload.Remove(DriverPackagePayloadKeys.PackageFingerprint);
        action = action with { Payload = payload };

        Assert.False(_policy.ValidateAction(action).IsAllowed);
    }

    [Fact]
    public void Denies_without_elevated_privilege()
    {
        var action = CreateDeleteAction(
            publishedName: "oem3.inf",
            classGuid: "4d36e96c-e325-11ce-bfc1-08002be10318",
            removalMode: DriverPackagePayloadKeys.RemovalModeOrphan) with
        {
            RequiredPrivilege = RequiredPrivilege.None
        };

        Assert.False(_policy.ValidateAction(action).IsAllowed);
    }

    private static OptimizationAction CreateDeleteAction(
        string publishedName,
        string classGuid,
        string removalMode,
        bool allowInUse = false,
        string provider = "Contoso",
        bool isMicrosoft = false)
        => new(
            Id: "1",
            ModuleId: "core-isolation-drivers",
            FindingId: "f1",
            OperationCode: PrivilegedOperationCodes.DeleteDriverPackage,
            Path: null,
            AllowedRoot: null,
            RequiredPrivilege: RequiredPrivilege.Elevated,
            Payload: new Dictionary<string, string>
            {
                [DriverPackagePayloadKeys.PublishedName] = publishedName,
                [DriverPackagePayloadKeys.ClassGuid] = classGuid,
                [DriverPackagePayloadKeys.PackageFingerprint] = "fp-1",
                [DriverPackagePayloadKeys.RemovalMode] = removalMode,
                [DriverPackagePayloadKeys.AllowInUse] = allowInUse ? "true" : "false",
                [DriverPackagePayloadKeys.Provider] = provider,
                [DriverPackagePayloadKeys.IsMicrosoftProvider] = isMicrosoft ? "true" : "false",
                [DriverPackagePayloadKeys.IsBootCritical] = "false",
                [DriverPackagePayloadKeys.OriginalName] = "contoso.inf"
            });
}
