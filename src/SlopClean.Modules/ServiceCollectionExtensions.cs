using Microsoft.Extensions.DependencyInjection.Extensions;
using SlopClean.Core.Engine;
using SlopClean.Core.Modules;
using SlopClean.Core.Safety;
using SlopClean.Modules;

namespace Microsoft.Extensions.DependencyInjection;

public static class SlopCleanModulesServiceCollectionExtensions
{
    public static IServiceCollection AddSlopCleanModules(this IServiceCollection services)
    {
        services.TryAddSingleton<SafetyPolicy>();
        services.TryAddSingleton<DriveScanScheduler>();

        services.AddSingleton<IModule, TempCleanerModule>();
        services.AddSingleton<IModule, RecycleBinModule>();
        services.AddSingleton<IModule, BrowserCleanerModule>();
        services.AddSingleton<IModule, StartupManagerModule>();
        services.AddSingleton<IModule, DiskAnalyzerModule>();
        services.AddSingleton<IModule, UninstallCleanupModule>();
        services.AddSingleton<IModule, ServiceAdvisorModule>();

        services.TryAddSingleton<ModuleRegistry>();
        services.TryAddSingleton<OptimizationEngine>();
        return services;
    }
}
