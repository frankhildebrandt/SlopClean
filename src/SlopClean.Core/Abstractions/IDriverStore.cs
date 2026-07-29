using SlopClean.Core.Models;

namespace SlopClean.Core.Abstractions;

public interface IDriverStore
{
    bool IsEnumerationAvailable { get; }

    DriverStoreEnumerationResult EnumerateOemPackages();

    OemDriverPackage? FindPackage(string publishedName);

    DriverPackageMutationResult ExportPackage(string publishedName, string destinationDirectory);

    DriverPackageMutationResult DeletePackage(string publishedName, bool uninstallFromDevices, bool force = false);

    DriverPackageMutationResult AddPackage(string infPath);
}

public sealed record DriverPackageMutationResult(
    bool Succeeded,
    bool RebootRequired,
    string Message,
    int ExitCode = 0)
{
    public static DriverPackageMutationResult Ok(string message, bool rebootRequired = false)
        => new(true, rebootRequired, message, rebootRequired ? 3010 : 0);

    public static DriverPackageMutationResult Fail(string message, int exitCode = -1)
        => new(false, false, message, exitCode);
}
