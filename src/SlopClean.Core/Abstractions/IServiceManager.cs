namespace SlopClean.Core.Abstractions;

public interface IServiceManager
{
    IReadOnlyList<WindowsServiceInfo> GetServices();
    WindowsServiceInfo? GetService(string serviceName);
}

public sealed record WindowsServiceInfo(
    string Name,
    string DisplayName,
    string StartType,
    string Status,
    string? Description);
