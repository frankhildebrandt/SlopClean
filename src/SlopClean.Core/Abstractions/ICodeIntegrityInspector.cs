using SlopClean.Core.Models;

namespace SlopClean.Core.Abstractions;

public interface ICodeIntegrityInspector
{
    CodeIntegrityInspectionResult ReadObservedSignals(TimeSpan lookback, CancellationToken cancellationToken = default);
}

public sealed record CodeIntegrityInspectionResult(
    bool IsAvailable,
    string? FailureReason,
    IReadOnlyList<CodeIntegritySignal> Signals)
{
    public static CodeIntegrityInspectionResult Unavailable(string reason)
        => new(false, reason, []);

    public static CodeIntegrityInspectionResult Available(IReadOnlyList<CodeIntegritySignal> signals)
        => new(true, null, signals);
}
