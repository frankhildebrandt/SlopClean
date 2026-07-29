using Microsoft.Extensions.DependencyInjection;
using SlopClean.Core.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

public static class SlopCleanPlatformServiceCollectionExtensions
{
    public static IServiceCollection AddSlopCleanWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IFileSystem, SlopClean.Platform.Windows.WindowsFileSystem>();
        services.AddSingleton<IRegistryStore, SlopClean.Platform.Windows.WindowsRegistryStore>();
        services.AddSingleton<IProcessInspector, SlopClean.Platform.Windows.WindowsProcessInspector>();
        services.AddSingleton<IServiceManager, SlopClean.Platform.Windows.WindowsServiceManager>();
        services.AddSingleton<IRecycleBinService, SlopClean.Platform.Windows.WindowsRecycleBinService>();
        services.AddSingleton<IPrivilegeBroker, SlopClean.Platform.Windows.ElevatedPrivilegeBroker>();
        return services;
    }
}
