using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Dapper;

namespace R2K.Backend;

public sealed class R2KOptimizer(
    ILogger<R2KOptimizer> logger,
    ICommandOptimizationService optimizationService)
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

        int totalSessionTokenSavings = 0;

        string? connString = Environment.GetEnvironmentVariable("SqlConnectionString");
        if (!string.IsNullOrWhiteSpace(connString))
        {
            try
            {
                await using var conn = new SqlConnection(connString);
                await conn.OpenAsync(req.FunctionContext.CancellationToken);
                await conn.ExecuteAsync(
                    "INSERT INTO TokenLogs (Command, OriginalTokens, OptimizedTokens, SavingsPercent) VALUES (@c, @orig, @opt, @p)",
                    new
                    {
                        c = rawCommand,
                        orig = metrics.TokensOriginal,
                        opt = metrics.TokensOptimized,
                        p = metrics.EfficiencyPercent,
                    });
                totalSessionTokenSavings = await conn.ExecuteScalarAsync<int>(
                    "SELECT COALESCE(SUM(OriginalTokens - OptimizedTokens), 0) FROM TokenLogs");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Telemetry persistence failed");
                return await Bad(req, HttpStatusCode.InternalServerError, "{\"error\":\"Database operation failed\"}");
            }
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
}
