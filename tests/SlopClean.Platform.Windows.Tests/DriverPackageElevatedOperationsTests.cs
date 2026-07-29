using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
using SlopClean.Platform.Windows;

namespace SlopClean.Platform.Windows.Tests;

public class DriverPackageElevatedOperationsTests
{
    private static readonly Guid MediaClass = new("4d36e96c-e325-11ce-bfc1-08002be10318");

    [Fact]
    public void Delete_orphan_with_allow_in_use_uses_uninstall()
    {
        var store = new RecordingDriverStore(CreatePackage("oem36.inf", connected: 0, disconnected: 0));
        var dir = CreateTempDir();
        try
        {
            var action = CreateDeleteAction(
                "oem36.inf",
                removalMode: DriverPackagePayloadKeys.RemovalModeOrphan,
                allowInUse: true,
                packageDir: dir);

            var result = DriverPackageElevatedOperations.Execute(action, store);

            Assert.True(result.IsSuccessful);
            Assert.True(store.LastUninstallFromDevices);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Delete_orphan_without_allow_in_use_does_not_uninstall()
    {
        var store = new RecordingDriverStore(CreatePackage("oem36.inf", connected: 0, disconnected: 0));
        var dir = CreateTempDir();
        try
        {
            var action = CreateDeleteAction(
                "oem36.inf",
                removalMode: DriverPackagePayloadKeys.RemovalModeOrphan,
                allowInUse: false,
                packageDir: dir);

            var result = DriverPackageElevatedOperations.Execute(action, store);

            Assert.True(result.IsSuccessful);
            Assert.False(store.LastUninstallFromDevices);
            Assert.False(store.LastForce);
            Assert.Equal(1, store.DeleteCallCount);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Delete_with_allow_in_use_retries_with_force_when_uninstall_fails()
    {
        var store = new RecordingDriverStore(
            CreatePackage("oem55.inf", connected: 0, disconnected: 0),
            failUntilForce: true);
        var dir = CreateTempDir();
        try
        {
            var action = CreateDeleteAction(
                "oem55.inf",
                removalMode: DriverPackagePayloadKeys.RemovalModeInUse,
                allowInUse: true,
                packageDir: dir);

            var result = DriverPackageElevatedOperations.Execute(action, store);

            Assert.True(result.IsSuccessful);
            Assert.Equal(2, store.DeleteCallCount);
            Assert.True(store.LastUninstallFromDevices);
            Assert.True(store.LastForce);
            Assert.Contains("/force", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SlopCleanDriverElevatedTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static OemDriverPackage CreatePackage(string published, int connected, int disconnected)
        => new(
            published,
            "contoso.inf",
            "Contoso",
            MediaClass,
            $"fp-{published}",
            connected + disconnected == 0 ? [] : ["USB\\VID_1&PID_2\\1"],
            connected,
            disconnected,
            IsBootCritical: false,
            ApproximateSizeBytes: 10);

    private static OptimizationAction CreateDeleteAction(
        string published,
        string removalMode,
        bool allowInUse,
        string packageDir)
        => new(
            "a1",
            "core-isolation-drivers",
            "f1",
            PrivilegedOperationCodes.DeleteDriverPackage,
            published,
            null,
            RequiredPrivilege.Elevated,
            new Dictionary<string, string>
            {
                [DriverPackagePayloadKeys.PublishedName] = published,
                [DriverPackagePayloadKeys.OriginalName] = "contoso.inf",
                [DriverPackagePayloadKeys.Provider] = "Contoso",
                [DriverPackagePayloadKeys.ClassGuid] = MediaClass.ToString("D"),
                [DriverPackagePayloadKeys.PackageFingerprint] = $"fp-{published}",
                [DriverPackagePayloadKeys.RemovalMode] = removalMode,
                [DriverPackagePayloadKeys.AllowInUse] = allowInUse ? "true" : "false",
                [DriverPackagePayloadKeys.IsBootCritical] = "false",
                [DriverPackagePayloadKeys.IsMicrosoftProvider] = "false",
                [DriverPackagePayloadKeys.BestEffortRestore] = allowInUse ? "true" : "false",
                [DriverPackagePayloadKeys.RestorePayloadDirectory] = packageDir
            });

    private sealed class RecordingDriverStore(OemDriverPackage package, bool failUntilForce = false) : IDriverStore
    {
        public bool? LastUninstallFromDevices { get; private set; }
        public bool? LastForce { get; private set; }
        public int DeleteCallCount { get; private set; }

        public bool IsEnumerationAvailable => true;

        public DriverStoreEnumerationResult EnumerateOemPackages()
            => DriverStoreEnumerationResult.Succeeded([package]);

        public OemDriverPackage? FindPackage(string publishedName)
            => package.PublishedName.Equals(publishedName, StringComparison.OrdinalIgnoreCase) ? package : null;

        public DriverPackageMutationResult ExportPackage(string publishedName, string destinationDirectory)
            => DriverPackageMutationResult.Ok("exported");

        public DriverPackageMutationResult DeletePackage(string publishedName, bool uninstallFromDevices, bool force = false)
        {
            DeleteCallCount++;
            LastUninstallFromDevices = uninstallFromDevices;
            LastForce = force;
            if (failUntilForce && !force)
            {
                return DriverPackageMutationResult.Fail(
                    "Fehler beim Löschen des Treiberpakets: Ein oder mehrere, zur Zeit installierte Geräte verwenden die angegebene INF.");
            }

            return DriverPackageMutationResult.Ok(force ? "forced" : uninstallFromDevices ? "uninstalled" : "deleted");
        }

        public DriverPackageMutationResult AddPackage(string infPath)
            => DriverPackageMutationResult.Ok("added");
    }
}
