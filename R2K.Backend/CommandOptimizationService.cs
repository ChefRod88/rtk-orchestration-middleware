using Tiktoken;

namespace R2K.Backend;

/// <summary>Uses OpenAI-aligned <see cref="TikTokenEncoder"/> for token parity with GPT-style tooling.</summary>
public sealed class CommandOptimizationService(
    Encoder encoder,
    ICommandOptimizer optimizer) : ICommandOptimizationService
{
    public OptimizationMetrics Compute(string rawCommand)
    {
        rawCommand ??= string.Empty;

        string optimizedCommand = optimizer.Optimize(rawCommand);
        var originalTokens = encoder.CountTokens(rawCommand);
        var optimizedTokens = encoder.CountTokens(optimizedCommand);
        decimal efficiency = originalTokens > 0
            ? Math.Round(((decimal)(originalTokens - optimizedTokens) / originalTokens) * 100, 2)
            : 0;

        return new OptimizationMetrics(optimizedCommand, originalTokens, optimizedTokens, efficiency);
    }
}
