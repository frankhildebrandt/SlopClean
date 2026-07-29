namespace SlopClean.Core.Models;

public sealed record RestoreToken(
    string Id,
    string ModuleId,
    string ActionId,
    DateTimeOffset CreatedUtc,
    string Kind,
    IReadOnlyDictionary<string, string> Data);
