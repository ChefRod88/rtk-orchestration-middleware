using Tiktoken;

namespace R2K.Backend;

public sealed record PromptOptimizationMetrics(
    string OptimizedPrompt,
    int TokensOriginal,
    int TokensOptimized,
    int TokensSaved,
    decimal EfficiencyPercent);

public interface IPromptOptimizationService
{
    PromptOptimizationMetrics Compute(string rawPrompt);
}

public sealed class PromptOptimizationService(Encoder encoder) : IPromptOptimizationService
{
    public PromptOptimizationMetrics Compute(string rawPrompt)
    {
        rawPrompt ??= string.Empty;
        string optimizedPrompt = PromptTextOptimizer.Optimize(rawPrompt);
        int originalTokens = encoder.CountTokens(rawPrompt);
        int optimizedTokens = encoder.CountTokens(optimizedPrompt);
        int tokensSaved = Math.Max(0, originalTokens - optimizedTokens);
        decimal efficiency = originalTokens > 0
            ? Math.Round(((decimal)tokensSaved / originalTokens) * 100, 2)
            : 0;

        return new PromptOptimizationMetrics(
            optimizedPrompt,
            originalTokens,
            optimizedTokens,
            tokensSaved,
            efficiency);
    }
}

internal static class PromptTextOptimizer
{
    internal static string Optimize(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return string.Empty;

        string normalizedNewlines = prompt.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        var lines = normalizedNewlines.Split('\n');
        var optimized = new List<string>(lines.Length);
        var inCodeFence = false;
        var previousWasBlank = false;

        foreach (string line in lines)
        {
            string trimmedStart = line.TrimStart();
            if (trimmedStart.StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                optimized.Add(line.TrimEnd());
                previousWasBlank = false;
                continue;
            }

            if (inCodeFence)
            {
                optimized.Add(line.TrimEnd());
                previousWasBlank = false;
                continue;
            }

            string normalizedLine = NormalizeHorizontalWhitespace(line.Trim());
            if (normalizedLine.Length == 0)
            {
                if (!previousWasBlank && optimized.Count > 0)
                    optimized.Add(string.Empty);

                previousWasBlank = true;
                continue;
            }

            optimized.Add(normalizedLine);
            previousWasBlank = false;
        }

        while (optimized.Count > 0 && optimized[^1].Length == 0)
            optimized.RemoveAt(optimized.Count - 1);

        return string.Join('\n', optimized);
    }

    private static string NormalizeHorizontalWhitespace(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var w = 0;
        var previousWasSpace = false;

        foreach (char c in value)
        {
            bool isHorizontalWhitespace = c is ' ' or '\t';
            if (isHorizontalWhitespace)
            {
                if (previousWasSpace)
                    continue;

                buffer[w++] = ' ';
                previousWasSpace = true;
                continue;
            }

            buffer[w++] = c;
            previousWasSpace = false;
        }

        return w == 0 ? string.Empty : new string(buffer[..w]);
    }
}
