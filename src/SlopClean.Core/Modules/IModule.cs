using SlopClean.Core.Parameters;

namespace SlopClean.Core.Modules;

public interface IModule
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    ModuleCategory Category { get; }
    IReadOnlyList<IModuleParameter> Parameters { get; }
}
