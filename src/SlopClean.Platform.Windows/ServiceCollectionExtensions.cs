using Microsoft.Extensions.DependencyInjection;
using SlopClean.Core.Abstractions;
using SlopClean.Platform.Windows;

namespace Microsoft.Extensions.DependencyInjection;

public static class SlopCleanPlatformServiceCollectionExtensions
{
    public static IServiceCollection AddSlopCleanWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IFileSystem, WindowsFileSystem>();
        services.AddSingleton<IRegistryStore, WindowsRegistryStore>();
        services.AddSingleton<IProcessInspector, WindowsProcessInspector>();
        services.AddSingleton<IServiceManager, WindowsServiceManager>();
        services.AddSingleton<IRecycleBinService, WindowsRecycleBinService>();
        services.AddSingleton<IPrivilegeBroker, ElevatedPrivilegeBroker>();
        services.AddSingleton<IDriverStore, WindowsDriverStore>();
        services.AddSingleton<IDeviceGuardStatus, WindowsDeviceGuardStatus>();
        services.AddSingleton<ICodeIntegrityInspector, WindowsCodeIntegrityInspector>();
        services.AddSingleton<IHvciCompatibilityInspector, PeHvciCompatibilityInspector>();
        return services;
    }
}
