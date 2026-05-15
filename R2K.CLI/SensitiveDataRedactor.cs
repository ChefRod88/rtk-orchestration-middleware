using System.Text.RegularExpressions;

namespace R2K.CLI;

public static class SensitiveDataRedactor
{
    private static readonly (Regex Pattern, string Replacement)[] Rules =
    [
        (new Regex(@"(?i)(password\s*=\s*)[^;'\s]+", RegexOptions.Compiled), "$1[REDACTED]"),
        (new Regex(@"(?i)(api[_-]?key\s*[:=]\s*)['""]?[^'""\s,;]+", RegexOptions.Compiled), "$1[REDACTED]"),
        (new Regex(@"(?i)(secret\s*[:=]\s*)['""]?[^'""\s,;]+", RegexOptions.Compiled), "$1[REDACTED]"),
        (new Regex(@"(?i)(token\s*[:=]\s*)['""]?[^'""\s,;]+", RegexOptions.Compiled), "$1[REDACTED]"),
        (new Regex(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled), "[REDACTED_AWS_ACCESS_KEY]"),
        (new Regex(@"(?i)bearer\s+[a-z0-9._~+/=-]+", RegexOptions.Compiled), "Bearer [REDACTED]"),
        (new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled), "[REDACTED_SSN]"),
    ];

    public static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        string redacted = value;
        foreach (var (pattern, replacement) in Rules)
            redacted = pattern.Replace(redacted, replacement);

        return redacted;
    }
}
