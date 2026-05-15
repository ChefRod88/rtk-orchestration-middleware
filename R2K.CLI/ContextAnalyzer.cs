using System.Text.RegularExpressions;

namespace R2K.CLI;

public sealed class ContextAnalyzer
{
    private static readonly string[] CandidateExtensions =
    [
        ".cs",
        ".ts",
        ".tsx",
        ".js",
        ".jsx",
        ".py",
        ".json",
        ".yml",
        ".yaml",
        ".sql",
        ".md",
        ".sln",
        ".csproj"
    ];

    private static readonly string[] IgnoredPathParts =
    [
        $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}coverage{Path.DirectorySeparatorChar}",
    ];

    public ContextAnalysisResult Analyze(string prompt, string workspaceRoot)
        => Analyze(prompt, workspaceRoot, forceWorkspaceScan: false);

    public ContextAnalysisResult Analyze(string prompt, string workspaceRoot, bool forceWorkspaceScan)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var explicitArgs = ExtractExplicitContextArgs(prompt);
        if (explicitArgs.Count > 0)
        {
            return new ContextAnalysisResult(
                explicitArgs,
                "explicit-references",
                "Prompt named file or line references.");
        }

        var keywords = ExtractKeywords(prompt);
        var candidates = Directory.EnumerateFiles(workspaceRoot, "*.*", SearchOption.AllDirectories)
            .Where(IsCandidateFile)
            .Select(path => new
            {
                Path = path,
                Score = ScoreFile(path, keywords),
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Path, StringComparer.Ordinal)
            .Take(3)
            .Select(item => item.Path)
            .ToArray();

        if (candidates.Length > 0)
            return new ContextAnalysisResult(
                candidates,
                forceWorkspaceScan ? "forced-workspace-discovery" : "workspace-discovery",
                forceWorkspaceScan
                    ? "RTK trigger forced workspace discovery by prompt keyword overlap."
                    : "Selected files by prompt keyword overlap.");

        string? fallback = FindPrimaryProjectFile(workspaceRoot);
        return fallback is null
            ? new ContextAnalysisResult([], "no-context", "No relevant workspace context discovered.")
            : new ContextAnalysisResult(
                [fallback],
                forceWorkspaceScan ? "forced-workspace-fallback" : "workspace-fallback",
                forceWorkspaceScan
                    ? "RTK trigger forced primary project file fallback context."
                    : "Selected primary project file as minimal fallback context.");
    }

    public static IReadOnlyList<string> ExtractExplicitContextArgs(string prompt)
    {
        var args = new List<string>();
        foreach (Match match in Regex.Matches(
                     prompt,
                     @"(?<path>[\w./\\-]+\.(cs|ts|tsx|js|jsx|py|json|ya?ml|sql|md))(?::(?<line>\d+))?",
                     RegexOptions.IgnoreCase))
        {
            string path = match.Groups["path"].Value;
            string line = match.Groups["line"].Value;
            args.Add(string.IsNullOrWhiteSpace(line) ? path : $"{path}:{line}");
        }

        Match lineMatch = Regex.Match(prompt, @"\bline\s+(?<line>\d+)\b", RegexOptions.IgnoreCase);
        if (lineMatch.Success && args.All(arg => !arg.Contains(':', StringComparison.Ordinal)))
        {
            args.Add("--line");
            args.Add(lineMatch.Groups["line"].Value);
        }

        return args.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string[] ExtractKeywords(string prompt)
        => Regex.Matches(prompt.ToLowerInvariant(), @"[a-z][a-z0-9_]{2,}")
            .Select(match => match.Value)
            .Where(token => token is not "the" and not "and" and not "for" and not "this" and not "that" and not "with" and not "from" and not "how")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static int ScoreFile(string path, IReadOnlyCollection<string> keywords)
    {
        string fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        string pathText = path.ToLowerInvariant();
        int score = 0;
        foreach (string keyword in keywords)
        {
            if (fileName.Contains(keyword, StringComparison.Ordinal))
                score += 5;
            else if (pathText.Contains(keyword, StringComparison.Ordinal))
                score += 2;
        }

        return score;
    }

    private static bool IsCandidateFile(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (IgnoredPathParts.Any(part => fullPath.Contains(part, StringComparison.OrdinalIgnoreCase)))
            return false;

        return CandidateExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static string? FindPrimaryProjectFile(string workspaceRoot)
        => Directory.EnumerateFiles(workspaceRoot, "*.*", SearchOption.AllDirectories)
            .Where(IsCandidateFile)
            .OrderBy(path => PreferredFallbackRank(Path.GetFileName(path)))
            .ThenBy(path => path.Length)
            .FirstOrDefault();

    private static int PreferredFallbackRank(string fileName)
        => fileName.ToLowerInvariant() switch
        {
            "readme.md" => 0,
            var name when name.EndsWith(".sln", StringComparison.Ordinal) => 1,
            var name when name.EndsWith(".csproj", StringComparison.Ordinal) => 2,
            "program.cs" => 3,
            "function.cs" => 4,
            "hooks.json" => 5,
            "package.json" => 6,
            _ => 100,
        };
}

public sealed record ContextAnalysisResult(
    IReadOnlyList<string> ContextArgs,
    string Strategy,
    string Reason);
