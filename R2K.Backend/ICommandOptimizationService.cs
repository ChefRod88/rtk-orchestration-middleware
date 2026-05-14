namespace R2K.Backend;

public sealed record OptimizationMetrics(
    string OptimizedCommand,
    int TokensOriginal,
    int TokensOptimized,
    decimal EfficiencyPercent);

public interface ICommandOptimizationService
{
    OptimizationMetrics Compute(string rawCommand);
}
