using System.Text.RegularExpressions;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;

namespace SlopClean.Core.Safety;

public static partial class DriverPackageEligibility
{
    [GeneratedRegex(@"^oem\d+\.inf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OemInfRegex();

    public static bool IsOemPublishedName(string? publishedName)
        => !string.IsNullOrWhiteSpace(publishedName) && OemInfRegex().IsMatch(publishedName.Trim());

    public static SafetyValidationResult ValidatePayload(OptimizationAction action)
    {
        if (action.OperationCode is not PrivilegedOperationCodes.DeleteDriverPackage
            and not PrivilegedOperationCodes.RestoreDriverPackage)
        {
            return SafetyValidationResult.Deny("Not a driver-package operation.");
        }

        if (action.RequiredPrivilege != RequiredPrivilege.Elevated)
        {
            return SafetyValidationResult.Deny("Driver-package operations require elevation.");
        }

        if (action.Payload is null)
        {
            return SafetyValidationResult.Deny("Driver-package action payload is missing.");
        }

        if (!action.Payload.TryGetValue(DriverPackagePayloadKeys.PublishedName, out var published)
            || !IsOemPublishedName(published))
        {
            return SafetyValidationResult.Deny("Driver published name must match oemN.inf.");
        }

        if (!action.Payload.TryGetValue(DriverPackagePayloadKeys.ClassGuid, out var classGuidText)
            || !CriticalDriverClassGuids.TryParse(classGuidText, out var classGuid))
        {
            return SafetyValidationResult.Deny("Driver classGuid payload is missing or invalid.");
        }

        if (CriticalDriverClassGuids.IsDenied(classGuid))
        {
            return SafetyValidationResult.Deny("Driver class is protected and cannot be removed.");
        }

        if (!action.Payload.TryGetValue(DriverPackagePayloadKeys.PackageFingerprint, out var fingerprint)
            || string.IsNullOrWhiteSpace(fingerprint))
        {
            return SafetyValidationResult.Deny("Driver packageFingerprint payload is required.");
        }

        if (action.Payload.TryGetValue(DriverPackagePayloadKeys.IsBootCritical, out var boot)
            && bool.TryParse(boot, out var isBootCritical)
            && isBootCritical)
        {
            return SafetyValidationResult.Deny("Boot-critical driver packages cannot be removed.");
        }

        if (action.OperationCode == PrivilegedOperationCodes.RestoreDriverPackage)
        {
            if (!action.Payload.TryGetValue(DriverPackagePayloadKeys.RestorePayloadDirectory, out var dir)
                || string.IsNullOrWhiteSpace(dir))
            {
                return SafetyValidationResult.Deny("Restore payload directory is required.");
            }

            return SafetyValidationResult.Allow();
        }

        if (!action.Payload.TryGetValue(DriverPackagePayloadKeys.RemovalMode, out var mode)
            || string.IsNullOrWhiteSpace(mode))
        {
            return SafetyValidationResult.Deny("Driver removalMode payload is required.");
        }

        var normalizedMode = mode.Trim();
        if (!normalizedMode.Equals(DriverPackagePayloadKeys.RemovalModeOrphan, StringComparison.OrdinalIgnoreCase)
            && !normalizedMode.Equals(DriverPackagePayloadKeys.RemovalModeInUse, StringComparison.OrdinalIgnoreCase))
        {
            return SafetyValidationResult.Deny("Unknown driver removalMode.");
        }

        if (normalizedMode.Equals(DriverPackagePayloadKeys.RemovalModeInUse, StringComparison.OrdinalIgnoreCase))
        {
            if (!action.Payload.TryGetValue(DriverPackagePayloadKeys.AllowInUse, out var allow)
                || !bool.TryParse(allow, out var allowInUse)
                || !allowInUse)
            {
                return SafetyValidationResult.Deny("In-use driver removal requires allowInUse=true.");
            }

            if (action.Payload.TryGetValue(DriverPackagePayloadKeys.IsMicrosoftProvider, out var ms)
                && bool.TryParse(ms, out var isMicrosoft)
                && isMicrosoft)
            {
                return SafetyValidationResult.Deny("Microsoft-provider in-use driver packages cannot be removed.");
            }

            var provider = action.Payload.GetValueOrDefault(DriverPackagePayloadKeys.Provider);
            if (!string.IsNullOrWhiteSpace(provider)
                && provider.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            {
                return SafetyValidationResult.Deny("Microsoft-provider in-use driver packages cannot be removed.");
            }
        }

        return SafetyValidationResult.Allow();
    }
}
