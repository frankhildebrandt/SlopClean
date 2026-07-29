using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
using SlopClean.Platform.Windows;
using static SlopClean.Platform.Windows.ElevatedPrivilegeBroker;

namespace SlopClean.Elevated;

/// <summary>
/// Named-pipe elevated session host. Optional UI callbacks marshal via <see cref="DispatcherQueue"/>.
/// </summary>
internal sealed class ElevatedHost
{
    private readonly string _pipeName;
    private readonly string _sessionNonce;
    private readonly DispatcherQueue? _dispatcher;
    private readonly Action<string, string>? _onJobChanged;

    public ElevatedHost(
        string pipeName,
        string sessionNonce,
        DispatcherQueue? dispatcher = null,
        Action<string, string>? onJobChanged = null)
    {
        _pipeName = pipeName;
        _sessionNonce = sessionNonce;
        _dispatcher = dispatcher;
        _onJobChanged = onJobChanged;
    }

    public async Task<int> RunAsync()
    {
        var fileSystem = new WindowsFileSystem();
        var registry = new WindowsRegistryStore();
        var recycleBin = new WindowsRecycleBinService();
        var driverStore = new WindowsDriverStore();
        var safety = new SafetyPolicy(fileSystem);

        Report(ElevatedUiStrings.StatusWaiting, ElevatedUiStrings.JobNone);

        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await client.ConnectAsync(60_000).ConfigureAwait(false);

            var ready = new ElevatedReady
            {
                Kind = "ready",
                Nonce = _sessionNonce,
                Pid = Environment.ProcessId
            };
            var readyBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ready));
            await client.WriteAsync(BitConverter.GetBytes(readyBytes.Length)).ConfigureAwait(false);
            await client.WriteAsync(readyBytes).ConfigureAwait(false);
            await client.FlushAsync().ConfigureAwait(false);

            Report(ElevatedUiStrings.StatusReady, ElevatedUiStrings.JobNone);

            while (true)
            {
                var lengthBuffer = new byte[sizeof(int)];
                await ReadExactAsync(client, lengthBuffer).ConfigureAwait(false);
                var requestLength = BitConverter.ToInt32(lengthBuffer, 0);
                if (requestLength == 0)
                {
                    Report(ElevatedUiStrings.StatusFinished, ElevatedUiStrings.JobNone);
                    return 0;
                }

                if (requestLength is < 0 or > 1_000_000)
                {
                    return 2;
                }

                var requestBuffer = new byte[requestLength];
                await ReadExactAsync(client, requestBuffer).ConfigureAwait(false);
                var request = JsonSerializer.Deserialize<ElevatedRequest>(Encoding.UTF8.GetString(requestBuffer));
                if (request?.Action is null || string.IsNullOrWhiteSpace(request.Nonce))
                {
                    return 3;
                }

                var jobLabel = ResolveJobLabel(request);
                Report(ElevatedUiStrings.StatusWorking, jobLabel);

                ApplyResult result;
                if (!PrivilegedOperationCodes.All.Contains(request.Action.OperationCode))
                {
                    result = ApplyResult.Failed(
                        request.Action.Id,
                        request.Action.FindingId,
                        "Operation code is not allowed.");
                }
                else
                {
                    var validation = safety.ValidateAction(request.Action);
                    if (!validation.IsAllowed)
                    {
                        result = ApplyResult.Skipped(
                            request.Action.Id,
                            request.Action.FindingId,
                            validation.Reason ?? "Blocked by safety policy.");
                    }
                    else
                    {
                        result = Execute(request.Action, fileSystem, registry, recycleBin, driverStore);
                    }
                }

                await WriteResponseAsync(client, request.Nonce, result).ConfigureAwait(false);
                Report(ElevatedUiStrings.StatusReady, ElevatedUiStrings.JobNone);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Report(ElevatedUiStrings.StatusFailed, ex.Message);
            return 10;
        }
    }

    private void Report(string status, string job)
    {
        if (_onJobChanged is null)
        {
            return;
        }

        if (_dispatcher is null)
        {
            _onJobChanged(status, job);
            return;
        }

        _ = _dispatcher.TryEnqueue(() => _onJobChanged(status, job));
    }

    private static string ResolveJobLabel(ElevatedRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return request.DisplayName;
        }

        var action = request.Action;
        if (!string.IsNullOrWhiteSpace(action.Path))
        {
            return action.Path;
        }

        return string.IsNullOrWhiteSpace(action.FindingId)
            ? action.OperationCode
            : action.FindingId;
    }

    private static ApplyResult Execute(
        OptimizationAction action,
        IFileSystem fileSystem,
        IRegistryStore registry,
        IRecycleBinService recycleBin,
        IDriverStore driverStore)
    {
        try
        {
            switch (action.OperationCode)
            {
                case PrivilegedOperationCodes.DeleteDriverPackage:
                case PrivilegedOperationCodes.RestoreDriverPackage:
                    return DriverPackageElevatedOperations.Execute(action, driverStore);

                case PrivilegedOperationCodes.DeleteFile:
                    if (string.IsNullOrWhiteSpace(action.Path) || !fileSystem.FileExists(action.Path))
                    {
                        return ApplyResult.Skipped(action.Id, action.FindingId, "File no longer exists.");
                    }

                    var fileInfo = fileSystem.GetFileInfo(action.Path);
                    fileSystem.DeleteFile(action.Path);
                    return ApplyResult.Succeeded(action.Id, action.FindingId, fileInfo?.Length ?? 0, "File deleted.");

                case PrivilegedOperationCodes.DeleteDirectory:
                    if (string.IsNullOrWhiteSpace(action.Path) || !fileSystem.DirectoryExists(action.Path))
                    {
                        return ApplyResult.Skipped(action.Id, action.FindingId, "Directory no longer exists.");
                    }

                    var size = fileSystem.GetDirectorySize(action.Path);
                    fileSystem.DeleteDirectory(action.Path, recursive: true);
                    return ApplyResult.Succeeded(action.Id, action.FindingId, size, "Directory deleted.");

                case PrivilegedOperationCodes.EmptyRecycleBin:
                    var before = recycleBin.Query();
                    recycleBin.Empty();
                    return ApplyResult.Succeeded(action.Id, action.FindingId, before.SizeBytes, "Recycle Bin emptied.");

                case PrivilegedOperationCodes.DeleteRegistryValue:
                    {
                        var hive = Enum.Parse<RegistryHiveKind>(action.Payload!["hive"], ignoreCase: true);
                        var subKey = action.Payload["subKey"];
                        var valueName = action.Payload["valueName"];
                        registry.DeleteValue(hive, subKey, valueName);
                        return ApplyResult.Succeeded(action.Id, action.FindingId, 0, "Registry value deleted.");
                    }

                case PrivilegedOperationCodes.DeleteRegistryKey:
                    {
                        var hive = Enum.Parse<RegistryHiveKind>(action.Payload!["hive"], ignoreCase: true);
                        var subKey = action.Payload["subKey"];
                        registry.DeleteSubKeyTree(hive, subKey);
                        return ApplyResult.Succeeded(action.Id, action.FindingId, 0, "Registry key deleted.");
                    }

                default:
                    return ApplyResult.Failed(action.Id, action.FindingId, "Unsupported operation.");
            }
        }
        catch (Exception ex)
        {
            return ApplyResult.Failed(action.Id, action.FindingId, ex.Message);
        }
    }

    private static async Task WriteResponseAsync(Stream stream, string nonce, ApplyResult result)
    {
        var response = new ElevatedResponse { Nonce = nonce, Result = result };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response));
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length)).ConfigureAwait(false);
        await stream.WriteAsync(bytes).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset)).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }
}
