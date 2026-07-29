using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
using SlopClean.Platform.Windows;
using static SlopClean.Platform.Windows.ElevatedPrivilegeBroker;

if (args.Length < 2 || !string.Equals(args[0], "--pipe", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: SlopClean.Elevated --pipe <name>");
    return 1;
}

var pipeName = args[1].Trim('"');
var fileSystem = new WindowsFileSystem();
var registry = new WindowsRegistryStore();
var recycleBin = new WindowsRecycleBinService();
var safety = new SafetyPolicy(fileSystem);

try
{
    await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    // UI process may still be setting up the server after UAC; allow a longer connect window.
    await client.ConnectAsync(60_000).ConfigureAwait(false);

    while (true)
    {
        var lengthBuffer = new byte[sizeof(int)];
        await ReadExactAsync(client, lengthBuffer).ConfigureAwait(false);
        var requestLength = BitConverter.ToInt32(lengthBuffer, 0);
        if (requestLength == 0)
        {
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
                result = Execute(request.Action, fileSystem, registry, recycleBin);
            }
        }

        await WriteResponseAsync(client, request.Nonce, result).ConfigureAwait(false);
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 10;
}

static ApplyResult Execute(
    OptimizationAction action,
    IFileSystem fileSystem,
    IRegistryStore registry,
    IRecycleBinService recycleBin)
{
    try
    {
        switch (action.OperationCode)
        {
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

static async Task WriteResponseAsync(Stream stream, string nonce, ApplyResult result)
{
    var response = new ElevatedResponse { Nonce = nonce, Result = result };
    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response));
    await stream.WriteAsync(BitConverter.GetBytes(bytes.Length)).ConfigureAwait(false);
    await stream.WriteAsync(bytes).ConfigureAwait(false);
    await stream.FlushAsync().ConfigureAwait(false);
}

static async Task ReadExactAsync(Stream stream, byte[] buffer)
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
