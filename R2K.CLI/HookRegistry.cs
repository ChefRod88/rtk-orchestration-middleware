using System.Text.Json;
using System.Text.Json.Serialization;

namespace R2K.CLI;

public enum PruningStrategy
{
    Minimal,
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

    private HookRegistry(IEnumerable<HookDefinition> hookDefinitions)
    {
        hooks = hookDefinitions
            .Where(hook => !string.IsNullOrWhiteSpace(hook.Command))
            .ToDictionary(
                hook => hook.Command.Trim(),
                hook => hook,
                StringComparer.OrdinalIgnoreCase);
    }

    public static HookRegistry LoadFromDefaultLocations()
        => Load(FindHooksConfig());

    public static HookRegistry Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new HookRegistry([]);

        try
        {
            string json = File.ReadAllText(path);
            var hooks = JsonSerializer.Deserialize<List<HookDefinition>>(json, JsonOptions) ?? [];
            return new HookRegistry(hooks);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"RTK warning: could not read hooks registry: {ex.Message}");
            return new HookRegistry([]);
        }
    }

    public bool ShouldIntercept(string command)
        => hooks.ContainsKey(command);

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

        return null;
    }
}

public sealed record HookDefinition(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("pruningStrategy")] PruningStrategy PruningStrategy);
