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
    private readonly string _helperPath;

    public ElevatedPrivilegeBroker(string? helperPath = null)
    {
        _helperPath = helperPath
            ?? Path.Combine(AppContext.BaseDirectory, "SlopClean.Elevated.exe");
    }

    public async Task<ApplyResult> ExecuteElevatedAsync(
        OptimizationAction action,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_helperPath))
        {
            return ApplyResult.Failed(action.Id, action.FindingId, $"Elevated helper not found at '{_helperPath}'.");
        }

        var pipeName = $"SlopClean.Elevated.{Guid.NewGuid():N}";
        var nonce = Guid.NewGuid().ToString("N");
        var request = new ElevatedRequest
        {
            Nonce = nonce,
            Action = action
        };

        await using var server = CreateServer(pipeName);
        using var process = StartElevated(pipeName);
        try
        {
            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

            var payload = JsonSerializer.Serialize(request);
            var bytes = Encoding.UTF8.GetBytes(payload);
            await server.WriteAsync(BitConverter.GetBytes(bytes.Length), cancellationToken).ConfigureAwait(false);
            await server.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);

            var lengthBuffer = new byte[sizeof(int)];
            await ReadExactAsync(server, lengthBuffer, cancellationToken).ConfigureAwait(false);
            var responseLength = BitConverter.ToInt32(lengthBuffer, 0);
            if (responseLength is < 0 or > 1_000_000)
            {
                return ApplyResult.Failed(action.Id, action.FindingId, "Invalid elevated helper response.");
            }

            var responseBuffer = new byte[responseLength];
            await ReadExactAsync(server, responseBuffer, cancellationToken).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize<ElevatedResponse>(Encoding.UTF8.GetString(responseBuffer));
            if (response is null || !string.Equals(response.Nonce, nonce, StringComparison.Ordinal))
            {
                return ApplyResult.Failed(action.Id, action.FindingId, "Elevated helper nonce validation failed.");
            }

            try
            {
                if (!process.HasExited)
                {
                    using var exitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    exitCts.CancelAfter(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }
            catch
            {
                // ignored
            }

            return response.Result ?? ApplyResult.Failed(action.Id, action.FindingId, "Empty elevated result.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
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
    {
        var security = new PipeSecurity();
        var identity = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Unable to resolve current user SID.");
        security.AddAccessRule(new PipeAccessRule(identity, PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
    }

    private Process StartElevated(string pipeName)
    {
        var start = new ProcessStartInfo
        {
            FileName = _helperPath,
            Arguments = $"--pipe \"{pipeName}\"",
            UseShellExecute = true,
            Verb = "runas"
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
