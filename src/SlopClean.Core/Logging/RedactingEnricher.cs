namespace SlopClean.Core.Logging;

/// <summary>
/// Pure helper used by host logging sinks to redact sensitive path fragments.
/// Kept free of Serilog types so Core stays dependency-light and testable.
/// </summary>
public static class RedactingEnricher
{
    public static string EnrichMessage(string message) => LogRedactor.Redact(message);
}
