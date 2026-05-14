using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Newtonsoft.Json.Linq;

// Override with env RTK_API_URL; Function auth: set RTK_FUNCTION_KEY (x-functions-key).
var apiUrl = Environment.GetEnvironmentVariable("RTK_API_URL")
    ?? "";
var promptApiUrl = Environment.GetEnvironmentVariable("RTK_PROMPT_API_URL")
    ?? DerivePromptApiUrl(apiUrl);

if (args.Length == 0) return;

if (string.Equals(args[0], "--optimize-prompt", StringComparison.Ordinal))
{
    await OptimizePrompt(args.Skip(1).ToArray(), promptApiUrl);
    return;
}

if (string.IsNullOrWhiteSpace(apiUrl))
{
    Console.Error.WriteLine("RTK_API_URL is not set. Run scripts/setup-r2k-codespace.sh or export RTK_API_URL.");
    Environment.ExitCode = 2;
    return;
}

string? shimCommand = GetShimCommandName();
if (shimCommand is not null && !IsInterceptorEnabled(shimCommand))
{
    await ExecuteRealCommand(shimCommand, args);
    return;
}

string fullCommand = string.Join(" ", shimCommand is null ? args : new[] { shimCommand }.Concat(args));

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

static async Task OptimizePrompt(string[] promptArgs, string promptApiUrl)
{
    if (string.IsNullOrWhiteSpace(promptApiUrl))
    {
        Console.Error.WriteLine("RTK_PROMPT_API_URL is not set and could not be derived from RTK_API_URL.");
        Environment.ExitCode = 2;
        return;
    }

    string prompt = promptArgs.Length > 0
        ? string.Join(" ", promptArgs)
        : await Console.In.ReadToEndAsync();

    if (string.IsNullOrWhiteSpace(prompt))
    {
        Console.Error.WriteLine("No prompt provided on argv or stdin.");
        Environment.ExitCode = 2;
        return;
    }

    using var client = new HttpClient();
    var functionKey = Environment.GetEnvironmentVariable("RTK_FUNCTION_KEY");
    if (!string.IsNullOrWhiteSpace(functionKey))
        client.DefaultRequestHeaders.Add("x-functions-key", functionKey.Trim());

    HttpResponseMessage response;
    try
    {
        response = await client.PostAsJsonAsync(promptApiUrl, new { prompt });
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"RTK prompt optimizer request failed: {ex.Message}");
        Environment.ExitCode = 2;
        return;
    }

    string responseBody = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"RTK prompt optimizer failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        if (!string.IsNullOrWhiteSpace(responseBody))
            Console.Error.WriteLine(responseBody);
        Environment.ExitCode = (int)response.StatusCode;
        return;
    }

    Console.WriteLine(responseBody);
}

static string? GetShimCommandName()
{
    string invokedAs = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);
    if (string.IsNullOrWhiteSpace(invokedAs)
        || string.Equals(invokedAs, "rtk", StringComparison.OrdinalIgnoreCase)
        || string.Equals(invokedAs, "R2K.CLI", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    return invokedAs;
}

static bool IsInterceptorEnabled(string command)
{
    string? configPath = FindHooksConfig();
    if (configPath is null)
        return true;

    try
    {
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        if (!document.RootElement.TryGetProperty("interceptors", out var interceptors)
            || interceptors.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        foreach (var interceptor in interceptors.EnumerateArray())
        {
            if (!interceptor.TryGetProperty("command", out var commandElement)
                || !string.Equals(commandElement.GetString(), command, StringComparison.Ordinal))
            {
                continue;
            }

            return !interceptor.TryGetProperty("enabled", out var enabledElement)
                || enabledElement.ValueKind != JsonValueKind.False;
        }
    }
    catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"RTK warning: could not read hooks config: {ex.Message}");
    }

    return true;
}

static string? FindHooksConfig()
{
    string? current = Directory.GetCurrentDirectory();
    while (!string.IsNullOrWhiteSpace(current))
    {
        string candidate = Path.Combine(current, ".r2k", "hooks.json");
        if (File.Exists(candidate))
            return candidate;

        current = Directory.GetParent(current)?.FullName;
    }

    string userConfig = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config",
        "r2k",
        "hooks.json");
    return File.Exists(userConfig) ? userConfig : null;
}

static async Task ExecuteRealCommand(string command, string[] args)
{
    var psi = new ProcessStartInfo
    {
        FileName = command,
        UseShellExecute = false
    };

    foreach (string arg in args)
        psi.ArgumentList.Add(arg);

    string shimDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local",
        "share",
        "r2k",
        "shims");
    string path = Environment.GetEnvironmentVariable("PATH") ?? "";
    string filteredPath = string.Join(
        Path.PathSeparator,
        path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !string.Equals(part, shimDir, StringComparison.Ordinal)));
    psi.Environment["PATH"] = filteredPath;

    using var process = Process.Start(psi);
    if (process is null)
    {
        Environment.ExitCode = 127;
        return;
    }

    await process.WaitForExitAsync();
    Environment.ExitCode = process.ExitCode;
}

static string DerivePromptApiUrl(string commandApiUrl)
{
    if (string.IsNullOrWhiteSpace(commandApiUrl))
        return string.Empty;

    return commandApiUrl.EndsWith("OptimizeCommand", StringComparison.OrdinalIgnoreCase)
        ? string.Concat(commandApiUrl.AsSpan(0, commandApiUrl.Length - "OptimizeCommand".Length), "OptimizePrompt")
        : commandApiUrl.TrimEnd('/') + "/OptimizePrompt";
}

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
