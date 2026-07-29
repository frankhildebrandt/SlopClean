using System.Diagnostics;
using System.Text;
using SlopClean.Core.Abstractions;

namespace SlopClean.Platform.Windows;

internal static class PnPUtilRunner
{
    private const int ErrorSuccessRebootRequired = 3010;
    private const int MaxOutputChars = 256_000;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    public static string ResolveExecutablePath()
    {
        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return Path.Combine(windir, "System32", "pnputil.exe");
    }

    public static DriverPackageMutationResult Run(IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        var exe = ResolveExecutablePath();
        if (!File.Exists(exe))
        {
            return DriverPackageMutationResult.Fail($"pnputil not found at '{exe}'.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            return DriverPackageMutationResult.Fail("Failed to start pnputil.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var waitTimeout = timeout ?? DefaultTimeout;
        if (!process.WaitForExit((int)waitTimeout.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            return DriverPackageMutationResult.Fail("pnputil timed out.");
        }

        var stdout = Truncate(stdoutTask.GetAwaiter().GetResult());
        var stderr = Truncate(stderrTask.GetAwaiter().GetResult());
        var message = string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout}\n{stderr}".Trim();

        if (process.ExitCode == 0)
        {
            return DriverPackageMutationResult.Ok(string.IsNullOrWhiteSpace(message) ? "pnputil succeeded." : message);
        }

        if (process.ExitCode == ErrorSuccessRebootRequired)
        {
            return DriverPackageMutationResult.Ok(
                string.IsNullOrWhiteSpace(message) ? "Operation succeeded; reboot required." : message,
                rebootRequired: true);
        }

        return DriverPackageMutationResult.Fail(
            string.IsNullOrWhiteSpace(message) ? $"pnputil failed with exit code {process.ExitCode}." : message,
            process.ExitCode);
    }

    private static string Truncate(string text)
        => text.Length <= MaxOutputChars ? text : text[..MaxOutputChars] + "…";
}
