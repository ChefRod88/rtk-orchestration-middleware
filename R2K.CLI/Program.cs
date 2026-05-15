using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using R2K.CLI;

// Override with env RTK_API_URL; Function auth: set RTK_FUNCTION_KEY (x-functions-key).
var apiUrl = Environment.GetEnvironmentVariable("RTK_API_URL")
    ?? "";

if (args.Length == 0) return;

var hookRegistry = HookRegistry.LoadFromDefaultLocations();
if (string.IsNullOrWhiteSpace(apiUrl))
    apiUrl = hookRegistry.Settings.TelemetryEndpoint ?? "";

var promptApiUrl = Environment.GetEnvironmentVariable("RTK_PROMPT_API_URL")
    ?? DerivePromptApiUrl(apiUrl);

if (string.Equals(args[0], "--cursor-session-report", StringComparison.Ordinal))
{
    CursorSessionMeter.PrintReport();
    return;
}

if (string.Equals(args[0], "--last-prompt-savings", StringComparison.Ordinal))
{
    CursorSessionMeter.PrintLatestPromptSavings();
    return;
}

if (string.Equals(args[0], "--cursor-session-reset", StringComparison.Ordinal))
{
    CursorSessionMeter.Reset();
    Console.WriteLine("Observed Cursor session metrics reset.");
    return;
}

if (string.Equals(args[0], "--optimize-prompt", StringComparison.Ordinal))
{
    await OptimizePrompt(args.Skip(1).ToArray(), promptApiUrl);
    return;
}

if (string.Equals(args[0], "--orchestrate-prompt", StringComparison.Ordinal))
{
    await OrchestratePrompt(args.Skip(1).ToArray(), apiUrl, hookRegistry);
    return;
}

string? shimCommand = GetShimCommandName();
string commandName = shimCommand ?? args[0];
string[] rawCommandArgs = shimCommand is null ? args.Skip(1).ToArray() : args;
bool dryRun = rawCommandArgs.Any(arg => string.Equals(arg, "--dry-run", StringComparison.Ordinal));
string[] commandArgs = rawCommandArgs
    .Where(arg => !string.Equals(arg, "--dry-run", StringComparison.Ordinal))
    .ToArray();
HookDefinition? hook = hookRegistry.GetHook(commandName);
if (hook is null)
{
    await ExecuteRealCommand(commandName, commandArgs);
    return;
}

var contextPruning = new ContextPruningEngine(new ContextPruner())
    .Prune(commandArgs, hook.PruningStrategy);

if (dryRun)
{
    PrintDryRunEstimate(commandName, hook.PruningStrategy, contextPruning);
    return;
}

if (string.IsNullOrWhiteSpace(apiUrl))
{
    Console.Error.WriteLine("RTK_API_URL is not set. Run scripts/setup-r2k-codespace.sh or export RTK_API_URL.");
    Environment.ExitCode = 2;
    return;
}

string fullCommand = string.Join(" ", shimCommand is null ? args : new[] { shimCommand }.Concat(args));

using var client = new HttpClient();
var functionKey = Environment.GetEnvironmentVariable("RTK_FUNCTION_KEY");

JObject result;
try
{
    result = await new AwsLambdaClient(client)
        .OptimizeAsync(apiUrl, fullCommand, contextPruning, hook.PruningStrategy, functionKey);
}
catch (AwsLambdaRequestException ex)
{
    Console.Error.WriteLine($"RTK optimizer failed: {(int)ex.StatusCode} {ex.ReasonPhrase}");
    if (!string.IsNullOrWhiteSpace(ex.ResponseBody))
        Console.Error.WriteLine(ex.ResponseBody);
    Environment.ExitCode = (int)ex.StatusCode;
    return;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"RTK optimizer request failed: {ex.Message}");
    Environment.ExitCode = 2;
    return;
}

AwsLambdaClient.PrintOptimizedOutput(result, contextPruning);

string optimized = result["command_executed"]!.ToString();

var psi = new ProcessStartInfo {
    FileName = "/bin/bash",
    UseShellExecute = false
};
psi.ArgumentList.Add("-c");
psi.ArgumentList.Add(optimized);

using var process = Process.Start(psi);

process?.WaitForExit();

int? exitCode = process?.ExitCode;
RecordLastMetrics(result["metrics"], optimized, exitCode, contextPruning);
if (ShouldPrintSavingsToTerminal())
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

