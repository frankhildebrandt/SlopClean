using SlopClean.Core.Models;

namespace SlopClean.Core.Planning;

public sealed class OptimizationPlanSession : IOptimizationPlanSession
{
    private OptimizationPlan? _current;

    public OptimizationPlan? Current => _current;

    public void Set(OptimizationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _current = plan;
    }

    public void Clear() => _current = null;
}