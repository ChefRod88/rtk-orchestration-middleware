using R2K.Backend;
using Tiktoken;

namespace R2K.Backend.Tests;

public sealed class CliCommandOptimizerTests
{
    private readonly CliCommandOptimizer sut = new();

    [Fact]
    public void Collapses_interior_whitespace_outside_quotes()
    {
        Assert.Equal("npm run build --prod", sut.Optimize("npm    run    build       --prod"));
    }

    [Fact]
    public void Removes_adjacent_duplicate_flags()
    {
        Assert.Equal("git status --verbose", sut.Optimize("git   status --verbose   --verbose"));
    }

    [Fact]
    public void Preserves_internal_whitespace_inside_quotes()
    {
        Assert.Equal("\"a    b\" x", sut.Optimize("  \"a    b\"   x "));
    }

    [Fact]
    public void NormalizeWhitespace_keeps_spaces_inside_double_quotes()
    {
        string s = "echo \"two  spaces\" tail";
        string n = CliCommandOptimizer.NormalizeWhitespace(s);
        Assert.Contains("two  spaces", n);
    }

    [Fact]
    public void Dedup_duplicate_short_flag_tokens()
    {
        Assert.Equal("cmd -vv", sut.Optimize("cmd -vv -vv"));
        Assert.NotEqual("-v", sut.Optimize("cmd -vv"));
    }
}

public sealed class CommandOptimizationServiceTests
{
    private readonly Encoder encoder = TikTokenEncoder.CreateForModel(Models.Gpt4o);
    private readonly CommandOptimizationService svc;

    public CommandOptimizationServiceTests()
    {
        svc = new CommandOptimizationService(encoder, new CliCommandOptimizer());
    }

    [Fact]
    public void Efficiency_formula_matches_manual_roundtrip()
    {
        const string raw = "pnpm    install      --silent     --silent";
        var metrics = svc.Compute(raw);

        int orig = encoder.CountTokens(raw);
        int optCount = encoder.CountTokens(metrics.OptimizedCommand);
        Assert.Equal(metrics.TokensOriginal, orig);
        Assert.Equal(metrics.TokensOptimized, optCount);

        decimal expected = orig > 0
            ? Math.Round(((decimal)(orig - optCount) / orig) * 100, 2)
            : 0;

        Assert.Equal(expected, metrics.EfficiencyPercent);
        Assert.True(metrics.TokensOptimized <= metrics.TokensOriginal);
    }

    [Fact]
    public void Empty_command_reports_zero_efficiency_and_zero_counts()
    {
        var metrics = svc.Compute("");
        Assert.Equal("", metrics.OptimizedCommand);
        Assert.Equal(0, metrics.TokensOriginal);
        Assert.Equal(0, metrics.EfficiencyPercent);
    }
}
