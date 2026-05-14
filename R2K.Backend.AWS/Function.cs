using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using MySqlConnector;
using Dapper;
using Newtonsoft.Json;
using Tiktoken;

// This attribute tells Lambda to use System.Text.Json for serialization
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace R2K.Backend.AWS;

public class Function
{
    private static readonly Encoder Encoder = TikTokenEncoder.CreateForModel(Models.Gpt4o);

    /// <summary>
    /// Handles the POST request from your CLI tool, optimizes the command,
    /// and logs telemetry to your AWS RDS MySQL instance.
    /// </summary>
    public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        try
        {
            return IsPromptRequest(request)
                ? await OptimizePrompt(request, context)
                : await OptimizeCommand(request, context);
        }
        catch (Exception ex)
        {
            context.Logger.LogLine($"Critical Error: {ex.Message}");
            return new APIGatewayProxyResponse { StatusCode = 500, Body = "Internal Server Error" };
        }
    }

    private static async Task<APIGatewayProxyResponse> OptimizeCommand(
        APIGatewayProxyRequest request,
        ILambdaContext context)
    {
        var data = JsonConvert.DeserializeObject<dynamic>(request.Body);
        string rawCommand = data?.command ?? "";

        if (string.IsNullOrEmpty(rawCommand))
            return JsonResponse(400, new { error = "command is required" });

        string optimizedCommand = CliCommandOptimizer.Optimize(rawCommand);
        var metrics = CalculateMetrics(rawCommand, optimizedCommand);
        int totalSessionSavings = await PersistTelemetry("command", rawCommand, metrics, context);

        return JsonResponse(200, new
        {
            command_executed = optimizedCommand,
            metrics = new
            {
                tokens_original = metrics.OriginalTokens,
                tokens_optimized = metrics.OptimizedTokens,
                tokens_saved = metrics.TokensSaved,
                savings_percentage = metrics.SavingsPercent,
                total_session_savings = totalSessionSavings
            }
        });
    }

    private static async Task<APIGatewayProxyResponse> OptimizePrompt(
        APIGatewayProxyRequest request,
        ILambdaContext context)
    {
        var data = JsonConvert.DeserializeObject<dynamic>(request.Body);
        string rawPrompt = data?.prompt ?? "";

        if (rawPrompt.Length == 0)
            return JsonResponse(400, new { error = "prompt is required" });

        string optimizedPrompt = PromptTextOptimizer.Optimize(rawPrompt);
        var metrics = CalculateMetrics(rawPrompt, optimizedPrompt);
        int totalSessionSavings = await PersistTelemetry("prompt", rawPrompt, metrics, context);

        return JsonResponse(200, new
        {
            optimized_prompt = optimizedPrompt,
            metrics = new
            {
                tokens_original = metrics.OriginalTokens,
                tokens_optimized = metrics.OptimizedTokens,
                tokens_saved = metrics.TokensSaved,
                savings_percentage = metrics.SavingsPercent,
                total_session_savings = totalSessionSavings
            }
        });
    }

    private static bool IsPromptRequest(APIGatewayProxyRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Path)
            && request.Path.EndsWith("/OptimizePrompt", StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(request.Resource)
            && request.Resource.EndsWith("/OptimizePrompt", StringComparison.OrdinalIgnoreCase);
    }

    private static OptimizationMetrics CalculateMetrics(string raw, string optimized)
    {
        int originalTokens = Encoder.CountTokens(raw);
        int optimizedTokens = Encoder.CountTokens(optimized);
        int tokensSaved = Math.Max(0, originalTokens - optimizedTokens);
        decimal savings = originalTokens > 0
            ? Math.Round(((decimal)tokensSaved / originalTokens) * 100, 2)
            : 0;

        return new OptimizationMetrics(originalTokens, optimizedTokens, tokensSaved, savings);
    }

    private static async Task<int> PersistTelemetry(
        string kind,
        string rawText,
        OptimizationMetrics metrics,
        ILambdaContext context)
    {
        string? connString = Environment.GetEnvironmentVariable("SqlConnectionString");
        if (string.IsNullOrWhiteSpace(connString))
        {
            context.Logger.LogLine("Missing SqlConnectionString environment variable.");
            return 0;
        }

        await using var conn = new MySqlConnection(connString);

        try
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO TokenLogs (Kind, Command, OriginalTokens, OptimizedTokens, SavingsPercent)
                VALUES (@kind, @cmd, @orig, @opt, @perc)
                """,
                new
                {
                    kind,
                    cmd = rawText,
                    orig = metrics.OriginalTokens,
                    opt = metrics.OptimizedTokens,
                    perc = metrics.SavingsPercent
                });
        }
        catch (MySqlException ex) when (ex.Number == 1054)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO TokenLogs (Command, OriginalTokens, OptimizedTokens, SavingsPercent)
                VALUES (@cmd, @orig, @opt, @perc)
                """,
                new
                {
                    cmd = rawText,
                    orig = metrics.OriginalTokens,
                    opt = metrics.OptimizedTokens,
                    perc = metrics.SavingsPercent
                });
        }

        return await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(SUM(OriginalTokens - OptimizedTokens), 0) FROM TokenLogs");
    }

    private static APIGatewayProxyResponse JsonResponse(int statusCode, object body)
        => new()
        {
            StatusCode = statusCode,
            Body = JsonConvert.SerializeObject(body),
            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
        };
}

internal sealed record OptimizationMetrics(
    int OriginalTokens,
    int OptimizedTokens,
    int TokensSaved,
    decimal SavingsPercent);

internal static class CliCommandOptimizer
{
    internal static string Optimize(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return string.Empty;

        var tokens = NormalizeWhitespace(command.Trim()).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filtered = new List<string>(tokens.Length);
        string? previous = null;
        foreach (string token in tokens)
        {
            if (IsFlag(token) && string.Equals(token, previous, StringComparison.Ordinal))
                continue;

            filtered.Add(token);
            previous = token;
        }

        return string.Join(' ', filtered);
    }

    private static string NormalizeWhitespace(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var w = 0;
        var previousWasSpace = false;

        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c))
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

    private static bool IsFlag(string token)
        => token.Length switch
        {
            0 => false,
            >= 2 when token[0] == '-' && token[1] != '-' => true,
            >= 3 when token.StartsWith("--", StringComparison.Ordinal) => true,
            _ => false
        };
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
