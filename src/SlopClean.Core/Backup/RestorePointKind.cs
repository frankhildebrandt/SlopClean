namespace SlopClean.Core.Backup;

public enum RestorePointKind
{
    File = 0,
    Directory = 1,
    RegistryExport = 2,
    RegistryValue = 3,
    StartupShortcut = 4,
    DriverPackage = 5
}
