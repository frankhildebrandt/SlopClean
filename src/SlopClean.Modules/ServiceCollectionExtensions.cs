using Microsoft.Extensions.DependencyInjection.Extensions;
using SlopClean.Core.Backup;
using SlopClean.Core.Engine;
using SlopClean.Core.Modules;
using SlopClean.Core.Planning;
using SlopClean.Core.Safety;
using SlopClean.Core.Settings;
using SlopClean.Modules.BrowserCleaner;
using SlopClean.Modules.CoreIsolationDrivers;
using SlopClean.Modules.DiskAnalyzer;
using SlopClean.Modules.RecycleBin;
using SlopClean.Modules.ServiceAdvisor;
using SlopClean.Modules.StartupManager;
using SlopClean.Modules.TempCleaner;
using SlopClean.Modules.UninstallCleanup;

namespace Microsoft.Extensions.DependencyInjection;

public static class SlopCleanModulesServiceCollectionExtensions
{
    public static IServiceCollection AddSlopCleanModules(this IServiceCollection services)
    {
        services.TryAddSingleton<SafetyPolicy>();
        services.TryAddSingleton<DriveScanScheduler>();
        services.TryAddSingleton<IAppSettingsStore, AppSettingsStore>();
        services.TryAddSingleton<IRestorePointStore, RestorePointStore>();
        services.TryAddSingleton<IBackupService, BackupService>();
        services.TryAddSingleton<IOptimizationPlanSession, OptimizationPlanSession>();

        services.AddSingleton<IModule, TempCleanerModule>();
        services.AddSingleton<IModule, RecycleBinModule>();
        services.AddSingleton<IModule, BrowserCleanerModule>();
        services.AddSingleton<IModule, StartupManagerModule>();
        services.AddSingleton<IModule, DiskAnalyzerModule>();
        services.AddSingleton<IModule, UninstallCleanupModule>();
        services.AddSingleton<IModule, ServiceAdvisorModule>();
        services.AddSingleton<IModule, CoreIsolationDriversModule>();

        services.TryAddSingleton<ModuleRegistry>();
        services.TryAddSingleton<OptimizationEngine>();
        return services;
    }
}