using System.Text.Json;
using System.Text.Json.Serialization;

namespace R2K.CLI;

public enum PruningStrategy
{
    Minimal,
    DiffOnly,
    Agentic,
}

public sealed class HookRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly Dictionary<string, HookDefinition> hooks;

    private HookRegistry(
        IEnumerable<HookDefinition> hookDefinitions,
        HookRegistrySettings? settings = null,
        string? version = null)
    {
        Settings = settings ?? new HookRegistrySettings(null, null);
        Version = version;
        hooks = hookDefinitions
            .Where(hook => !string.IsNullOrWhiteSpace(hook.Command))
            .ToDictionary(
                hook => hook.Command.Trim(),
                hook => hook,
                StringComparer.OrdinalIgnoreCase);
    }

    public HookRegistrySettings Settings { get; }
    public string? Version { get; }

    public static HookRegistry LoadFromDefaultLocations()
        => Load(FindHooksConfig());

    public static HookRegistry Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new HookRegistry([]);

        try
        {
            string json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var hooks = JsonSerializer.Deserialize<List<LegacyHookDefinition>>(json, JsonOptions) ?? [];
                return new HookRegistry(hooks.Select(hook => hook.ToHookDefinition()));
            }

            var registry = JsonSerializer.Deserialize<HookRegistryDocument>(json, JsonOptions);
            return new HookRegistry(
                registry?.GetHooks().Select(hook => hook.ToHookDefinition()) ?? [],
                registry?.Settings,
                registry?.Version);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"RTK warning: could not read hooks registry: {ex.Message}");
            return new HookRegistry([]);
        }
    }

    public bool ShouldIntercept(string command)
        => hooks.ContainsKey(command);

    public HookDefinition? GetHook(string command)
        => hooks.TryGetValue(command, out HookDefinition? hook)
            ? hook
            : null;

    public PruningStrategy? GetPruningStrategy(string command)
        => hooks.TryGetValue(command, out HookDefinition? hook)
            ? hook.PruningStrategy
            : null;

    private static string? FindHooksConfig()
    {
        string? current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine(current, "hooks.json");
            if (File.Exists(candidate))
                return candidate;

            current = Directory.GetParent(current)?.FullName;
        }

        string globalConfig = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "r2k",
            "hooks.json");
        return File.Exists(globalConfig) ? globalConfig : null;
    }
}

public sealed record HookDefinition(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("pruningStrategy")] PruningStrategy PruningStrategy);

public sealed record HookRegistrySettings(
    [property: JsonPropertyName("telemetry_endpoint")] string? TelemetryEndpoint,
    [property: JsonPropertyName("default_mode")] string? DefaultMode);

internal sealed record HookRegistryDocument(
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("settings")] HookRegistrySettings? Settings,
    [property: JsonPropertyName("hooks")] IReadOnlyList<ConfiguredHookDefinition>? Hooks)
{
    public IReadOnlyList<ConfiguredHookDefinition> GetHooks()
        => Hooks ?? [];
}

internal sealed record ConfiguredHookDefinition(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("strategy")] string? Strategy,
    [property: JsonPropertyName("pruningStrategy")] string? PruningStrategy)
{
    public HookDefinition ToHookDefinition()
        => new(Command, ParseStrategy(Strategy ?? PruningStrategy));

    private static PruningStrategy ParseStrategy(string? strategy)
        => Normalize(strategy) switch
        {
            "agentic" => R2K.CLI.PruningStrategy.Agentic,
            "diffonly" => R2K.CLI.PruningStrategy.DiffOnly,
            "minimal" or "" => R2K.CLI.PruningStrategy.Minimal,
            _ => R2K.CLI.PruningStrategy.Minimal,
        };

    private static string Normalize(string? value)
        => (value ?? "").Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
}

internal sealed record LegacyHookDefinition(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("pruningStrategy")] PruningStrategy PruningStrategy)
{
    public HookDefinition ToHookDefinition()
        => new(Command, PruningStrategy);
}
