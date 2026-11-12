using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Xunit;

namespace SandianshuiLauncher.Core.Tests;

public class RuleEvaluatorTests
{
    [Fact]
    public void NullOrEmptyRules_Allowed()
    {
        Assert.True(RuleEvaluator.RulesAllow(null));
        Assert.True(RuleEvaluator.RulesAllow([]));
    }

    [Fact]
    public void AllowWindows_AllowedOnWindows()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow", Os = new OsRule { Name = "windows" } }
        };
        if (OperatingSystem.IsWindows())
        {
            Assert.True(RuleEvaluator.RulesAllow(rules));
        }
    }

    [Fact]
    public void DisallowWindows_BlockedOnWindows()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow" },
            new() { Action = "disallow", Os = new OsRule { Name = "windows" } }
        };
        if (OperatingSystem.IsWindows())
        {
            Assert.False(RuleEvaluator.RulesAllow(rules));
        }
    }

    [Fact]
    public void DisallowOtherOs_DoesNotBlockWindows()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow" },
            new() { Action = "disallow", Os = new OsRule { Name = "osx" } }
        };
        if (OperatingSystem.IsWindows())
        {
            Assert.True(RuleEvaluator.RulesAllow(rules));
        }
    }

    [Fact]
    public void FeatureRule_ResolvesCorrectly()
    {
        var rules = new List<Rule>
        {
            new()
            {
                Action = "allow",
                Features = new Dictionary<string, bool> { ["has_custom_resolution"] = true }
            }
        };
        Assert.True(RuleEvaluator.RulesAllow(rules,
            new Dictionary<string, bool> { ["has_custom_resolution"] = true }));
        Assert.False(RuleEvaluator.RulesAllow(rules,
            new Dictionary<string, bool> { ["has_custom_resolution"] = false }));
    }
}
