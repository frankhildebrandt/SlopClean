using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
using SlopClean.Platform.Windows;

namespace SlopClean.Platform.Windows.Tests;

public class ElevatedHelperContractTests
{
    [Fact]
    public void Pipe_security_allows_current_user_and_administrators()
    {
        var security = ElevatedPrivilegeBroker.CreatePipeSecurity();
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>()
            .Select(r => (SecurityIdentifier)r.IdentityReference)
            .ToArray();

        var user = WindowsIdentity.GetCurrent().User!;
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        Assert.Contains(user, rules);
        Assert.Contains(admins, rules);
    }

    [Fact]
    public void Safety_policy_rejects_unknown_operation_before_helper()
    {
        var fs = new WindowsFileSystem();
        var policy = new SafetyPolicy(fs);
        var action = new OptimizationAction(
            "1",
            "x",
            "y",
            "rm-rf-system",
            Path.GetTempPath(),
            Path.GetTempPath(),
            RequiredPrivilege.Elevated);

        Assert.False(policy.ValidateAction(action).IsAllowed);
        Assert.DoesNotContain("rm-rf-system", PrivilegedOperationCodes.All);
    }

    [Fact]
    public void ResolveDefaultHelperPath_prefers_elevated_subdirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SlopClean.HelperLayout.{Guid.NewGuid():N}");
        var nestedDir = Path.Combine(root, "elevated");
        Directory.CreateDirectory(nestedDir);
        var nested = Path.Combine(nestedDir, "SlopClean.Elevated.exe");
        var flat = Path.Combine(root, "SlopClean.Elevated.exe");
        File.WriteAllBytes(nested, [0]);
        File.WriteAllBytes(flat, [0]);
        try
        {
            Assert.Equal(nested, ElevatedPrivilegeBroker.ResolveDefaultHelperPath(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveDefaultHelperPath_falls_back_to_flat_layout()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SlopClean.HelperLayout.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var flat = Path.Combine(root, "SlopClean.Elevated.exe");
        File.WriteAllBytes(flat, [0]);
        try
        {
            Assert.Equal(flat, ElevatedPrivilegeBroker.ResolveDefaultHelperPath(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Broker_fails_when_helper_missing()
    {
        var broker = new ElevatedPrivilegeBroker(helperPath: Path.Combine(Path.GetTempPath(), "missing-helper.exe"));
        var action = new OptimizationAction(
            "1",
            "temp-cleaner",
            "f",
            PrivilegedOperationCodes.DeleteFile,
            Path.Combine(Path.GetTempPath(), "nope.txt"),
            Path.GetTempPath(),
            RequiredPrivilege.Elevated);

        var result = await broker.ExecuteElevatedAsync(action, CancellationToken.None);
        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_preflight_fails_quickly_with_non_uac_message_when_helper_cannot_start()
    {
        var helper = Path.Combine(Path.GetTempPath(), $"SlopClean.FakeHelper.{Guid.NewGuid():N}.exe");
        await File.WriteAllBytesAsync(helper, [0]);
        try
        {
            Process? Start(ProcessStartInfo _)
                => Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c exit 1",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                });

            foreach (var elevate in new[] { false, true })
            {
                var broker = new ElevatedPrivilegeBroker(
                    helperPath: helper,
                    elevate: elevate,
                    startProcess: Start,
                    preflightTimeout: TimeSpan.FromSeconds(5),
                    connectTimeout: TimeSpan.FromSeconds(5));

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => broker.BeginElevatedSessionAsync(CancellationToken.None));

                Assert.Contains("failed to start", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("UAC", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Approve", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            File.Delete(helper);
        }
    }

    [Fact]
    public async Task Broker_fails_quickly_when_helper_passes_preflight_but_exits_without_ready_ipc()
    {
        var helper = Path.Combine(Path.GetTempPath(), $"SlopClean.FakeHelper.{Guid.NewGuid():N}.exe");
        await File.WriteAllBytesAsync(helper, [0]);
        try
        {
            Process? Start(ProcessStartInfo psi)
            {
                var args = psi.Arguments ?? string.Empty;
                var exit = args.Contains("--self-test", StringComparison.Ordinal) ? 0 : 1;
                return Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c exit {exit}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                });
            }

            var broker = new ElevatedPrivilegeBroker(
                helperPath: helper,
                elevate: false,
                startProcess: Start,
                preflightTimeout: TimeSpan.FromSeconds(5),
                connectTimeout: TimeSpan.FromSeconds(5));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => broker.BeginElevatedSessionAsync(CancellationToken.None));

            Assert.True(
                ex.Message.Contains("exited", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("ready", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("IPC", StringComparison.OrdinalIgnoreCase),
                ex.Message);
            Assert.DoesNotContain("Approve the UAC", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(helper);
        }
    }

    [Fact]
    public async Task Broker_arms_connect_timeout_only_after_helper_process_start_returns()
    {
        var helperPath = FindBuiltHelper();
        Assert.True(helperPath is not null, "Build SlopClean.Elevated before running this test.");

        // slowLaunch must exceed connectTimeout so an early-armed clock fails the test;
        // connectTimeout must still allow a headless helper to connect after Start returns.
        var connectTimeout = TimeSpan.FromMilliseconds(800);
        var slowLaunch = TimeSpan.FromMilliseconds(1600);

        Process? Start(ProcessStartInfo psi)
        {
            var args = psi.Arguments ?? string.Empty;
            if (args.Contains("--self-test", StringComparison.Ordinal))
            {
                return Process.Start(CreateUnelevatedStart(helperPath, "--self-test"));
            }

            // Block longer than connectTimeout before returning. If the broker armed the
            // connect CTS before Process.Start returned, WaitForConnection is already dead.
            Thread.Sleep(slowLaunch);
            var launchArgs = args.Contains("--headless", StringComparison.Ordinal)
                ? args
                : args + " --headless";
            return Process.Start(CreateUnelevatedStart(helperPath, launchArgs));
        }

        var broker = new ElevatedPrivilegeBroker(
            helperPath: helperPath,
            elevate: true,
            startProcess: Start,
            connectTimeout: connectTimeout,
            preflightTimeout: TimeSpan.FromSeconds(15));

        await using var session = await broker.BeginElevatedSessionAsync(CancellationToken.None);

        var missing = Path.Combine(Path.GetTempPath(), $"slopclean-missing-{Guid.NewGuid():N}.tmp");
        var result = await session.ExecuteAsync(
            new OptimizationAction(
                "1",
                "temp-cleaner",
                "f",
                PrivilegedOperationCodes.DeleteFile,
                missing,
                Path.GetTempPath(),
                RequiredPrivilege.Elevated),
            CancellationToken.None);

        Assert.Equal(ApplyOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task Broker_completes_ready_handshake_with_unelevated_helper()
    {
        var helperPath = FindBuiltHelper();
        Assert.True(helperPath is not null, "Build SlopClean.Elevated before running this test.");

        var broker = new ElevatedPrivilegeBroker(helperPath: helperPath, elevate: false);
        await using var session = await broker.BeginElevatedSessionAsync(CancellationToken.None);

        var missing = Path.Combine(Path.GetTempPath(), $"slopclean-missing-{Guid.NewGuid():N}.tmp");
        var result = await session.ExecuteAsync(
            new OptimizationAction(
                "1",
                "temp-cleaner",
                "f",
                PrivilegedOperationCodes.DeleteFile,
                missing,
                Path.GetTempPath(),
                RequiredPrivilege.Elevated),
            CancellationToken.None);

        Assert.Equal(ApplyOutcome.Skipped, result.Outcome);
    }

    private static ProcessStartInfo CreateUnelevatedStart(string helperPath, string arguments)
        => new()
        {
            FileName = helperPath,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(helperPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

    private static string? FindBuiltHelper()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "SlopClean.Elevated", "bin", "x64", "Release", "net10.0-windows10.0.19041.0", "win-x64", "SlopClean.Elevated.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "SlopClean.Elevated", "bin", "x64", "Debug", "net10.0-windows10.0.19041.0", "win-x64", "SlopClean.Elevated.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "SlopClean.Elevated", "bin", "Release", "net10.0-windows10.0.19041.0", "win-x64", "SlopClean.Elevated.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "SlopClean.Elevated", "bin", "Debug", "net10.0-windows10.0.19041.0", "win-x64", "SlopClean.Elevated.exe")),
        };

        foreach (var candidate in candidates.Where(File.Exists))
        {
            // Prefer a helper that understands --self-test (skip stale outputs).
            try
            {
                using var process = Process.Start(CreateUnelevatedStart(candidate, "--self-test"));
                if (process is null)
                {
                    continue;
                }

                if (!process.WaitForExit(15_000))
                {
                    TryKill(process);
                    continue;
                }

                if (process.ExitCode == 0)
                {
                    return candidate;
                }
            }
            catch
            {
                // try next candidate
            }
        }

        return null;
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
}
