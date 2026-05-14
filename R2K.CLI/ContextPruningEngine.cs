namespace R2K.CLI;

public sealed class ContextPruningEngine(ContextPruner contextPruner)
{
    public ContextPruningResult Prune(IReadOnlyCollection<string> commandArgs, PruningStrategy strategy)
    {
        var files = ResolveFileArguments(commandArgs);
        var originalContext = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        var prunedContext = strategy == PruningStrategy.Agentic
            ? string.Join(Environment.NewLine, files.Select(contextPruner.Prune))
            : originalContext;

        return new ContextPruningResult(
            files,
            prunedContext,
            EstimateTokens(originalContext),
            EstimateTokens(prunedContext));
    }

    private static IReadOnlyList<string> ResolveFileArguments(IReadOnlyCollection<string> commandArgs)
    {
        var files = new List<string>();
        foreach (string arg in commandArgs)
        {
            if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith("-", StringComparison.Ordinal))
                continue;

            string candidate = Path.GetFullPath(arg);
            if (File.Exists(candidate))
                files.Add(candidate);
        }

        return files;
    }

    private static int EstimateTokens(string value)
        => (int)Math.Ceiling(value.Length / 4m);
}

public sealed record ContextPruningResult(
    IReadOnlyList<string> Files,
    string PrunedContext,
    int OriginalTokenCount,
    int PrunedTokenCount);
