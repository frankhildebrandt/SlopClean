using System.Text.Json;

namespace SlopClean.Core.Presets;

public sealed class PresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _rootDirectory;

    public PresetStore(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SlopClean",
                "presets");
    }

    public async Task SaveAsync(string moduleId, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_rootDirectory);
        var path = GetPath(moduleId);
        var temp = path + ".tmp";
        var document = new PresetDocument
        {
            Version = 1,
            ModuleId = moduleId,
            SavedUtc = DateTimeOffset.UtcNow,
            Values = values.ToDictionary(static kv => kv.Key, static kv => kv.Value)
        };

        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Copy(temp, path, overwrite: true);
        File.Delete(temp);
    }

    public async Task<IReadOnlyDictionary<string, object?>?> LoadAsync(string moduleId, CancellationToken cancellationToken)
    {
        var path = GetPath(moduleId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<PresetDocument>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(true);
        return document?.Values;
    }

    private string GetPath(string moduleId)
        => Path.Combine(_rootDirectory, $"{moduleId}.json");

    private sealed class PresetDocument
    {
        public int Version { get; set; }
        public string ModuleId { get; set; } = "";
        public DateTimeOffset SavedUtc { get; set; }
        public Dictionary<string, object?> Values { get; set; } = new();
    }
}
