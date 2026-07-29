using Microsoft.Windows.ApplicationModel.Resources;
using SlopClean.Core.Modules;
using SlopClean.Modules.CoreIsolationDrivers;

namespace SlopClean.App.Helpers;

internal static class ModuleLocalization
{
    public static (string Name, string Description) Resolve(IModule module)
    {
        if (module.Id != CoreIsolationDriversModule.ModuleId)
        {
            return (module.Name, module.Description);
        }

        try
        {
            var resources = new ResourceLoader();
            var name = resources.GetString("ModuleCoreIsolationDriversName");
            var description = resources.GetString("ModuleCoreIsolationDriversDescription");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
            {
                return (module.Name, module.Description);
            }

            return (name, description);
        }
        catch
        {
            return (module.Name, module.Description);
        }
    }
}
