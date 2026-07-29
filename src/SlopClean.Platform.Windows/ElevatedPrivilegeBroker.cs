using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;

namespace SlopClean.Platform.Windows;

public sealed class ElevatedPrivilegeBroker : IPrivilegeBroker
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(45);

    private readonly string _helperPath;
    private readonly bool _elevate;

    public ElevatedPrivilegeBroker(string? helperPath = null, bool elevate = true)
    {
        _helperPath = helperPath
            ?? Path.Combine(AppContext.BaseDirectory, "SlopClean.Elevated.exe");
        _elevate = elevate;
    }

    public async Task<ApplyResult> ExecuteElevatedAsync(
        OptimizationAction action,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_helperPath))
        {
            return ApplyResult.Failed(action.Id, action.FindingId, $"Elevated helper not found at '{_helperPath}'.");
        }

        try
        {
            await using var session = await BeginElevatedSessionAsync(cancellationToken).ConfigureAwait(false);
            return await session.ExecuteAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return ApplyResult.Failed(action.Id, action.FindingId, ex.Message);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return ApplyResult.Failed(action.Id, action.FindingId, "Administrator approval was cancelled.");
        }
    }

    public async Task<IElevatedPrivilegeSession> BeginElevatedSessionAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_helperPath))
        {
            throw new InvalidOperationException($"Elevated helper not found at '{_helperPath}'.");
        }

        var pipeName = $"SlopClean.Elevated.{Guid.NewGuid():N}";
        var sessionNonce = Guid.NewGuid().ToString("N");

        Process? process = null;
        NamedPipeClientStream? client = null;
        try
        {
            process = StartHelper(pipeName, sessionNonce);
            client = await ConnectAndHandshakeAsync(pipeName, sessionNonce, process, cancellationToken)
                .ConfigureAwait(false);
            return new ElevatedSession(client, process);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            if (process is not null)
            {
                TryKill(process);
                process.Dispose();
            }

            throw new InvalidOperationException("Administrator approval was cancelled.");
        }
        catch
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            if (process is not null)
            {
                TryKill(process);
                process.Dispose();
            }

            throw;
        }
    }

    private async Task<NamedPipeClientStream> ConnectAndHandshakeAsync(
        string pipeName,
        string sessionNonce,
        Process process,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_elevate ? ConnectTimeout : TimeSpan.FromSeconds(5));

        Exception? last = null;
        while (!timeoutCts.IsCancellationRequested)
        {
            // NamedPipeClientStream is single-use after a failed ConnectAsync on some runtimes.
            var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            try
            {
                await client.ConnectAsync(500, timeoutCts.Token).ConfigureAwait(false);
                await ReadReadyAsync(client, sessionNonce, timeoutCts.Token).ConfigureAwait(false);
                return client;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException)
            {
                last = ex;
                await client.DisposeAsync().ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                // Fail fast for non-elevated stubs that exit immediately.
                if (!_elevate)
                {
                    try
                    {
                        if (process.HasExited)
                        {
                            throw new InvalidOperationException(
                                $"Elevated helper exited before IPC ready handshake (exit code {process.ExitCode}).");
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        throw;
                    }
                    catch
                    {
                        // ignore unreliable process handle
                    }
                }

                await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            "Timed out waiting for elevated helper IPC ready message. "
            + "Approve the UAC prompt if it is still open."
            + (last is null ? "" : $" ({last.GetType().Name})"));
    }

    private Process StartHelper(string pipeName, string sessionNonce)
    {
        var start = new ProcessStartInfo
        {
            FileName = _helperPath,
            Arguments = $"--pipe \"{pipeName}\" --nonce \"{sessionNonce}\"",
            WorkingDirectory = Path.GetDirectoryName(_helperPath) ?? AppContext.BaseDirectory,
            UseShellExecute = _elevate,
            Verb = _elevate ? "runas" : string.Empty,
            CreateNoWindow = !_elevate
        };

        return Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start elevated helper.");
    }

    private static async Task ReadReadyAsync(
        Stream stream,
        string expectedNonce,
        CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[sizeof(int)];
        await ReadExactAsync(stream, lengthBuffer, cancellationToken).ConfigureAwait(false);
        var length = BitConverter.ToInt32(lengthBuffer, 0);
        if (length is < 0 or > 1_000_000)
        {
            throw new InvalidOperationException("Invalid elevated helper ready frame.");
        }

        var buffer = new byte[length];
        await ReadExactAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
        var ready = JsonSerializer.Deserialize<ElevatedReady>(Encoding.UTF8.GetString(buffer));
        if (ready is null
            || !string.Equals(ready.Kind, "ready", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(ready.Nonce, expectedNonce, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Elevated helper ready handshake failed.");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    /// DACL for the helper-hosted pipe. Mandatory integrity is lowered separately via
    /// <see cref="PipeIntegrity"/> so the medium-IL UI client can connect.
    /// </summary>
    public static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        var identity = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Unable to resolve current user SID.");
        security.AddAccessRule(new PipeAccessRule(identity, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return security;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }

    private static async Task WriteFrameAsync(Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class ElevatedSession : IElevatedPrivilegeSession
    {
        private readonly NamedPipeClientStream _client;
        private readonly Process _process;
        private bool _disposed;

        public ElevatedSession(NamedPipeClientStream client, Process process)
        {
            _client = client;
            _process = process;
        }

        public async Task<ApplyResult> ExecuteAsync(OptimizationAction action, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var nonce = Guid.NewGuid().ToString("N");
            var request = new ElevatedRequest
            {
                Nonce = nonce,
                Action = action
            };

            var payload = JsonSerializer.Serialize(request);
            var bytes = Encoding.UTF8.GetBytes(payload);
            await WriteFrameAsync(_client, bytes, cancellationToken).ConfigureAwait(false);

            var lengthBuffer = new byte[sizeof(int)];
            await ReadExactAsync(_client, lengthBuffer, cancellationToken).ConfigureAwait(false);
            var responseLength = BitConverter.ToInt32(lengthBuffer, 0);
            if (responseLength is < 0 or > 1_000_000)
            {
                return ApplyResult.Failed(action.Id, action.FindingId, "Invalid elevated helper response.");
            }

            var responseBuffer = new byte[responseLength];
            await ReadExactAsync(_client, responseBuffer, cancellationToken).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize<ElevatedResponse>(Encoding.UTF8.GetString(responseBuffer));
            if (response is null || !string.Equals(response.Nonce, nonce, StringComparison.Ordinal))
            {
                return ApplyResult.Failed(action.Id, action.FindingId, "Elevated helper nonce validation failed.");
            }

            return response.Result ?? ApplyResult.Failed(action.Id, action.FindingId, "Empty elevated result.");
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (_client.IsConnected)
                {
                    await _client.WriteAsync(BitConverter.GetBytes(0)).ConfigureAwait(false);
                    await _client.FlushAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // ignored
            }

            try
            {
                if (!_process.HasExited)
                {
                    using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        await _process.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        TryKill(_process);
                    }
                }
            }
            catch
            {
                TryKill(_process);
            }
            finally
            {
                await _client.DisposeAsync().ConfigureAwait(false);
                _process.Dispose();
            }
        }
    }

    public sealed class ElevatedReady
    {
        public string Kind { get; set; } = "ready";
        public string Nonce { get; set; } = "";
        public int Pid { get; set; }
    }

    public sealed class ElevatedRequest
    {
        public string Nonce { get; set; } = "";
        public OptimizationAction Action { get; set; } = null!;
    }

    public sealed class ElevatedResponse
    {
        public string Nonce { get; set; } = "";
        public ApplyResult? Result { get; set; }
    }
}
