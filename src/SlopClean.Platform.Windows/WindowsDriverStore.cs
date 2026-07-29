using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
using static SlopClean.Platform.Windows.SetupApiNative;

namespace SlopClean.Platform.Windows;

public sealed partial class WindowsDriverStore : IDriverStore
{
    private static readonly Guid[] BootCriticalClasses =
    [
        CriticalDriverClassGuids.Computer,
        CriticalDriverClassGuids.DiskDrive,
        CriticalDriverClassGuids.Hdc,
        CriticalDriverClassGuids.ScsiAdapter,
        CriticalDriverClassGuids.Volume,
        CriticalDriverClassGuids.VolumeSnapshot,
        CriticalDriverClassGuids.Processor
    ];

    [GeneratedRegex(@"^oem\d+\.inf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OemInfNameRegex();

    public bool IsEnumerationAvailable
    {
        get
        {
            try
            {
                var infDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
                return Directory.Exists(infDir);
            }
            catch
            {
                return false;
            }
        }
    }

    public DriverStoreEnumerationResult EnumerateOemPackages()
    {
        try
        {
            if (!IsEnumerationAvailable)
            {
                return DriverStoreEnumerationResult.Failed("Windows INF directory is not available.");
            }

            var publishedToOriginal = TryReadPublishedToOriginalNames();
            var originalToPublished = publishedToOriginal
                .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

            var associations = BuildInfDeviceAssociations(originalToPublished);
            if (associations is null)
            {
                return DriverStoreEnumerationResult.Failed("SetupAPI device enumeration failed.");
            }

            var packages = new List<OemDriverPackage>();
            var infDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
            foreach (var infPath in Directory.EnumerateFiles(infDir, "oem*.inf"))
            {
                var name = Path.GetFileName(infPath);
                if (!OemInfNameRegex().IsMatch(name))
                {
                    continue;
                }

                if (!InfOemParser.TryRead(infPath, out var parsed))
                {
                    continue;
                }

                associations.TryGetValue(name, out var devices);
                devices ??= [];
                var connected = devices.Count(d => d.IsPresent);
                var disconnected = devices.Count - connected;
                var associated = devices
                    .GroupBy(d => d.InstanceId, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Select(d => new OemDriverAssociatedDevice(
                        d.InstanceId,
                        d.FriendlyName,
                        d.Description,
                        d.IsPresent))
                    .ToArray();
                var instanceIds = associated.Select(d => d.InstanceId).ToArray();
                var bootCritical = BootCriticalClasses.Contains(parsed.ClassGuid);
                var className = parsed.ClassName
                    ?? devices.Select(d => d.ClassName).FirstOrDefault(static c => !string.IsNullOrWhiteSpace(c));
                var originalName = publishedToOriginal.TryGetValue(name, out var fromPnP)
                    ? fromPnP
                    : parsed.OriginalName;

                packages.Add(new OemDriverPackage(
                    PublishedName: parsed.PublishedName,
                    OriginalName: originalName,
                    Provider: parsed.Provider,
                    ClassGuid: parsed.ClassGuid,
                    PackageFingerprint: parsed.PackageFingerprint,
                    AssociatedDeviceInstanceIds: instanceIds,
                    ConnectedDeviceCount: connected,
                    DisconnectedDeviceCount: disconnected,
                    IsBootCritical: bootCritical,
                    ApproximateSizeBytes: parsed.ApproximateSizeBytes,
                    ClassName: className,
                    DriverVersion: parsed.DriverVersion,
                    DriverDate: parsed.DriverDate,
                    InfLastWriteUtc: parsed.InfLastWriteUtc,
                    AssociatedDevices: associated,
                    ReferencedImageFileNames: parsed.ReferencedImageFileNames));
            }

            return DriverStoreEnumerationResult.Succeeded(packages);
        }
        catch (Exception ex)
        {
            return DriverStoreEnumerationResult.Failed(ex.Message);
        }
    }

    public OemDriverPackage? FindPackage(string publishedName)
    {
        var result = EnumerateOemPackages();
        if (!result.IsAuthoritative)
        {
            return null;
        }

        return result.Packages.FirstOrDefault(p =>
            p.PublishedName.Equals(publishedName, StringComparison.OrdinalIgnoreCase));
    }

    public DriverPackageMutationResult ExportPackage(string publishedName, string destinationDirectory)
    {
        if (!DriverPackageEligibility.IsOemPublishedName(publishedName))
        {
            return DriverPackageMutationResult.Fail("Invalid OEM published name.");
        }

        Directory.CreateDirectory(destinationDirectory);
        return PnPUtilRunner.Run(["/export-driver", publishedName, destinationDirectory]);
    }

    public DriverPackageMutationResult DeletePackage(string publishedName, bool uninstallFromDevices, bool force = false)
    {
        if (!DriverPackageEligibility.IsOemPublishedName(publishedName))
        {
            return DriverPackageMutationResult.Fail("Invalid OEM published name.");
        }

        var args = new List<string> { "/delete-driver", publishedName };
        if (uninstallFromDevices)
        {
            args.Add("/uninstall");
        }

        if (force)
        {
            args.Add("/force");
        }

        return PnPUtilRunner.Run(args);
    }

    public DriverPackageMutationResult AddPackage(string infPath)
    {
        if (string.IsNullOrWhiteSpace(infPath) || !File.Exists(infPath))
        {
            return DriverPackageMutationResult.Fail("INF path for restore is missing.");
        }

        return PnPUtilRunner.Run(["/add-driver", infPath]);
    }

    private static IReadOnlyDictionary<string, string> TryReadPublishedToOriginalNames()
    {
        try
        {
            var result = PnPUtilRunner.Run(["/enum-drivers"], TimeSpan.FromSeconds(45));
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Message))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return PnPUtilEnumDriversParser.ParsePublishedToOriginal(result.Message);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, List<DeviceAssoc>>? BuildInfDeviceAssociations(
        IReadOnlyDictionary<string, string> originalToPublished)
    {
        var presentIds = CollectPresentInstanceIds();
        if (presentIds is null)
        {
            return null;
        }

        var map = new Dictionary<string, List<DeviceAssoc>>(StringComparer.OrdinalIgnoreCase);
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DigcfAllClasses);
        if (set == IntPtr.Zero || set == new IntPtr(-1))
        {
            return null;
        }

        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                var instanceId = GetDevicePropertyString(set, ref data, DeviceInstanceId) ?? $"DEVINST-{data.DevInst}";
                var deviceInfPath = GetDevicePropertyString(set, ref data, DeviceDriverInfPath);
                var driverNodeInfPath = ReadDriverNodeInfPath(
                    GetDeviceRegistryPropertyString(set, ref data, SpdrpDriver));
                var published = DriverPackageNameResolver.ResolvePublishedOemName(
                    driverNodeInfPath,
                    deviceInfPath,
                    originalToPublished);
                if (published is null)
                {
                    continue;
                }

                var presentText = GetDevicePropertyString(set, ref data, DeviceIsPresent);
                bool isPresent;
                if (presentText is not null)
                {
                    isPresent = presentText.Equals("true", StringComparison.OrdinalIgnoreCase) || presentText == "1";
                }
                else
                {
                    // Fail closed: unknown presence counts as present unless we positively saw it only in the all-classes set.
                    isPresent = presentIds.Contains(instanceId);
                }

                if (!map.TryGetValue(published, out var list))
                {
                    list = [];
                    map[published] = list;
                }

                list.Add(new DeviceAssoc(
                    instanceId,
                    isPresent,
                    GetDeviceRegistryPropertyString(set, ref data, SpdrpFriendlyName),
                    GetDeviceRegistryPropertyString(set, ref data, SpdrpDeviceDesc),
                    GetDeviceRegistryPropertyString(set, ref data, SpdrpClass)));
            }

            return map;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string? ReadDriverNodeInfPath(string? driverNodeRelativePath)
    {
        if (string.IsNullOrWhiteSpace(driverNodeRelativePath))
        {
            return null;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\" + driverNodeRelativePath.TrimStart('\\'));
            return key?.GetValue("InfPath") as string;
        }
        catch
        {
            return null;
        }
    }

    private static HashSet<string>? CollectPresentInstanceIds()
    {
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DigcfAllClasses | DigcfPresent);
        if (set == IntPtr.Zero || set == new IntPtr(-1))
        {
            return null;
        }

        try
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                var instanceId = GetDevicePropertyString(set, ref data, DeviceInstanceId);
                if (!string.IsNullOrWhiteSpace(instanceId))
                {
                    ids.Add(instanceId);
                }
            }

            return ids;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string? GetDevicePropertyString(IntPtr set, ref SP_DEVINFO_DATA data, DEVPROPKEY key)
    {
        var keyCopy = key;
        if (!SetupDiGetDevicePropertyW(set, ref data, ref keyCopy, out var type, null, 0, out var required, 0)
            && required == 0)
        {
            return null;
        }

        var buffer = new byte[required];
        if (!SetupDiGetDevicePropertyW(set, ref data, ref keyCopy, out type, buffer, required, out _, 0))
        {
            return null;
        }

        // DEVPROP_TYPE_STRING = 18, BOOLEAN = 17
        if (type == 17 && buffer.Length > 0)
        {
            return buffer[0] != 0 ? "true" : "false";
        }

        return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    private static string? GetDeviceRegistryPropertyString(IntPtr set, ref SP_DEVINFO_DATA data, uint property)
    {
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out _, null, 0, out var required)
            && required == 0)
        {
            return null;
        }

        var buffer = new byte[Math.Max(required, 2)];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out _, buffer, (uint)buffer.Length, out _))
        {
            return null;
        }

        var text = Encoding.Unicode.GetString(buffer).TrimEnd('\0', '\u0000');
        // MULTI_SZ hardware IDs etc. — take first segment.
        var nullIndex = text.IndexOf('\0');
        if (nullIndex >= 0)
        {
            text = text[..nullIndex];
        }

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private sealed record DeviceAssoc(
        string InstanceId,
        bool IsPresent,
        string? FriendlyName,
        string? Description,
        string? ClassName);
}
