using System.Text.Json;

namespace SlopClean.Core.Parameters;

/// <summary>
/// Shared helpers for UI bindings and preset restore across parameter types.
/// Binding templates may evaluate typed accessors for every item; helpers must be fail-safe.
/// </summary>
public static class ParameterValueCoercion
{
    public static int ReadInt(IModuleParameter parameter, object? value)
    {
        if (parameter is not IntParameter)
        {
            return 0;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var parsed) => parsed,
            JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var parsed) => parsed,
            _ => parameter.DefaultValue is int fallback ? fallback : 0
        };
    }

    public static bool ReadBool(IModuleParameter parameter, object? value)
    {
        if (parameter is not BoolParameter)
        {
            return false;
        }

        return value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            JsonElement je when je.ValueKind is JsonValueKind.True or JsonValueKind.False => je.GetBoolean(),
            _ => parameter.DefaultValue is true
        };
    }

    public static object? CoercePreset(IModuleParameter parameter, object? value)
    {
        if (value is null)
        {
            return parameter.DefaultValue;
        }

        if (parameter is BoolParameter)
        {
            return ReadBool(parameter, value);
        }

        if (parameter is IntParameter)
        {
            return ReadInt(parameter, value);
        }

        if (parameter is PathListParameter)
        {
            return CoercePathList(parameter, value);
        }

        if (parameter is EnumParameter)
        {
            return value switch
            {
                string s => s,
                JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString() ?? parameter.DefaultValue,
                _ => value.ToString() ?? parameter.DefaultValue
            };
        }

        return value is string or int or bool ? value : value.ToString();
    }

    public static IReadOnlyList<string> CoercePathList(IModuleParameter parameter, object? value)
    {
        if (value is IEnumerable<string> paths)
        {
            return paths.ToArray();
        }

        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Array)
            {
                return je.EnumerateArray()
                    .Select(static e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(static s => !string.IsNullOrWhiteSpace(s))
                    .Cast<string>()
                    .ToArray();
            }

            if (je.ValueKind == JsonValueKind.String)
            {
                return ParsePathList(je.GetString());
            }
        }

        if (value is string text)
        {
            return ParsePathList(text);
        }

        return parameter.DefaultValue as IReadOnlyList<string> ?? Array.Empty<string>();
    }

    public static string FormatPathList(IEnumerable<string>? paths)
    {
        if (paths is null)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, paths.Where(static p => !string.IsNullOrWhiteSpace(p)));
    }

    public static IReadOnlyList<string> ParsePathList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return text
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .ToArray();
    }
}
