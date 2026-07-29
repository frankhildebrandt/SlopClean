using SlopClean.Core.Models;

namespace SlopClean.Core.Abstractions;

public interface IDeviceGuardStatus
{
    DeviceGuardSnapshot GetSnapshot();
}
