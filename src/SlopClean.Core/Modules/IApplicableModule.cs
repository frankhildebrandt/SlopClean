using SlopClean.Core.Models;

namespace SlopClean.Core.Modules;

public interface IApplicableModule : IModule
{
    Task<ApplyResult> ApplyAsync(
        OptimizationAction action,
        CancellationToken cancellationToken);
}
