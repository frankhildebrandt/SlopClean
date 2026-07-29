using SlopClean.Core.Models;

namespace SlopClean.Core.Abstractions;

public interface IPrivilegeBroker
{
    Task<ApplyResult> ExecuteElevatedAsync(
        OptimizationAction action,
        CancellationToken cancellationToken);
}
