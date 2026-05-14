using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Linq;

namespace R2K.CLI;

public sealed class AwsLambdaClient(HttpClient httpClient)
{
    public async Task<JObject> OptimizeAsync(
        string endpoint,
        string command,
        ContextPruningResult context,
        string? functionKey,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            endpoint)
        {
            Content = JsonContent.Create(CreatePayload(command, context)),
        };

        if (!string.IsNullOrWhiteSpace(functionKey))
            request.Headers.TryAddWithoutValidation("x-functions-key", functionKey.Trim());

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new AwsLambdaRequestException(response.StatusCode, response.ReasonPhrase, responseBody);

        return JObject.Parse(responseBody);
    }

    public static AwsLambdaPayload CreatePayload(string command, ContextPruningResult context)
        => new(
            command,
            context.PrunedContext,
            context.OriginalTokenCount,
            context.PrunedTokenCount);

    public static void PrintOptimizedOutput(JObject response, ContextPruningResult context)
    {
        string optimizedCommand = response["command_executed"]?.ToString()
            ?? response["optimized_output"]?.ToString()
            ?? "(no optimized output returned)";
        JToken? metrics = response["metrics"];

        Console.WriteLine();
        Console.WriteLine("======================================");
        Console.WriteLine("RTK AWS LAMBDA OPTIMIZATION");
        Console.WriteLine("--------------------------------------");
        Console.WriteLine($"Optimized output: {optimizedCommand}");
        Console.WriteLine();
        Console.WriteLine("Telemetry");
        Console.WriteLine($"Context files: {context.Files.Count}");
        Console.WriteLine($"Original context tokens: {context.OriginalTokenCount}");
        Console.WriteLine($"Pruned context tokens: {context.PrunedTokenCount}");
        Console.WriteLine($"Context tokens saved: {Math.Max(0, context.OriginalTokenCount - context.PrunedTokenCount)}");

        if (metrics is not null)
        {
            PrintMetric(metrics, "tokens_original", "Command tokens original");
            PrintMetric(metrics, "tokens_optimized", "Command tokens optimized");
            PrintMetric(metrics, "tokens_saved", "Command tokens saved");
            PrintMetric(metrics, "savings_percentage", "Command savings");
            PrintMetric(metrics, "total_session_savings", "Session total savings");
        }

        Console.WriteLine("======================================");
        Console.WriteLine();
    }

    private static void PrintMetric(JToken metrics, string name, string label)
    {
        JToken? value = metrics[name];
        if (value is null)
            return;

        string rendered = value.Type == JTokenType.Float
            ? value.Value<decimal>().ToString("0.##", CultureInfo.InvariantCulture)
            : value.ToString();
        string suffix = name.Contains("percentage", StringComparison.OrdinalIgnoreCase) ? "%" : "";
        Console.WriteLine($"{label}: {rendered}{suffix}");
    }
}

public sealed record AwsLambdaPayload(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("pruned_context")] string PrunedContext,
    [property: JsonPropertyName("original_token_count")] int OriginalTokenCount,
    [property: JsonPropertyName("pruned_token_count")] int PrunedTokenCount);

public sealed class AwsLambdaRequestException(
    HttpStatusCode statusCode,
    string? reasonPhrase,
    string responseBody) : Exception($"AWS Lambda request failed: {(int)statusCode} {reasonPhrase}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? ReasonPhrase { get; } = reasonPhrase;
    public string ResponseBody { get; } = responseBody;
}
