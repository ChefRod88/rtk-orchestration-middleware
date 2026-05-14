using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace R2K.CLI;

public static class CursorSessionMeter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Record(CursorSessionEvent sessionEvent)
    {
        try
        {
            Directory.CreateDirectory(RtkDirectory);
            File.AppendAllText(
                SessionPath,
                JsonSerializer.Serialize(sessionEvent, JsonOptions) + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"RTK warning: could not record Cursor session metrics: {ex.Message}");
        }
    }

    public static CursorSessionSummary ReadSummary()
    {
        if (!File.Exists(SessionPath))
            return new CursorSessionSummary(0, 0, 0, 0);

        int events = 0;
        int original = 0;
        int optimized = 0;
        int saved = 0;

        foreach (string line in File.ReadLines(SessionPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var item = JsonSerializer.Deserialize<CursorSessionEvent>(line, JsonOptions);
                if (item is null)
                    continue;

                events++;
                original += item.ObservedOriginalTokens;
                optimized += item.ObservedOptimizedTokens;
                saved += item.ObservedTokensSaved;
            }
            catch (JsonException)
            {
                // Skip malformed historical rows rather than losing the whole report.
            }
        }

        return new CursorSessionSummary(events, original, optimized, saved);
    }

    public static void Reset()
    {
        if (File.Exists(SessionPath))
            File.Delete(SessionPath);
    }

    public static void PrintReport()
    {
        CursorSessionSummary summary = ReadSummary();
        decimal savingsPercent = summary.ObservedOriginalTokens > 0
            ? Math.Round(((decimal)summary.ObservedTokensSaved / summary.ObservedOriginalTokens) * 100, 2)
            : 0;

        Console.WriteLine();
        Console.WriteLine("======================================");
        Console.WriteLine("Observed Cursor Session Token Report");
        Console.WriteLine("--------------------------------------");
        Console.WriteLine($"Events observed by RTK: {summary.EventCount}");
        Console.WriteLine($"Observed original tokens: {summary.ObservedOriginalTokens}");
        Console.WriteLine($"Observed optimized tokens: {summary.ObservedOptimizedTokens}");
        Console.WriteLine($"Observed tokens saved: {summary.ObservedTokensSaved}");
        Console.WriteLine($"Observed savings: {savingsPercent.ToString("0.##", CultureInfo.InvariantCulture)}%");
        Console.WriteLine();
        Console.WriteLine("Scope note: this excludes Cursor hidden/system context unless Cursor exposes it to hooks.");
        Console.WriteLine("======================================");
        Console.WriteLine();
    }

    public static void RecordLatestPromptSavings(PromptSavingsSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(RtkDirectory);
            File.WriteAllText(
                LatestPromptSavingsPath,
                JsonSerializer.Serialize(snapshot, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"RTK warning: could not write latest prompt savings: {ex.Message}");
        }
    }

    public static void PrintLatestPromptSavings()
    {
        if (!File.Exists(LatestPromptSavingsPath))
        {
            Console.WriteLine("No RTK prompt savings have been recorded yet.");
            return;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<PromptSavingsSnapshot>(
                File.ReadAllText(LatestPromptSavingsPath),
                JsonOptions);
            if (snapshot is null)
            {
                Console.WriteLine("No RTK prompt savings have been recorded yet.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("Latest RTK Prompt Savings");
            Console.WriteLine("--------------------------------------");
            Console.WriteLine($"Status: {snapshot.Status}");
            Console.WriteLine($"Original context tokens: {snapshot.OriginalContextTokens}");
            Console.WriteLine($"Pruned context tokens: {snapshot.PrunedContextTokens}");
            Console.WriteLine($"Tokens saved: {snapshot.TokensSaved}");
            Console.WriteLine($"Savings: {snapshot.SavingsPercent.ToString("0.##", CultureInfo.InvariantCulture)}%");
            Console.WriteLine("======================================");
            Console.WriteLine();
        }
        catch (JsonException)
        {
            Console.WriteLine("RTK prompt savings file is not readable.");
        }
    }

    public static int EstimateTokens(string value)
        => (int)Math.Ceiling((value ?? string.Empty).Length / 4m);

    private static string RtkDirectory
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rtk");

    private static string SessionPath
        => Path.Combine(RtkDirectory, "cursor-session.jsonl");

    private static string LatestPromptSavingsPath
        => Path.Combine(RtkDirectory, "latest-prompt-savings.json");
}

public sealed record CursorSessionEvent(
    DateTimeOffset WrittenAtUtc,
    string EventType,
    string Status,
    string? Command,
    int ObservedOriginalTokens,
    int ObservedOptimizedTokens,
    int ObservedTokensSaved);

public sealed record CursorSessionSummary(
    int EventCount,
    int ObservedOriginalTokens,
    int ObservedOptimizedTokens,
    int ObservedTokensSaved);

public sealed record PromptSavingsSnapshot(
    DateTimeOffset WrittenAtUtc,
    string Status,
    int OriginalContextTokens,
    int PrunedContextTokens,
    int TokensSaved,
    decimal SavingsPercent);
