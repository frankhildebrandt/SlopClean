namespace SlopClean.Core.Models;

public sealed record CodeIntegritySignal(
    int EventId,
    DateTimeOffset TimestampUtc,
    string? ImageFileName,
    string? Publisher,
    string RawMessage);