static async Task OrchestratePrompt(
    string[] promptArgs,
    string apiUrl,
    HookRegistry hookRegistry)
{
    bool dryRun = promptArgs.Any(arg => string.Equals(arg, "--dry-run", StringComparison.Ordinal));
    string[] filteredArgs = promptArgs
        .Where(arg => !string.Equals(arg, "--dry-run", StringComparison.Ordinal))
        .ToArray();
    string prompt = filteredArgs.Length > 0
        ? string.Join(" ", filteredArgs)
        : await Console.In.ReadToEndAsync();

    if (prompt.Contains("--bypass-rtk", StringComparison.OrdinalIgnoreCase))
    {
        WriteBypassPromptJson(prompt);
        return;
    }

    HookDefinition? hook = hookRegistry.GetHook("cursor");
    PruningStrategy strategy = hook?.PruningStrategy ?? PruningStrategy.Agentic;
    ContextAnalysisResult analysis = new ContextAnalyzer().Analyze(prompt, Directory.GetCurrentDirectory());
    string[] contextArgs = analysis.ContextArgs.ToArray();
    var contextPruning = new ContextPruningEngine(new ContextPruner())
        .Prune(contextArgs, strategy);
    int promptTokens = CursorSessionMeter.EstimateTokens(prompt);
    int hookObservedTokens = int.TryParse(
        Environment.GetEnvironmentVariable("RTK_CURSOR_HOOK_TOKEN_ESTIMATE"),
        out int parsedHookTokens)
        ? parsedHookTokens
        : 0;
    int rawPayloadTokens = Math.Max(
        hookObservedTokens,
        promptTokens + contextPruning.OriginalTokenCount);

    if (dryRun || string.IsNullOrWhiteSpace(apiUrl))
    {
        string status = dryRun ? "dry_run" : "missing_endpoint";
        RecordPromptSessionEvent(rawPayloadTokens, promptTokens, contextPruning, status);
        WritePromptOrchestrationJson(prompt, contextPruning, status, analysis, null);
        return;
    }

    try
    {
        using var client = new HttpClient();
        var result = await new AwsLambdaClient(client).OptimizeAsync(
            apiUrl,
            "cursor prompt",
            contextPruning,
            strategy,
            Environment.GetEnvironmentVariable("RTK_FUNCTION_KEY"));
        RecordPromptSessionEvent(rawPayloadTokens, promptTokens, contextPruning, "sent");
        WritePromptOrchestrationJson(prompt, contextPruning, "sent", analysis, result["metrics"]);
    }
    catch (Exception ex) when (ex is HttpRequestException or AwsLambdaRequestException)
    {
        RecordPromptSessionEvent(rawPayloadTokens, promptTokens, contextPruning, "failed_open");
        WritePromptOrchestrationJson(prompt, contextPruning, "failed_open", analysis, null);
    }
}

static void WriteBypassPromptJson(string prompt)
{
    var payload = new
    {
        optimized_prompt = prompt.Replace("--bypass-rtk", "", StringComparison.OrdinalIgnoreCase).Trim(),
        orchestration_status = "bypassed",
        metrics = new
        {
            tokens_original = CursorSessionMeter.EstimateTokens(prompt),
            tokens_optimized = CursorSessionMeter.EstimateTokens(prompt),
            tokens_saved = 0,
            savings_percentage = 0,
        },
    };
    Console.WriteLine(JsonSerializer.Serialize(payload));
}

static void RecordPromptSessionEvent(
    int rawPayloadTokens,
    int promptTokens,
    ContextPruningResult contextPruning,
    string status)
{
    int original = rawPayloadTokens;
    int optimized = promptTokens + contextPruning.PrunedTokenCount;
    int saved = Math.Max(0, original - optimized);
    CursorSessionMeter.Record(new CursorSessionEvent(
        DateTimeOffset.UtcNow,
        "cursor_prompt",
        status,
        "cursor prompt",
        original,
        optimized,
        saved));
}

static void WritePromptOrchestrationJson(
    string prompt,
    ContextPruningResult contextPruning,
    string status,
    ContextAnalysisResult analysis,
    JToken? lambdaMetrics)
{
    int promptTokens = CursorSessionMeter.EstimateTokens(prompt);
    int rawPayloadTokens = promptTokens + contextPruning.OriginalTokenCount;
    int prunedPayloadTokens = promptTokens + contextPruning.PrunedTokenCount;
    int saved = Math.Max(0, rawPayloadTokens - prunedPayloadTokens);
    decimal savingsPercent = rawPayloadTokens > 0
        ? Math.Round(((decimal)saved / rawPayloadTokens) * 100, 2)
        : 0;
    CursorSessionMeter.RecordLatestPromptSavings(new PromptSavingsSnapshot(
        DateTimeOffset.UtcNow,
        status,
        rawPayloadTokens,
        prunedPayloadTokens,
        saved,
        savingsPercent));
    var payload = new
    {
        optimized_prompt = BuildOptimizedPrompt(prompt, contextPruning, saved, savingsPercent),
        orchestration_status = status,
        context_discovery = new
        {
            strategy = analysis.Strategy,
            reason = analysis.Reason,
            files = contextPruning.Files,
        },
        metrics = new
        {
            tokens_original = contextPruning.OriginalTokenCount,
            tokens_optimized = contextPruning.PrunedTokenCount,
            tokens_saved = saved,
            savings_percentage = savingsPercent,
            raw_payload_tokens = rawPayloadTokens,
            pruned_payload_tokens = prunedPayloadTokens,
            lambda = lambdaMetrics,
        },
    };
    Console.WriteLine(JsonSerializer.Serialize(payload));
}

