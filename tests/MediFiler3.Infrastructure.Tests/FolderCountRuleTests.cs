using System.Text.Json;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Persistence;

namespace MediFiler3.Infrastructure.Tests;

public sealed class FolderCountRuleTests
{
    [Theory]
    [InlineData(null, false)] [InlineData(0L, false)] [InlineData(100L, false)]
    [InlineData(101L, true)] [InlineData(1000L, true)]
    public void GreaterThan100UsesInclusiveMinimum101AndUnknownDoesNotMatch(long? count, bool expected)
    {
        var rule = new FolderNameRule("", new(255, 0, 0)) { Kind = FolderRuleKind.FileCount, MinimumFiles = 101 };
        rule.Validate(); Assert.Equal(expected, FolderNameRule.FirstMatch("folder", count, [rule]) is not null);
    }

    [Theory]
    [InlineData(9L, false)] [InlineData(10L, true)] [InlineData(15L, true)] [InlineData(20L, true)] [InlineData(21L, false)]
    public void RangeIncludesBothBounds(long count, bool expected)
    {
        var rule = new FolderNameRule("", ExpandedByDefault: false) { Kind = FolderRuleKind.FileCount, MinimumFiles = 10, MaximumFiles = 20 };
        Assert.Equal(expected, FolderNameRule.FirstMatch("folder", count, [rule]) is not null);
    }

    [Fact]
    public void EmptyFolderDefaultIsEditableAndOnlyMatchesKnownZero()
    {
        var rule = FolderNameRule.EmptyFolderDefault(); rule.Validate();
        Assert.Equal(new FolderRuleColor(96, 96, 96), rule.TextColor);
        Assert.Null(rule.ExpandedByDefault);
        Assert.Same(rule, FolderNameRule.FirstMatch("folder", 0, [rule]));
        Assert.Null(FolderNameRule.FirstMatch("folder", null, [rule]));
        Assert.Null(FolderNameRule.FirstMatch("folder", 1, [rule]));
        Assert.Null(FolderNameRule.FirstMatch("folder", 0, [rule with { Enabled = false }]));
    }

    [Fact]
    public void NameAndCountConditionsShareOnePriorityOrder()
    {
        var name = new FolderNameRule("SET", new(255, 113, 206)); var count = FolderNameRule.EmptyFolderDefault();
        Assert.Same(name, FolderNameRule.FirstMatch("[SET]", 0, [name, count]));
        Assert.Same(count, FolderNameRule.FirstMatch("[SET]", 0, [count, name]));
        Assert.Same(name, FolderNameRule.FirstMatch("[SET]", null, [count, name]));
    }

    [Fact]
    public void DefaultsAreAddedAfterOldRulesButNeverResurrectConfiguredRemovals()
    {
        var old = new FolderNameRule("SET", new(255, 113, 206));
        var settings = new ApplicationSettings(FolderNameRules: [old]);
        Assert.Equal(old, settings.GetFolderRules()[0]);
        Assert.Equal(FolderNameRule.EmptyFolderRuleId, settings.GetFolderRules()[1].Id);
        Assert.Equal(2, new ApplicationSettings().GetFolderRules().Count);
        Assert.Equal(2, new ApplicationSettings(FolderNameRules: [FolderNameRule.EmptyFolderDefault()]).GetFolderRules().Count);
        var removed = settings with { FolderNameRules = [], FolderRulesConfigured = true, FolderCollapseDefaultConfigured = true };
        Assert.Empty(JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(removed))!.GetFolderRules());
    }

    [Fact]
    public void InvalidCountRangesAndKindsAreRejectedAndNewFieldsRoundTrip()
    {
        var rule = FolderNameRule.EmptyFolderDefault();
        Assert.Throws<ArgumentException>(() => (rule with { MinimumFiles = 5, MaximumFiles = 4 }).Validate());
        Assert.Throws<ArgumentException>(() => (rule with { MinimumFiles = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (rule with { MinimumFiles = null, MaximumFiles = null }).Validate());
        Assert.Throws<ArgumentException>(() => (rule with { Kind = (FolderRuleKind)123 }).Validate());
        Assert.Equal(rule, JsonSerializer.Deserialize<FolderNameRule>(JsonSerializer.Serialize(rule)));
    }
}
