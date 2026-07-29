using System.Text;
using SlopClean.Core.Models;

namespace SlopClean.Modules.CoreIsolationDrivers;

internal static class CoreIsolationDriverIdentityFormatter
{
    public static void AppendHumanReadableSummary(
        StringBuilder details,
        OemDriverPackage package,
        string? imageFileName)
    {
        details.Append("Hardware: ").Append(FormatHardware(package)).Append('.');
        details.Append(" Driver file: ").Append(FormatDriverFile(package, imageFileName)).Append('.');
        details.Append(" Provider: ").Append(package.Provider).Append('.');
        details.Append(" Version: ").Append(FormatVersion(package)).Append('.');
        details.Append(" Age: ").Append(FormatAge(package, DateTimeOffset.UtcNow)).Append('.');
        details.Append(" In use: ").Append(FormatInUse(package)).Append('.');
    }

    public static void AppendDriverIdentity(StringBuilder details, OemDriverPackage package)
        => AppendHumanReadableSummary(details, package, imageFileName: null);

    public static string FormatBlockerDisplayName(OemDriverPackage package, string? imageFileName)
    {
        var hardware = package.Devices
            .Select(static d => d.DisplayName)
            .FirstOrDefault(static n => !string.IsNullOrWhiteSpace(n));
        var driverFile = FirstImage(package, imageFileName);

        if (!string.IsNullOrWhiteSpace(hardware))
        {
            return string.IsNullOrWhiteSpace(driverFile)
                ? hardware
                : $"{hardware} — {driverFile}";
        }

        if (!string.IsNullOrWhiteSpace(driverFile))
        {
            return $"{package.Provider} — {driverFile}";
        }

        return $"{package.Provider} — {package.OriginalName}";
    }

    public static string FormatPackageDisplayName(string prefix, OemDriverPackage package)
        => $"{prefix}: {FormatBlockerDisplayName(package, imageFileName: null)}";

    internal static string FormatHardware(OemDriverPackage package)
    {
        if (!string.IsNullOrWhiteSpace(package.ClassName))
        {
            var devices = package.Devices
                .Select(static d => d.DisplayName)
                .Where(static n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToArray();
            if (devices.Length > 0)
            {
                var more = package.Devices.Count > devices.Length
                    ? $" (+{package.Devices.Count - devices.Length} more)"
                    : string.Empty;
                return $"{string.Join("; ", devices)}{more} [{package.ClassName}]";
            }

            return package.TotalDeviceCount == 0
                ? $"{package.ClassName} (no associated devices)"
                : package.ClassName;
        }

        var names = package.Devices
            .Select(static d => d.DisplayName)
            .Where(static n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
        if (names.Length > 0)
        {
            var more = package.Devices.Count > names.Length
                ? $" (+{package.Devices.Count - names.Length} more)"
                : string.Empty;
            return string.Join("; ", names) + more;
        }

        return package.TotalDeviceCount == 0
            ? "unknown (no associated devices)"
            : "unknown device name";
    }

    internal static string FormatInUse(OemDriverPackage package)
    {
        if (package.IsCurrentlyInUse)
        {
            return package.ConnectedDeviceCount == 1
                ? "yes (1 present device)"
                : $"yes ({package.ConnectedDeviceCount} present devices)";
        }

        if (package.DisconnectedDeviceCount > 0)
        {
            return package.DisconnectedDeviceCount == 1
                ? "no (only 1 disconnected/phantom device)"
                : $"no (only {package.DisconnectedDeviceCount} disconnected/phantom devices)";
        }

        return "no (orphaned — no devices)";
    }

    internal static string FormatVersion(OemDriverPackage package)
    {
        if (!string.IsNullOrWhiteSpace(package.DriverVersion) && package.DriverDate is { } date)
        {
            return $"{package.DriverVersion} (driver date {date:yyyy-MM-dd})";
        }

        if (!string.IsNullOrWhiteSpace(package.DriverVersion))
        {
            return package.DriverVersion;
        }

        if (package.DriverDate is { } onlyDate)
        {
            return $"unknown (driver date {onlyDate:yyyy-MM-dd})";
        }

        return "unknown";
    }

    internal static string FormatAge(OemDriverPackage package, DateTimeOffset now)
    {
        var anchor = package.DriverDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) is { } driverDate
            ? new DateTimeOffset(driverDate, TimeSpan.Zero)
            : package.InfLastWriteUtc;

        if (anchor is null)
        {
            return "unknown";
        }

        var days = Math.Max(0, (int)(now - anchor.Value).TotalDays);
        if (days < 30)
        {
            return days <= 1
                ? $"~{days} day (since {anchor.Value:yyyy-MM-dd})"
                : $"~{days} days (since {anchor.Value:yyyy-MM-dd})";
        }

        var years = days / 365;
        var months = (days % 365) / 30;
        if (years >= 1)
        {
            return months > 0
                ? $"~{years}y {months}mo (since {anchor.Value:yyyy-MM-dd})"
                : $"~{years}y (since {anchor.Value:yyyy-MM-dd})";
        }

        return $"~{months} months (since {anchor.Value:yyyy-MM-dd})";
    }

    private static string FormatDriverFile(OemDriverPackage package, string? imageFileName)
    {
        var image = FirstImage(package, imageFileName);
        if (!string.IsNullOrWhiteSpace(image))
        {
            return $"{image} · package {package.PublishedName} ({package.OriginalName})";
        }

        return $"{package.PublishedName} ({package.OriginalName})";
    }

    private static string? FirstImage(OemDriverPackage package, string? imageFileName)
    {
        if (!string.IsNullOrWhiteSpace(imageFileName))
        {
            return imageFileName;
        }

        return package.ImageFileNames.FirstOrDefault();
    }
}
