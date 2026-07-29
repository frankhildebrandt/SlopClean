using SlopClean.Core.Models;

namespace SlopClean.Core.Abstractions;

public interface IPrivilegeBroker
{
    Task<ApplyResult> ExecuteElevatedAsync(
        OptimizationAction action,
        CancellationToken cancellationToken);

    /// <summary>
    /// Starts one elevated helper session (single UAC prompt) for multiple privileged actions.
    /// </summary>
    Task<IElevatedPrivilegeSession> BeginElevatedSessionAsync(CancellationToken cancellationToken);
}

public interface IElevatedPrivilegeSession : IAsyncDisposable
{
    Task<ApplyResult> ExecuteAsync(OptimizationAction action, CancellationToken cancellationToken);
}
