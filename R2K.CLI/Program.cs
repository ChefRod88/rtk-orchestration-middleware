using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using Newtonsoft.Json.Linq;

// Override with env RTK_API_URL; Function auth: set RTK_FUNCTION_KEY (x-functions-key).
var apiUrl = Environment.GetEnvironmentVariable("RTK_API_URL")
    ?? "";

if (args.Length == 0) return;
if (string.IsNullOrWhiteSpace(apiUrl))
{
    Console.Error.WriteLine("RTK_API_URL is not set. Run scripts/setup-r2k-codespace.sh or export RTK_API_URL.");
    Environment.ExitCode = 2;
    return;
}

string fullCommand = string.Join(" ", args);

using var client = new HttpClient();
var functionKey = Environment.GetEnvironmentVariable("RTK_FUNCTION_KEY");
if (!string.IsNullOrWhiteSpace(functionKey))
    client.DefaultRequestHeaders.Add("x-functions-key", functionKey.Trim());

HttpResponseMessage response;
try
{
    response = await client.PostAsJsonAsync(apiUrl, new { command = fullCommand });
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"RTK optimizer request failed: {ex.Message}");
    Environment.ExitCode = 2;
    return;
}
if (!response.IsSuccessStatusCode)
{
    var problem = await response.Content.ReadAsStringAsync();
    Console.Error.WriteLine($"RTK optimizer failed: {(int)response.StatusCode} {response.ReasonPhrase}");
    if (!string.IsNullOrWhiteSpace(problem))
        Console.Error.WriteLine(problem);
    Environment.ExitCode = (int)response.StatusCode;
    return;
}

var result = JObject.Parse(await response.Content.ReadAsStringAsync());

string optimized = result["command_executed"]!.ToString();

var psi = new ProcessStartInfo {
    FileName = "/bin/bash",
    UseShellExecute = false
};
psi.ArgumentList.Add("-c");
psi.ArgumentList.Add(optimized);

using var process = Process.Start(psi);

process?.WaitForExit();

PrintSavings(result["metrics"]);

if (process is not null)
    Environment.ExitCode = process.ExitCode;

static void PrintSavings(JToken? metrics)
{
    if (metrics is null)
        return;

    var original = ReadInt(metrics, "tokens_original");
    var optimized = ReadInt(metrics, "tokens_optimized");
    var saved = original.HasValue && optimized.HasValue
        ? Math.Max(0, original.Value - optimized.Value)
        : (int?)null;
    var percent = ReadDecimal(metrics, "savings_percentage");
    var sessionTotal = ReadInt(metrics, "total_session_savings");

    Console.WriteLine();
    Console.WriteLine("======================================");
    Console.WriteLine("RTK SAVINGS");
    if (original.HasValue)
        Console.WriteLine($"Original tokens: {original.Value}");
    if (optimized.HasValue)
        Console.WriteLine($"Optimized tokens: {optimized.Value}");
    if (saved.HasValue)
        Console.WriteLine($"Tokens saved: {saved.Value}");
    if (percent.HasValue)
        Console.WriteLine($"Savings: {percent.Value.ToString("0.##", CultureInfo.InvariantCulture)}%");
    if (sessionTotal.HasValue)
        Console.WriteLine($"Session total saved: {sessionTotal.Value} tokens");
    Console.WriteLine("======================================");
}

static int? ReadInt(JToken token, string name)
{
    var value = token[name];
    return value?.Type is JTokenType.Integer or JTokenType.Float
        ? value.Value<int>()
        : null;
}

static decimal? ReadDecimal(JToken token, string name)
{
    var value = token[name];
    return value?.Type is JTokenType.Integer or JTokenType.Float
        ? value.Value<decimal>()
        : null;
}
