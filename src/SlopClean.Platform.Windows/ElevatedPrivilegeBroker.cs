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
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultPreflightTimeout = TimeSpan.FromSeconds(15);

    private readonly string _helperPath;
    private readonly bool _elevate;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _preflightTimeout;

    public ElevatedPrivilegeBroker(
        string? helperPath = null,
        bool elevate = true,
        Func<ProcessStartInfo, Process?>? startProcess = null,
        TimeSpan? connectTimeout = null,
        TimeSpan? preflightTimeout = null)
    {
        _helperPath = helperPath ?? ResolveDefaultHelperPath(AppContext.BaseDirectory);
        _elevate = elevate;
        _startProcess = startProcess ?? (psi => Process.Start(psi));
        _connectTimeout = connectTimeout ?? DefaultConnectTimeout;
        _preflightTimeout = preflightTimeout ?? DefaultPreflightTimeout;
    }

    /// <summary>
    /// Prefers <c>elevated\SlopClean.Elevated.exe</c> (self-contained publish layout), else flat beside the app.
    /// </summary>
    public static string ResolveDefaultHelperPath(string baseDirectory)
    {
        var nested = Path.Combine(baseDirectory, "elevated", "SlopClean.Elevated.exe");
        if (File.Exists(nested))
        {
            return nested;
        }

        return Path.Combine(baseDirectory, "SlopClean.Elevated.exe");
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

        await RunPreflightAsync(cancellationToken).ConfigureAwait(false);

        var pipeName = $"SlopClean.Elevated.{Guid.NewGuid():N}";
        var sessionNonce = Guid.NewGuid().ToString("N");

        // Medium-IL UI owns the pipe. High-IL elevated helper connects as client and sends "ready".
        // (Opposite ownership breaks under UAC mandatory integrity.)
        var server = CreateServer(pipeName);
        Process? process = null;
        try
        {
            // Do not arm the connect timeout until Process.Start returns (UAC may block).
            var connectTask = server.WaitForConnectionAsync(cancellationToken);

            try
            {
                process = StartHelper(pipeName, sessionNonce, elevate: _elevate);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                throw new InvalidOperationException("Administrator approval was cancelled.");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_connectTimeout);

            await WaitForHelperConnectionAsync(connectTask, process, cancellationToken, timeoutCts.Token)
                .ConfigureAwait(false);
            await ReadReadyAsync(server, sessionNonce, timeoutCts.Token).ConfigureAwait(false);
            return new ElevatedSession(server, process);
        }
        catch
        {
            if (process is not null)
            {
                TryKill(process);
                process.Dispose();
            }

            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task RunPreflightAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = _helperPath,
            Arguments = "--self-test",
            WorkingDirectory = Path.GetDirectoryName(_helperPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        Process process;
        try
        {
            process = _startProcess(start)
                ?? throw new InvalidOperationException(
                    $"Elevated helper failed to start (missing files or runtime). Path: '{_helperPath}'.");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"Elevated helper failed to start (missing files or runtime). Path: '{_helperPath}'. {ex.Message}");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_preflightTimeout);

            string stderr;
            try
            {
                var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                stderr = await stderrTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw new InvalidOperationException(
                    $"Elevated helper failed to start (preflight timed out). Path: '{_helperPath}'.");
            }

            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? $"exit code {process.ExitCode}" : stderr.Trim();
                throw new InvalidOperationException(
                    $"Elevated helper failed to start (missing files or runtime). Path: '{_helperPath}'. {detail}");
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    private async Task WaitForHelperConnectionAsync(
        Task connectTask,
        Process process,
        CancellationToken userCancellation,
        CancellationToken waitCancellation)
    {
        if (!_elevate)
        {
            var exitTask = process.WaitForExitAsync(waitCancellation);
            var finished = await Task.WhenAny(connectTask, exitTask).ConfigureAwait(false);
            if (finished == exitTask)
            {
                await exitTask.ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"Elevated helper exited before IPC ready handshake (exit code {process.ExitCode}).");
            }
        }

        try
        {
            await connectTask.WaitAsync(waitCancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!userCancellation.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "Timed out waiting for elevated helper IPC ready message after administrator approval.");
        }
    }

    private Process StartHelper(string pipeName, string sessionNonce, bool elevate)
    {
        var start = new ProcessStartInfo
        {
            FileName = _helperPath,
            Arguments = $"--pipe \"{pipeName}\" --nonce \"{sessionNonce}\"",
            WorkingDirectory = Path.GetDirectoryName(_helperPath) ?? AppContext.BaseDirectory,
            UseShellExecute = elevate,
            Verb = elevate ? "runas" : string.Empty,
            CreateNoWindow = !elevate
        };

        if (!elevate)
        {
            start.RedirectStandardError = true;
            start.RedirectStandardOutput = true;
        }

        return _startProcess(start)
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
    /// Pipe ACL for the UI-hosted server. Elevated helpers include BuiltinAdministrators.
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
                if (_server.IsConnected)
                {
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
