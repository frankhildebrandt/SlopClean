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
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromMinutes(2);

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
        var server = CreateServer(pipeName);
        Process process;
        try
        {
            process = StartHelper(pipeName);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException("Administrator approval was cancelled.");
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        try
        {
            await WaitForConnectionOrExitAsync(server, process, _elevate, ConnectionTimeout, cancellationToken)
                .ConfigureAwait(false);
            return new ElevatedSession(server, process);
        }
        catch
        {
            TryKill(process);
            process.Dispose();
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task WaitForConnectionOrExitAsync(
        NamedPipeServerStream server,
        Process process,
        bool elevate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var connectTask = server.WaitForConnectionAsync(timeoutCts.Token);

        // ShellExecute + runas often returns a process handle that is already exited / not the
        // elevated helper. Racing WaitForExit then false-fails the session. Only race exit for
        // non-elevated test stubs where the handle is reliable.
        if (!elevate)
        {
            var exitTask = process.WaitForExitAsync(timeoutCts.Token);
            var finished = await Task.WhenAny(connectTask, exitTask).ConfigureAwait(false);
            if (finished == exitTask)
            {
                await exitTask.ConfigureAwait(false);
                var code = process.HasExited ? process.ExitCode.ToString() : "?";
                throw new InvalidOperationException(
                    $"Elevated helper exited before it could connect (exit code {code}).");
            }
        }

        try
        {
            await connectTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var detail = SafeExitDetail(process);
            throw new InvalidOperationException(
                "Timed out waiting for the elevated helper to connect"
                + (detail is null ? "." : $" ({detail}).")
                + " Approve the UAC prompt if it is still open.");
        }
    }

    private static string? SafeExitDetail(Process process)
    {
        try
        {
            return process.HasExited ? $"helper exit code {process.ExitCode}" : null;
        }
        catch
        {
            return null;
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

    private static NamedPipeServerStream CreateServer(string pipeName)
        => NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            CreatePipeSecurity());

    /// <summary>
    /// Elevated helpers run at high integrity; the UI process is medium. Allowing only the
    /// current user SID is not enough — the elevated token needs BuiltinAdministrators too.
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

    private Process StartHelper(string pipeName)
    {
        // UseShellExecute is required for the UAC "runas" verb. For tests (elevate: false),
        // keep UseShellExecute so .cmd stubs still launch.
        var start = new ProcessStartInfo
        {
            FileName = _helperPath,
            Arguments = $"--pipe \"{pipeName}\"",
            WorkingDirectory = Path.GetDirectoryName(_helperPath) ?? AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = _elevate ? "runas" : string.Empty
        };

        return Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start elevated helper.");
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
        private readonly NamedPipeServerStream _server;
        private readonly Process _process;
        private bool _disposed;

        public ElevatedSession(NamedPipeServerStream server, Process process)
        {
            _server = server;
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
            await WriteFrameAsync(_server, bytes, cancellationToken).ConfigureAwait(false);

            var lengthBuffer = new byte[sizeof(int)];
            await ReadExactAsync(_server, lengthBuffer, cancellationToken).ConfigureAwait(false);
            var responseLength = BitConverter.ToInt32(lengthBuffer, 0);
            if (responseLength is < 0 or > 1_000_000)
            {
                return ApplyResult.Failed(action.Id, action.FindingId, "Invalid elevated helper response.");
            }

            var responseBuffer = new byte[responseLength];
            await ReadExactAsync(_server, responseBuffer, cancellationToken).ConfigureAwait(false);
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
                if (_server.IsConnected && !_process.HasExited)
                {
                    // Length 0 signals end-of-session to the helper.
                    await _server.WriteAsync(BitConverter.GetBytes(0)).ConfigureAwait(false);
                    await _server.FlushAsync().ConfigureAwait(false);
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
                await _server.DisposeAsync().ConfigureAwait(false);
                _process.Dispose();
            }
        }
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
