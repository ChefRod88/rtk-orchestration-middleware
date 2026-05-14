using System.Text;
using System.Text.RegularExpressions;

namespace R2K.CLI;

public sealed class ContextPruner
{
    private static readonly Regex HeaderPattern = new(
        @"^\s*(using\s+[^;]+;|namespace\s+[\w.]+;?|#\s*(region|if|endif|nullable|pragma)\b.*|\[[\w.,\s()""=:<>-]+\])\s*$",
        RegexOptions.Compiled);

    private static readonly Regex TypeDeclarationPattern = new(
        @"^\s*(public|private|protected|internal)?\s*(static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+|ref\s+)*" +
        @"(class|interface|record|struct|enum)\s+[\w<>]+[^{;]*[{;]?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex MemberSignaturePattern = new(
        @"^\s*(public|private|protected|internal)\s+" +
        @"(static\s+|virtual\s+|override\s+|abstract\s+|async\s+|sealed\s+|extern\s+|partial\s+|readonly\s+|unsafe\s+)*" +
        @"[\w<>\[\],.?]+\s+" +
        @"[\w.<>]+\s*\([^;]*\)\s*(where\s+[^{}]+)?[{;]?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex PropertySignaturePattern = new(
        @"^\s*(public|private|protected|internal)\s+" +
        @"(static\s+|virtual\s+|override\s+|abstract\s+|sealed\s+|readonly\s+|required\s+|init\s+)*" +
        @"[\w<>\[\],.?]+\s+[\w.]+\s*[{;]",
        RegexOptions.Compiled);

    public string Prune(string filePath)
        => Prune(filePath, targetLine: null);

    public string Prune(string filePath, int? targetLine, int contextRadius = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string[] lines = File.ReadAllLines(filePath);
        var output = new StringBuilder();
        output.AppendLine($"// Pruned context for: {filePath}");

        if (targetLine is > 0)
            AppendTargetedWindow(output, lines, targetLine.Value, contextRadius);

        for (var i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            if (HeaderPattern.IsMatch(line) || TypeDeclarationPattern.IsMatch(line))
            {
                output.AppendLine(Normalize(line));
                continue;
            }

            if (IsMemberSignature(line))
            {
                AppendPrunedMember(output, line);
                if (line.Contains('{', StringComparison.Ordinal))
                    i = SkipBlock(lines, i);
            }
        }

        return output.ToString().TrimEnd();
    }

    private static bool IsMemberSignature(string line)
        => MemberSignaturePattern.IsMatch(line) || PropertySignaturePattern.IsMatch(line);

    private static void AppendPrunedMember(StringBuilder output, string signatureLine)
    {
        string normalized = Normalize(signatureLine);
        if (normalized.EndsWith(';'))
        {
            output.AppendLine(normalized);
            return;
        }

        string signature = normalized.TrimEnd('{').TrimEnd();
        string indent = signatureLine[..(signatureLine.Length - signatureLine.TrimStart().Length)];
        output.AppendLine($"{signature} {{");
        output.AppendLine($"{indent}    // ... [logic removed] ...");
        output.AppendLine($"{indent}}}");
    }

    private static int SkipBlock(string[] lines, int startIndex)
    {
        var depth = 0;
        var started = false;

        for (int i = startIndex; i < lines.Length; i++)
        {
            foreach (char c in lines[i])
            {
                if (c == '{')
                {
                    started = true;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (started && depth <= 0)
                        return i;
                }
            }
        }

        return startIndex;
    }

    private static void AppendTargetedWindow(
        StringBuilder output,
        string[] lines,
        int targetLine,
        int contextRadius)
    {
        int lineIndex = Math.Clamp(targetLine - 1, 0, Math.Max(0, lines.Length - 1));
        int start = Math.Max(0, lineIndex - contextRadius);
        int end = Math.Min(lines.Length - 1, lineIndex + contextRadius);

        output.AppendLine($"// Targeted context window around line {targetLine}:");
        for (int i = start; i <= end; i++)
            output.AppendLine($"// L{i + 1}: {lines[i].TrimEnd()}");

        output.AppendLine();
    }

    private static string Normalize(string line)
        => Regex.Replace(line.TrimEnd(), @"\s+", " ");
}
