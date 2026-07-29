using Microsoft.Extensions.DependencyInjection;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using FakeFileSystem = SlopClean.Modules.TestSupport.Fakes.FakeFileSystem;
using SlopClean.Modules.BrowserCleaner;
using SlopClean.Modules.CoreIsolationDrivers;
using SlopClean.Modules.DiskAnalyzer;
using SlopClean.Modules.RecycleBin;
using SlopClean.Modules.ServiceAdvisor;
using SlopClean.Modules.StartupManager;
using SlopClean.Modules.TempCleaner;
using SlopClean.Modules.UninstallCleanup;

namespace SlopClean.Modules.Tests;

public class ModuleRegistrationTests
{
    [Fact]
    public void AddSlopCleanModules_registers_exactly_eight_modules()
    {
        var services = new ServiceCollection();
        RegisterPlatformFakes(services);
        services.AddSlopCleanModules();

        using var provider = services.BuildServiceProvider();
        var modules = provider.GetServices<IModule>().ToList();

        Assert.Equal(8, modules.Count);
        Assert.Equal(8, modules.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.Contains(modules, m => m is TempCleanerModule && m.Id == TempCleanerModule.ModuleId);
        Assert.Contains(modules, m => m is RecycleBinModule && m.Id == RecycleBinModule.ModuleId);
        Assert.Contains(modules, m => m is BrowserCleanerModule && m.Id == BrowserCleanerModule.ModuleId);
        Assert.Contains(modules, m => m is StartupManagerModule && m.Id == StartupManagerModule.ModuleId);
        Assert.Contains(modules, m => m is DiskAnalyzerModule && m.Id == DiskAnalyzerModule.ModuleId);
        Assert.Contains(modules, m => m is UninstallCleanupModule && m.Id == UninstallCleanupModule.ModuleId);
        Assert.Contains(modules, m => m is ServiceAdvisorModule && m.Id == ServiceAdvisorModule.ModuleId);
        Assert.Contains(modules, m => m is CoreIsolationDriversModule && m.Id == CoreIsolationDriversModule.ModuleId);
    }

    [Fact]
    public void AddSlopCleanModules_each_module_ships_png_illustration()
    {
        var services = new ServiceCollection();
        RegisterPlatformFakes(services);
        services.AddSlopCleanModules();

        using var provider = services.BuildServiceProvider();
        var modules = provider.GetServices<IModule>().ToList();

        Assert.All(modules, module =>
        {
            var illustration = Assert.IsAssignableFrom<IModuleIllustration>(module);
            using var stream = illustration.OpenIllustration();
            Assert.True(stream.CanRead);
            Assert.True(stream.Length > 32);

            var header = new byte[8];
            Assert.Equal(8, stream.Read(header, 0, 8));
            Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], header);
        });
    }

    private static void RegisterPlatformFakes(IServiceCollection services)
    {
        services.AddSingleton<IFileSystem, FakeFileSystem>();
        services.AddSingleton<IRegistryStore, EmptyRegistryStore>();
        services.AddSingleton<IProcessInspector, EmptyProcessInspector>();
        services.AddSingleton<IRecycleBinService, EmptyRecycleBinService>();
        services.AddSingleton<IServiceManager, EmptyServiceManager>();
        services.AddSingleton<IDriverStore, EmptyDriverStore>();
        services.AddSingleton<IDeviceGuardStatus, EmptyDeviceGuardStatus>();
        services.AddSingleton<ICodeIntegrityInspector, EmptyCodeIntegrityInspector>();
        services.AddSingleton<IHvciCompatibilityInspector, EmptyHvciCompatibilityInspector>();
        services.AddSingleton<IPrivilegeBroker, EmptyPrivilegeBroker>();
    }

    private sealed class EmptyRegistryStore : IRegistryStore
    {
        public IReadOnlyList<RegistryValueInfo> GetValues(RegistryHiveKind hive, string subKey) => [];
        public IReadOnlyList<string> GetSubKeyNames(RegistryHiveKind hive, string subKey) => [];
        public string? GetStringValue(RegistryHiveKind hive, string subKey, string valueName) => null;
        public void DeleteValue(RegistryHiveKind hive, string subKey, string valueName) { }
        public void DeleteSubKeyTree(RegistryHiveKind hive, string subKey) { }
        public void SetStringValue(RegistryHiveKind hive, string subKey, string valueName, string value) { }
        public string ExportKey(RegistryHiveKind hive, string subKey, string destinationFile) => destinationFile;
    }

    private sealed class EmptyProcessInspector : IProcessInspector
    {
        public bool IsProcessRunning(string processName) => false;
        public IReadOnlyList<string> GetRunningProcessNames() => [];
    }

    private sealed class EmptyRecycleBinService : IRecycleBinService
    {
        public RecycleBinInfo Query() => new(0, 0);
        public void Empty() { }
    }

    private sealed class EmptyServiceManager : IServiceManager
    {
        public IReadOnlyList<WindowsServiceInfo> GetServices() => [];
        public WindowsServiceInfo? GetService(string serviceName) => null;
    }

    private sealed class EmptyDriverStore : IDriverStore
    {
        public bool IsEnumerationAvailable => true;
        public DriverStoreEnumerationResult EnumerateOemPackages() => DriverStoreEnumerationResult.Succeeded([]);
        public OemDriverPackage? FindPackage(string publishedName) => null;
        public DriverPackageMutationResult ExportPackage(string publishedName, string destinationDirectory)
            => DriverPackageMutationResult.Ok("exported");
        public DriverPackageMutationResult DeletePackage(string publishedName, bool uninstallFromDevices, bool force = false)
            => DriverPackageMutationResult.Ok("deleted");
        public DriverPackageMutationResult AddPackage(string infPath)
            => DriverPackageMutationResult.Ok("added");
    }

    private sealed class EmptyDeviceGuardStatus : IDeviceGuardStatus
    {
        public DeviceGuardSnapshot GetSnapshot()
            => new(
                DeviceGuardFeatureState.Unavailable,
                DeviceGuardFeatureState.Unavailable,
                DeviceGuardFeatureState.Unavailable,
                DeviceGuardFeatureState.Unavailable,
                "test");
    }

    private sealed class EmptyCodeIntegrityInspector : ICodeIntegrityInspector
    {
        public CodeIntegrityInspectionResult ReadObservedSignals(TimeSpan lookback, CancellationToken cancellationToken = default)
            => CodeIntegrityInspectionResult.Available([]);
    }

    private sealed class EmptyHvciCompatibilityInspector : IHvciCompatibilityInspector
    {
        public HvciImageAnalysis AnalyzeDriverImage(string imagePath)
            => HvciImageAnalysis.Compatible();
    }

    private sealed class EmptyPrivilegeBroker : IPrivilegeBroker
    {
        public Task<ApplyResult> ExecuteElevatedAsync(OptimizationAction action, CancellationToken cancellationToken)
            => Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, 0, "ok"));

        public Task<IElevatedPrivilegeSession> BeginElevatedSessionAsync(CancellationToken cancellationToken)
            => Task.FromResult<IElevatedPrivilegeSession>(new EmptyElevatedSession());
    }

    private sealed class EmptyElevatedSession : IElevatedPrivilegeSession
    {
        public Task<ApplyResult> ExecuteAsync(
            OptimizationAction action,
            CancellationToken cancellationToken,
            string? displayName = null)
            => Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, 0, "ok"));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
