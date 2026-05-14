using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Dapper;
using MySqlConnector;

namespace R2K.Backend;

public sealed class R2KOptimizer(
    ILogger<R2KOptimizer> logger,
    ICommandOptimizationService optimizationService,
    IPromptOptimizationService promptOptimizationService)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ResponseJson = new()
    {
        PropertyNamingPolicy = null,
    };

    [Function(nameof(OptimizeCommand))]
    public async Task<HttpResponseData> OptimizeCommand(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        string body = await new StreamReader(req.Body).ReadToEndAsync();
        OptimizeRequest? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OptimizeRequest>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid JSON payload");
            return await Bad(req, HttpStatusCode.BadRequest, "{\"error\":\"Invalid JSON payload\"}");
        }

        string? rawCommand = payload?.Command;
        if (rawCommand is null)
            return await Bad(req, HttpStatusCode.BadRequest, "{\"error\":\"command is required\"}");

        OptimizationMetrics metrics = optimizationService.Compute(rawCommand);
        int totalSessionTokenSavings;
        try
        {
            totalSessionTokenSavings = await PersistTelemetry(
                req,
                "command",
                rawCommand,
                metrics.TokensOriginal,
                metrics.TokensOptimized,
                metrics.EfficiencyPercent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Telemetry persistence failed");
            return await Bad(req, HttpStatusCode.InternalServerError, "{\"error\":\"Database operation failed\"}");
        }

        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=utf-8");
        byte[] okBody = JsonSerializer.SerializeToUtf8Bytes(new
        {
            command_executed = metrics.OptimizedCommand,
            metrics = new
            {
                tokens_original = metrics.TokensOriginal,
                tokens_optimized = metrics.TokensOptimized,
                savings_percentage = metrics.EfficiencyPercent,
                total_session_savings = totalSessionTokenSavings,
            },
        }, ResponseJson);
        await response.Body.WriteAsync(okBody, req.FunctionContext.CancellationToken);

        return response;
    }

    [Function(nameof(OptimizePrompt))]
    public async Task<HttpResponseData> OptimizePrompt(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        string body = await new StreamReader(req.Body).ReadToEndAsync();
        OptimizePromptRequest? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OptimizePromptRequest>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid JSON payload");
            return await Bad(req, HttpStatusCode.BadRequest, "{\"error\":\"Invalid JSON payload\"}");
        }

        string? rawPrompt = payload?.Prompt;
        if (rawPrompt is null)
            return await Bad(req, HttpStatusCode.BadRequest, "{\"error\":\"prompt is required\"}");

        PromptOptimizationMetrics metrics = promptOptimizationService.Compute(rawPrompt);
        int totalSessionTokenSavings;
        try
        {
            totalSessionTokenSavings = await PersistTelemetry(
                req,
                "prompt",
                rawPrompt,
                metrics.TokensOriginal,
                metrics.TokensOptimized,
                metrics.EfficiencyPercent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Prompt telemetry persistence failed");
            return await Bad(req, HttpStatusCode.InternalServerError, "{\"error\":\"Database operation failed\"}");
        }

        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=utf-8");
        byte[] okBody = JsonSerializer.SerializeToUtf8Bytes(new
        {
            optimized_prompt = metrics.OptimizedPrompt,
            metrics = new
            {
                tokens_original = metrics.TokensOriginal,
                tokens_optimized = metrics.TokensOptimized,
                tokens_saved = metrics.TokensSaved,
                savings_percentage = metrics.EfficiencyPercent,
                total_session_savings = totalSessionTokenSavings,
            },
        }, ResponseJson);
        await response.Body.WriteAsync(okBody, req.FunctionContext.CancellationToken);

        return response;
    }

    private static async Task<int> PersistTelemetry(
        HttpRequestData req,
        string kind,
        string text,
        int originalTokens,
        int optimizedTokens,
        decimal savingsPercent)
    {
        string? connString = MySqlTelemetryConnection.BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connString))
            return 0;

        await using var conn = new MySqlConnection(connString);
        await conn.OpenAsync(req.FunctionContext.CancellationToken);
        try
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO TokenLogs (Kind, Command, OriginalTokens, OptimizedTokens, SavingsPercent, Timestamp)
                VALUES (@kind, @cmd, @orig, @opt, @perc, UTC_TIMESTAMP(3))
                """,
                new
                {
                    kind,
                    cmd = text,
                    orig = originalTokens,
                    opt = optimizedTokens,
                    perc = savingsPercent,
                });
        }
        catch (MySqlException ex) when (ex.Number == 1054)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO TokenLogs (Command, OriginalTokens, OptimizedTokens, SavingsPercent, Timestamp)
                VALUES (@cmd, @orig, @opt, @perc, UTC_TIMESTAMP(3))
                """,
                new
                {
                    cmd = text,
                    orig = originalTokens,
                    opt = optimizedTokens,
                    perc = savingsPercent,
                });
        }

        return await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(SUM(OriginalTokens - OptimizedTokens), 0) FROM TokenLogs");
    }

    private static async Task<HttpResponseData> Bad(HttpRequestData req, HttpStatusCode code, string json)
    {
        HttpResponseData res = req.CreateResponse(code);
        res.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=utf-8");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        await res.Body.WriteAsync(body);
        return res;
    }

    private sealed record OptimizeRequest(
        [property: JsonPropertyName("command")] string? Command);

    private sealed record OptimizePromptRequest(
        [property: JsonPropertyName("prompt")] string? Prompt);
}
