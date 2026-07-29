using System.ServiceProcess;
using SlopClean.Core.Abstractions;

namespace SlopClean.Platform.Windows;

public sealed class WindowsServiceManager : IServiceManager
{
    public IReadOnlyList<WindowsServiceInfo> GetServices()
    {
        return ServiceController.GetServices()
            .Select(Map)
            .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public WindowsServiceInfo? GetService(string serviceName)
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            return Map(controller);
        }
        catch
        {
            return null;
        }
    }

    private static WindowsServiceInfo Map(ServiceController controller)
    {
        string startType;
        try { startType = controller.StartType.ToString(); }
        catch { startType = "Unknown"; }

        return new WindowsServiceInfo(
            controller.ServiceName,
            controller.DisplayName,
            startType,
            controller.Status.ToString(),
            Description: null);
    }
}
