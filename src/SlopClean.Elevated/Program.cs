using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SlopClean.Core.Safety;
using SlopClean.Platform.Windows;

namespace SlopClean.Elevated;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            _ = new SafetyPolicy(new WindowsFileSystem());
            return 0;
        }

        if (!TryParseArgs(args, out var pipeName, out var sessionNonce, out var headless))
        {
            Console.Error.WriteLine("Usage: SlopClean.Elevated --pipe <name> --nonce <nonce> [--headless]");
            return 1;
        }

        // Headless (tests / unelevated): skip WinUI so IPC stays fast and CI stays windowless.
        if (headless)
        {
            return new ElevatedHost(pipeName, sessionNonce).RunAsync().GetAwaiter().GetResult();
        }

        LaunchArgs.PipeName = pipeName;
        LaunchArgs.SessionNonce = sessionNonce;

        WinRT.ComWrappersSupport.InitializeComWrappers();

        Application.Start(static _ =>
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            new App();
        });

        return 0;
    }

    private static bool TryParseArgs(
        string[] args,
        out string pipeName,
        out string sessionNonce,
        out bool headless)
    {
        pipeName = "";
        sessionNonce = "";
        headless = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--headless", StringComparison.OrdinalIgnoreCase))
            {
                headless = true;
                continue;
            }

            if (i >= args.Length - 1)
            {
                continue;
            }

            if (string.Equals(args[i], "--pipe", StringComparison.OrdinalIgnoreCase))
            {
                pipeName = args[i + 1].Trim('"');
            }
            else if (string.Equals(args[i], "--nonce", StringComparison.OrdinalIgnoreCase))
            {
                sessionNonce = args[i + 1].Trim('"');
            }
        }

        return !string.IsNullOrWhiteSpace(pipeName) && !string.IsNullOrWhiteSpace(sessionNonce);
    }
}
