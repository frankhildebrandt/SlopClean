using System.Reflection;

namespace SlopClean.Core.Modules;

/// <summary>
/// Helpers for opening embedded resources shipped with module assemblies.
/// </summary>
public static class EmbeddedResourceStreams
{
    public const string ModuleIllustrationSuffix = ".Assets.illustration.png";

    public static Stream OpenModuleIllustration(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        return OpenRequired(moduleType.Assembly, ModuleIllustrationSuffix);
    }

    public static Stream OpenRequired(Assembly assembly, string nameSuffix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameSuffix);

        var names = assembly.GetManifestResourceNames();
        string? match = null;
        foreach (var name in names)
        {
            if (!name.EndsWith(nameSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match is not null)
            {
                throw new InvalidOperationException(
                    $"Multiple embedded resources ending with '{nameSuffix}' in '{assembly.GetName().Name}'.");
            }

            match = name;
        }

        if (match is null)
        {
            var available = names.Length == 0 ? "(none)" : string.Join(", ", names);
            throw new InvalidOperationException(
                $"Embedded resource ending with '{nameSuffix}' not found in '{assembly.GetName().Name}'. Available: {available}");
        }

        return assembly.GetManifestResourceStream(match)
            ?? throw new InvalidOperationException($"Failed to open embedded resource '{match}'.");
    }
}
