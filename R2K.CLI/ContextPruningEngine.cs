using System.Text.RegularExpressions;

namespace R2K.CLI;

public sealed class ContextPruningEngine(ContextPruner contextPruner)
{
    public ContextPruningResult Prune(IReadOnlyCollection<string> commandArgs, PruningStrategy strategy)
    {
        var fileContexts = ResolveFileArguments(commandArgs);
        var files = fileContexts.Select(context => context.Path).ToArray();
        var originalContext = string.Join(Environment.NewLine, fileContexts.Select(context => File.ReadAllText(context.Path)));
        var prunedContext = strategy == PruningStrategy.Agentic
            ? string.Join(Environment.NewLine, fileContexts.Select(context => contextPruner.Prune(context.Path, context.TargetLine)))
            : originalContext;

        return new ContextPruningResult(
            files,
            prunedContext,
            EstimateTokens(originalContext),
            EstimateTokens(prunedContext));
    }

    private static IReadOnlyList<TargetedFileContext> ResolveFileArguments(IReadOnlyCollection<string> commandArgs)
    {
        var files = new List<TargetedFileContext>();
        int? globalTargetLine = ResolveGlobalLineReference(commandArgs);
        foreach (string arg in commandArgs)
        {
            if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith("-", StringComparison.Ordinal))
                continue;

            string candidateArg = arg.Trim('"', '\'');
            int? targetLine = TrySplitPathAndLine(candidateArg, out string pathFromLineRef)
                ?? globalTargetLine;
            string candidate = Path.GetFullPath(pathFromLineRef);
            if (File.Exists(candidate))
                files.Add(new TargetedFileContext(candidate, targetLine));
        }

        return files;
    }

    private static int? ResolveGlobalLineReference(IReadOnlyCollection<string> commandArgs)
    {
        string[] args = commandArgs.ToArray();
        for (var i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if ((string.Equals(arg, "--line", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(arg, "line", StringComparison.OrdinalIgnoreCase))
                && i + 1 < args.Length
                && int.TryParse(args[i + 1], out int nextLine))
            {
                return nextLine;
            }

            Match match = Regex.Match(arg, @"^(--)?line[:=](\d+)$", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[2].Value, out int inlineLine))
                return inlineLine;
        }

        return null;
    }

    private static int? TrySplitPathAndLine(string value, out string path)
    {
        int separatorIndex = value.LastIndexOf(':');
        if (separatorIndex > 0
            && separatorIndex < value.Length - 1
            && int.TryParse(value[(separatorIndex + 1)..], out int lineNumber))
        {
            path = value[..separatorIndex];
            return lineNumber;
        }

        path = value;
        return null;
    }

    private static int EstimateTokens(string value)
        => (int)Math.Ceiling(value.Length / 4m);

    private sealed record TargetedFileContext(string Path, int? TargetLine);
}

public sealed record ContextPruningResult(
    IReadOnlyList<string> Files,
    string PrunedContext,
    int OriginalTokenCount,
    int PrunedTokenCount);
