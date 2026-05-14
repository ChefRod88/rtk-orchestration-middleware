using System.Text.RegularExpressions;
using System.Diagnostics;

namespace R2K.CLI;

public sealed class ContextPruningEngine(ContextPruner contextPruner)
{
    public ContextPruningResult Prune(IReadOnlyCollection<string> commandArgs, PruningStrategy strategy)
        => strategy switch
        {
            PruningStrategy.Agentic => PruneFiles(commandArgs, PruningStrategy.Agentic),
            PruningStrategy.DiffOnly => PruneGitDiff(),
            _ => PruneFiles(commandArgs, PruningStrategy.Minimal),
        };

    private ContextPruningResult PruneFiles(IReadOnlyCollection<string> commandArgs, PruningStrategy strategy)
    {
        var fileContexts = ResolveFileArguments(commandArgs);
        var files = fileContexts.Select(context => context.Path).ToArray();
        var originalContext = string.Join(Environment.NewLine, fileContexts.Select(context => File.ReadAllText(context.Path)));
        var prunedContext = strategy == PruningStrategy.Agentic
            ? string.Join(Environment.NewLine, fileContexts.Select(context => contextPruner.Prune(context.Path, context.TargetLine)))
            : originalContext;
        prunedContext = SensitiveDataRedactor.Redact(prunedContext);

        return new ContextPruningResult(
            files,
            prunedContext,
            EstimateTokens(originalContext),
            EstimateTokens(prunedContext));
    }

    private static ContextPruningResult PruneGitDiff()
    {
        string unstaged = RunGitDiff("--no-ext-diff", "--unified=3");
        string staged = RunGitDiff("--no-ext-diff", "--cached", "--unified=3");
        string diffContext = string.Join(
            Environment.NewLine,
            new[] { unstaged, staged }.Where(value => !string.IsNullOrWhiteSpace(value)));
        string redactedDiffContext = SensitiveDataRedactor.Redact(diffContext);
        return new ContextPruningResult(
            [],
            redactedDiffContext,
            EstimateTokens(diffContext),
            EstimateTokens(redactedDiffContext));
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

    private static string RunGitDiff(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            string shimDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share",
                "r2k",
                "shims");
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.Environment["PATH"] = string.Join(
                Path.PathSeparator,
                path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Where(part => !string.Equals(part, shimDir, StringComparison.Ordinal)));
            foreach (string arg in new[] { "diff" }.Concat(args))
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process is null)
                return string.Empty;

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.TrimEnd() : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private sealed record TargetedFileContext(string Path, int? TargetLine);
}

public sealed record ContextPruningResult(
    IReadOnlyList<string> Files,
    string PrunedContext,
    int OriginalTokenCount,
    int PrunedTokenCount);
