namespace SlopClean.Core.Parameters;

public sealed class PathListParameter : IModuleParameter
{
    public PathListParameter(
        string id,
        string displayName,
        string description,
        IReadOnlyList<string>? defaultValue = null)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        DefaultValue = defaultValue ?? Array.Empty<string>();
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public object? DefaultValue { get; }
    public Type ValueType => typeof(IReadOnlyList<string>);

    public ParameterValidationResult Validate(object? value)
    {
        if (value is null)
        {
            return ParameterValidationResult.Success();
        }

        if (value is not IEnumerable<string> paths)
        {
            return ParameterValidationResult.Fail($"Parameter '{Id}' must be a list of paths.");
        }

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return ParameterValidationResult.Fail($"Parameter '{Id}' contains an empty path.");
            }
        }

        return ParameterValidationResult.Success();
    }

    public IReadOnlyList<string> Resolve(IReadOnlyDictionary<string, object?> values)
    {
        if (values.TryGetValue(Id, out var raw) && raw is IEnumerable<string> paths)
        {
            return paths.ToArray();
        }

        return (IReadOnlyList<string>)DefaultValue!;
    }
}
