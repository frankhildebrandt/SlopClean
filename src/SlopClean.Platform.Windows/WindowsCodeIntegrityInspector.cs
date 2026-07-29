using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;

namespace SlopClean.Platform.Windows;

public sealed partial class WindowsCodeIntegrityInspector : ICodeIntegrityInspector
{
    // Observed HVCI / CI load policy events. 3087 is the Memory Integrity compatibility event family.
    // 3033/3089 are noisy (often user-mode DLL loads) and are intentionally omitted.
    private static readonly HashSet<int> WatchedEventIds = [3023, 3074, 3076, 3077, 3082, 3083, 3087, 3099, 3111];

    [GeneratedRegex(@"(?:[\\/]|^|\s)(?<file>[A-Za-z0-9][A-Za-z0-9._-]*\.sys)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SysFileRegex();

    public CodeIntegrityInspectionResult ReadObservedSignals(TimeSpan lookback, CancellationToken cancellationToken = default)
    {
        try
        {
            var since = DateTime.UtcNow - lookback;
            // Event Log query uses local time in some builds; use both bounds loosely via XPath timestamp is awkward —
            // filter in-process after read.
            const string query = "*[System[Provider[@Name='Microsoft-Windows-CodeIntegrity']]]";
            var signals = new List<CodeIntegritySignal>();

            var log = new EventLogQuery("Microsoft-Windows-CodeIntegrity/Operational", PathType.LogName, query)
            {
                ReverseDirection = true
            };
            using var reader = new EventLogReader(log);

            EventRecord? record;
            while ((record = reader.ReadEvent()) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (record)
                {
                    if (!WatchedEventIds.Contains(record.Id))
                    {
                        continue;
                    }

                    var ts = record.TimeCreated?.ToUniversalTime() ?? DateTime.MinValue;
                    if (ts < since)
                    {
                        break;
                    }

                    string? message = null;
                    try
                    {
                        message = record.FormatDescription();
                    }
                    catch
                    {
                        message = record.ToXml();
                    }

                    message ??= string.Empty;
                    var match = SysFileRegex().Match(message);
                    var file = match.Success ? match.Groups["file"].Value : null;
                    signals.Add(new CodeIntegritySignal(
                        EventId: record.Id,
                        TimestampUtc: new DateTimeOffset(ts, TimeSpan.Zero),
                        ImageFileName: file,
                        Publisher: null,
                        RawMessage: message.Length > 500 ? message[..500] + "…" : message));

                    if (signals.Count >= 200)
                    {
                        break;
                    }
                }
            }

            return CodeIntegrityInspectionResult.Available(signals);
        }
        catch (EventLogException ex)
        {
            return CodeIntegrityInspectionResult.Unavailable($"Code Integrity log unavailable: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return CodeIntegrityInspectionResult.Unavailable($"Code Integrity log access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            return CodeIntegrityInspectionResult.Unavailable(ex.Message);
        }
    }
}