static string BuildOptimizedPrompt(
    string prompt,
    ContextPruningResult contextPruning,
    int saved,
    decimal savingsPercent)
{
    if (saved <= 0 || string.IsNullOrWhiteSpace(contextPruning.PrunedContext))
        return prompt;

    return $"""
    {prompt.Trim()}

    Use this RTK-pruned context instead of broad workspace context:

    ```text
    {contextPruning.PrunedContext.Trim()}
    ```

    After you complete the answer, append this exact footer:

    RTK Savings:
    - Original context tokens: {contextPruning.OriginalTokenCount}
    - Pruned context tokens: {contextPruning.PrunedTokenCount}
    - Tokens saved: {saved}
    - Savings: {savingsPercent.ToString("0.##", CultureInfo.InvariantCulture)}%
    """;
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

static bool ShouldPrintSavingsToTerminal()
{
    string? v = Environment.GetEnvironmentVariable("RTK_PRINT_SAVINGS");
    return string.Equals(v, "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase);
}

static void PrintDryRunEstimate(
    string commandName,
    PruningStrategy strategy,
    ContextPruningResult contextPruning)
{
    int saved = Math.Max(0, contextPruning.OriginalTokenCount - contextPruning.PrunedTokenCount);
    decimal savingsPercent = contextPruning.OriginalTokenCount > 0
        ? Math.Round(((decimal)saved / contextPruning.OriginalTokenCount) * 100, 2)
        : 0;

    Console.WriteLine();
    Console.WriteLine("======================================");
    Console.WriteLine("Mission 2026 Token Savings Estimate");
    Console.WriteLine("--------------------------------------");
    Console.WriteLine($"Command: {commandName}");
    Console.WriteLine($"Pruning strategy: {strategy.ToString().ToLowerInvariant()}");
    Console.WriteLine($"Context files: {contextPruning.Files.Count}");
    Console.WriteLine($"Original context tokens: {contextPruning.OriginalTokenCount}");
    Console.WriteLine($"Pruned context tokens: {contextPruning.PrunedTokenCount}");
    Console.WriteLine($"Estimated tokens saved: {saved}");
    Console.WriteLine($"Estimated savings: {savingsPercent.ToString("0.##", CultureInfo.InvariantCulture)}%");
    Console.WriteLine();
    Console.WriteLine("Dry run only: skipped AWS Lambda request and command execution.");
    Console.WriteLine("======================================");
    Console.WriteLine();
}

static void RecordLastMetrics(
    JToken? metrics,
    string commandExecuted,
    int? commandExitCode,
    ContextPruningResult? contextPruning = null)
{
    try
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".rtk");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "last-metrics.json");

        int? original = metrics is not null ? ReadInt(metrics, "tokens_original") : null;
        int? optimizedTok = metrics is not null ? ReadInt(metrics, "tokens_optimized") : null;
        int? saved = original.HasValue && optimizedTok.HasValue
            ? Math.Max(0, original.Value - optimizedTok.Value)
            : null;
        decimal? percent = metrics is not null ? ReadDecimal(metrics, "savings_percentage") : null;
        int? sessionTotal = metrics is not null ? ReadInt(metrics, "total_session_savings") : null;

        var payload = new Dictionary<string, object?>
        {
            ["writtenAtUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["commandExecuted"] = commandExecuted,
            ["commandExitCode"] = commandExitCode,
            ["tokensOriginal"] = original,
            ["tokensOptimized"] = optimizedTok,
            ["tokensSaved"] = saved,
            ["savingsPercent"] = percent,
            ["sessionTotalSavedTokens"] = sessionTotal,
            ["contextFiles"] = contextPruning?.Files,
            ["contextTokensOriginal"] = contextPruning?.OriginalTokenCount,
            ["contextTokensPruned"] = contextPruning?.PrunedTokenCount,
            ["contextTokensSaved"] = contextPruning is not null
                ? Math.Max(0, contextPruning.OriginalTokenCount - contextPruning.PrunedTokenCount)
                : null,
        };

        var json = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        RecordCommandSessionEvent(metrics, commandExecuted, commandExitCode, contextPruning);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"RTK warning: could not write last-metrics.json: {ex.Message}");
    }
}

static void RecordCommandSessionEvent(
    JToken? metrics,
    string commandExecuted,
    int? commandExitCode,
    ContextPruningResult? contextPruning)
{
    int commandOriginal = metrics is not null ? ReadInt(metrics, "tokens_original") ?? 0 : 0;
    int commandOptimized = metrics is not null ? ReadInt(metrics, "tokens_optimized") ?? commandOriginal : commandOriginal;
    int contextOriginal = contextPruning?.OriginalTokenCount ?? 0;
    int contextPruned = contextPruning?.PrunedTokenCount ?? contextOriginal;
    int original = commandOriginal + contextOriginal;
    int optimized = commandOptimized + contextPruned;
    CursorSessionMeter.Record(new CursorSessionEvent(
        DateTimeOffset.UtcNow,
        "rtk_command",
        commandExitCode == 0 ? "completed" : "failed",
        commandExecuted,
        original,
        optimized,
        Math.Max(0, original - optimized)));
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
