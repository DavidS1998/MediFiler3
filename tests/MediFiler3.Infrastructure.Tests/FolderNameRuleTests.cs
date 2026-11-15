using System.Text.Json;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Persistence;

namespace MediFiler3.Infrastructure.Tests;

public sealed class FolderNameRuleTests
{
    private static readonly FolderRuleColor Pink = new(255, 113, 206);

    [Theory]
    [InlineData("album [SET]", true)] [InlineData("[set] album", true)]
    [InlineData("SET album", false)] [InlineData("other", false)]
    public void ContainsIsCaseInsensitiveLiteralText(string name, bool matches)
    {
        var rule = new FolderNameRule("[SET]", Pink, false);
        Assert.Equal(matches, FolderNameRule.FirstMatch(name, [rule]) is not null);
    }

    [Fact]
    public void FirstMatchFindsFirstEnabledCondition()
    {
        var first = new FolderNameRule("[SET]", ExpandedByDefault: false);
        var second = new FolderNameRule("SET", Pink, true);
        Assert.Same(first, FolderNameRule.FirstMatch("album [SET]", [first, second]));
        Assert.Null(FolderNameRule.FirstMatch("album [SET]", [first, second])!.TextColor);
        Assert.Same(second, FolderNameRule.FirstMatch("album [SET]", [second, first]));
        Assert.Same(second, FolderNameRule.FirstMatch("album [SET]", [first with { Enabled = false }, second]));
    }

    [Fact]
    public void ColorAndCollapseApplyTogetherAndSurviveProgressiveDiscovery()
    {
        var folder = new FolderTreeEntry("[SET]", "root", areChildrenLoaded: false);
        folder.ApplyNameRule(new("[SET]", Pink, false));
        folder.AddChild(new("child", "root/child"));
        Assert.Equal(Pink, folder.RuleTextColor); Assert.False(folder.IsExpanded);
        folder.IsExpanded = true;
        folder.ApplyNameRule(new("[SET]", new(10, 20, 30), false));
        Assert.True(folder.IsExpanded); // Explicit user/drag expansion wins over defaults.
        Assert.Equal(new FolderRuleColor(10, 20, 30), folder.RuleTextColor);
        folder.AddChild(new("child2", "root/child2")); Assert.True(folder.IsExpanded);
    }

    [Fact]
    public void DefaultsApplyToUntouchedFoldersAndUnspecifiedExpansionLeavesCurrentState()
    {
        var folder = new FolderTreeEntry("[SET]", "root", [new("child", "root/child")]);
        Assert.True(folder.IsExpanded);
        folder.ApplyNameRule(new("[SET]", Pink, false)); Assert.False(folder.IsExpanded);
        folder.ApplyNameRule(null); Assert.False(folder.IsExpanded); Assert.Null(folder.RuleTextColor);
        folder.IsExpanded = true; folder.IsExpanded = false; // A real user collapse, not a binding echo.
        folder.ApplyNameRule(new("SET", Pink, true)); Assert.False(folder.IsExpanded);
    }

    [Fact]
    public void ExpansionDefaultsDoNotHideTheSelectedFoldersAncestorRoute()
    {
        var selected = new FolderTreeEntry("reading", "root/reading");
        var root = new FolderTreeEntry("[SET]", "root", [selected]);
        selected.IsSelected = true;
        root.ApplyNameRule(new("[SET]", Pink, false));
        Assert.True(root.IsExpanded); Assert.True(selected.IsSelected); Assert.Equal(Pink, root.RuleTextColor);
    }

    [Fact]
    public void FolderNamesAreNotInheritedFromParentAndColorsCanBeRemoved()
    {
        var child = new FolderTreeEntry("child", "root/child");
        var root = new FolderTreeEntry("[SET]", "root", [child]);
        FolderNameRule[] rules = [new("[SET]", Pink, false)];
        root.ApplyNameRule(FolderNameRule.FirstMatch(root.Name, rules));
        child.ApplyNameRule(FolderNameRule.FirstMatch(child.Name, rules));
        Assert.Null(child.RuleTextColor); Assert.Equal(Pink, root.RuleTextColor);
        root.ApplyNameRule(null); Assert.Null(root.RuleTextColor);
    }

    [Fact]
    public void RulesRoundTripWithOrderActionsAndStableIdentitiesAndOldSettingsRemainValid()
    {
        FolderNameRule[] rules = [new("[SET]", Pink, false), new("other", ExpandedByDefault: true, Enabled: false)];
        var settings = new ApplicationSettings(FolderNameRules: rules);
        settings.Validate();
        var loaded = JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(settings))!;
        loaded.Validate(); Assert.Equal(rules, loaded.FolderNameRules);
        var old = JsonSerializer.Deserialize<ApplicationSettings>("{}")!;
        old.Validate(); Assert.Null(old.FolderNameRules); Assert.True(old.FolderDepthShadingEnabled);
    }

    [Fact]
    public void InvalidRulesCannotReplaceSettingsAndMatchingIsCancellable()
    {
        Assert.Throws<ArgumentException>(() => new FolderNameRule("", Pink).Validate());
        Assert.Throws<ArgumentException>(() => new FolderNameRule("name").Validate());
        Assert.Throws<ArgumentException>(() => new FolderNameRule(new string('x', 257), Pink).Validate());
        var rule = new FolderNameRule("[SET]", Pink);
        Assert.Throws<ArgumentException>(() => FolderNameRule.ValidateAll([rule, rule]));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => FolderNameRule.FirstMatch("[SET]", [rule], stop.Token));
    }
}
