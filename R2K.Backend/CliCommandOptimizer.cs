namespace R2K.Backend;

/// <summary>Flag-aware rewrite: whitespace normalization, stripping of duplicate consecutive flags,</summary>
public sealed class CliCommandOptimizer : ICommandOptimizer
{
    public string Optimize(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return string.Empty;

        var collapsedSpaces = NormalizeWhitespace(command.Trim());

        var tokens = TokenizeRespectingQuotes(collapsedSpaces).ToArray();
        if (tokens.Length == 0)
            return string.Empty;

        var filtered = DedupeAdjacentFlags(tokens);
        return string.Join(' ', filtered);
    }

    internal static string NormalizeWhitespace(string s)
    {
        Span<char> buffer = stackalloc char[s.Length];
        var w = 0;
        var inSingle = false;
        var inDouble = false;

        foreach (var c in s)
        {
            if (c == '\'' && !inDouble)
                inSingle = !inSingle;
            else if (c == '"' && !inSingle)
                inDouble = !inDouble;

            var isWs = char.IsWhiteSpace(c) && !inSingle && !inDouble;

            if (isWs)
            {
                if (w == 0 || buffer[w - 1] == ' ')
                    continue;
                buffer[w++] = ' ';
            }
            else
                buffer[w++] = c;
        }

        while (w > 0 && buffer[w - 1] == ' ')
            w--;

        return w <= 0 ? string.Empty : new string(buffer[..w]);
    }

    internal static List<string> TokenizeRespectingQuotes(string s)
    {
        var result = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
            if (i >= s.Length)
                break;

            var c = s[i];
            if (c is '\'' or '"')
            {
                var quote = c;
                i++;
                var start = i;
                while (i < s.Length && s[i] != quote)
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                        i += 2;
                    else
                        i++;
                }
                var inner = start <= i ? s[start..i] : string.Empty;
                if (i < s.Length && s[i] == quote)
                    i++;
                result.Add($"{quote}{inner}{quote}");
            }
            else
            {
                var start = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]))
                    i++;
                result.Add(s[start..i]);
            }
        }
        return result;
    }

    internal static IEnumerable<string> DedupeAdjacentFlags(IReadOnlyList<string> tokens)
    {
        string? prev = null;
        foreach (var tok in tokens)
        {
            if (IsFlag(tok) && prev != null && string.Equals(tok, prev, StringComparison.Ordinal))
                continue;

            yield return tok;
            prev = tok;
        }
    }

    private static bool IsFlag(string tok)
        => tok.Length switch
        {
            0 => false,
            >= 2 when tok[0] == '-' && tok[1] != '-' => true,
            >= 3 when tok.StartsWith("--", StringComparison.Ordinal) => true,
            _ => false
        };
}
