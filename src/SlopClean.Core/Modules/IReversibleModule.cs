using SlopClean.Core.Models;

namespace SlopClean.Core.Modules;

public interface IReversibleModule : IApplicableModule
{
    Task<RestoreToken> CreateRestoreAsync(
        OptimizationAction action,
        CancellationToken cancellationToken);

    Task<ApplyResult> RestoreAsync(
        RestoreToken token,
        CancellationToken cancellationToken);
}
