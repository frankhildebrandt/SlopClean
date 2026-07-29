using SlopClean.Core.Models;

namespace SlopClean.Core.Planning;

public interface IOptimizationPlanSession
{
    OptimizationPlan? Current { get; }

    void Set(OptimizationPlan plan);

    void Clear();
}
