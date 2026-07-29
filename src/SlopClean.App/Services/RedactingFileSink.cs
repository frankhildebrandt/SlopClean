using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using SlopClean.Core.Logging;

namespace SlopClean.App.Services;

/// <summary>
/// Writes log events to a rolling file after redacting sensitive path fragments.
/// </summary>
public sealed class RedactingFileSink : ILogEventSink, IDisposable
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly MessageTemplateTextFormatter _formatter = new(
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
    private StreamWriter? _writer;
    private string? _currentDate;

    public RedactingFileSink(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public void Emit(LogEvent logEvent)
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        lock (_gate)
        {
            if (_writer is null || _currentDate != date)
            {
                _writer?.Dispose();
                _currentDate = date;
                var path = Path.Combine(_directory, $"slopclean-{date}.log");
                _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    AutoFlush = true
                };
            }

            using var buffer = new StringWriter();
            _formatter.Format(logEvent, buffer);
            _writer.Write(RedactingEnricher.EnrichMessage(buffer.ToString()));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
