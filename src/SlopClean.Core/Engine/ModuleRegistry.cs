using SlopClean.Core.Modules;

namespace SlopClean.Core.Engine;

public sealed class ModuleRegistry
{
    private readonly Dictionary<string, IModule> _modules;

    public ModuleRegistry(IEnumerable<IModule> modules)
    {
        _modules = modules.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IModule> All => _modules.Values;

    public IModule GetRequired(string moduleId)
    {
        if (!_modules.TryGetValue(moduleId, out var module))
        {
            throw new KeyNotFoundException($"Module '{moduleId}' was not found.");
        }

        return module;
    }

    public T GetRequired<T>(string moduleId) where T : class, IModule
    {
        var module = GetRequired(moduleId);
        if (module is not T typed)
        {
            throw new InvalidOperationException($"Module '{moduleId}' does not implement {typeof(T).Name}.");
        }

        return typed;
    }

    public IEnumerable<IScannableModule> Scannable => All.OfType<IScannableModule>();
    public IEnumerable<IApplicableModule> Applicable => All.OfType<IApplicableModule>();
}
