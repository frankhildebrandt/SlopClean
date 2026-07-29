using Microsoft.Win32;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;

namespace SlopClean.Platform.Windows;

/// <summary>
/// Device Guard status from registry configuration keys.
/// Runtime "Running" is reported as Unknown without WMI (no System.Management dependency).
/// </summary>
public sealed class WindowsDeviceGuardStatus : IDeviceGuardStatus
{
    public DeviceGuardSnapshot GetSnapshot()
    {
        try
        {
            using var dg = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard");
            using var hvci = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");

            var vbsEnabled = ReadDword(dg, "EnableVirtualizationBasedSecurity") == 1;
            var miEnabled = ReadDword(hvci, "Enabled") == 1;
            var locked = ReadDword(dg, "Locked") == 1;

            var vbs = vbsEnabled ? DeviceGuardFeatureState.Configured : DeviceGuardFeatureState.Unavailable;
            var miConfigured = miEnabled ? DeviceGuardFeatureState.Configured : DeviceGuardFeatureState.Unavailable;
            var hardware = locked
                ? DeviceGuardFeatureState.Unavailable
                : DeviceGuardFeatureState.Unknown;

            var summary =
                $"VBS configured={vbsEnabled}; Memory Integrity configured={miEnabled}; Running state unknown without WMI; Locked={locked}.";

            return new DeviceGuardSnapshot(
                VirtualizationBasedSecurity: vbs,
                MemoryIntegrityConfigured: miConfigured,
                MemoryIntegrityRunning: DeviceGuardFeatureState.Unknown,
                StandardHardwareSecurity: hardware,
                Summary: summary);
        }
        catch (Exception ex)
        {
            return new DeviceGuardSnapshot(
                DeviceGuardFeatureState.Unknown,
                DeviceGuardFeatureState.Unknown,
                DeviceGuardFeatureState.Unknown,
                DeviceGuardFeatureState.Unknown,
                $"Device Guard status unavailable: {ex.Message}");
        }
    }

    private static int? ReadDword(RegistryKey? key, string name)
    {
        if (key?.GetValue(name) is int i)
        {
            return i;
        }

        if (key?.GetValue(name) is long l)
        {
            return (int)l;
        }

        return null;
    }
}
