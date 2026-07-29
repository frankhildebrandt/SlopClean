namespace SlopClean.Core.Models;

public enum DeviceGuardFeatureState
{
    Unknown = 0,
    Configured = 1,
    Running = 2,
    Supported = 3,
    Unavailable = 4
}

public sealed record DeviceGuardSnapshot(
    DeviceGuardFeatureState VirtualizationBasedSecurity,
    DeviceGuardFeatureState MemoryIntegrityConfigured,
    DeviceGuardFeatureState MemoryIntegrityRunning,
    DeviceGuardFeatureState StandardHardwareSecurity,
    string Summary);
