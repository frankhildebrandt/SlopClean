namespace SlopClean.Core.Engine;

/// <summary>
/// Ensures at most one I/O-intensive scan runs per drive letter at a time.
/// </summary>
public sealed class DriveScanScheduler : IAsyncDisposable
{
    private readonly Dictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public async Task<IDisposable> AcquireAsync(string? driveRoot, CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(driveRoot)
            ? "_"
            : driveRoot.TrimEnd('\\', '/').ToUpperInvariant();

        SemaphoreSlim semaphore;
        lock (_gate)
        {
            if (!_locks.TryGetValue(key, out semaphore!))
            {
                semaphore = new SemaphoreSlim(1, 1);
                _locks[key] = semaphore;
            }
        }

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            foreach (var semaphore in _locks.Values)
            {
                semaphore.Dispose();
            }

            _locks.Clear();
        }

        return ValueTask.CompletedTask;
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
