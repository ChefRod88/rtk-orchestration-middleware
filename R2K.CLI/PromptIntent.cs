namespace R2K.CLI;

public sealed record PromptIntent(string CleanPrompt, bool ForceWorkspaceScan)
{
    private const string DefaultWorkspacePrompt = "Review this workspace and summarize the relevant code.";

    public static PromptIntent Parse(string prompt)
    {
        string trimmed = (prompt ?? string.Empty).Trim();
        if (trimmed.Equals("rtk", StringComparison.OrdinalIgnoreCase))
            return new PromptIntent(DefaultWorkspacePrompt, ForceWorkspaceScan: true);

        if (trimmed.StartsWith("rtk:", StringComparison.OrdinalIgnoreCase))
            return FromRtkPrefix(trimmed[4..]);

        if (trimmed.StartsWith("rtk ", StringComparison.OrdinalIgnoreCase))
            return FromRtkPrefix(trimmed[4..]);

        return new PromptIntent(prompt ?? string.Empty, ForceWorkspaceScan: false);
    }

    private static PromptIntent FromRtkPrefix(string value)
    {
        string clean = value.Trim();
        return new PromptIntent(
            clean.Length == 0 ? DefaultWorkspacePrompt : clean,
            ForceWorkspaceScan: true);
    }
}
