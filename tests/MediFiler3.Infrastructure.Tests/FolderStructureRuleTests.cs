using System.Text.Json;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Persistence;

namespace MediFiler3.Infrastructure.Tests;

public sealed class FolderStructureRuleTests
{
    [Fact]
    public void CollapseAllDefaultIsDisabledMatchesUnknownCountsAndExcludesRoots()
    {
        var rule = FolderNameRule.CollapseFoldersDefault(); rule.Validate();
        Assert.False(rule.Enabled); Assert.False(rule.ApplyToRootFolders);
        Assert.Null(FolderNameRule.FirstMatch("anything", null, [rule]));
        rule = rule with { Enabled = true };
        Assert.Same(rule, FolderNameRule.FirstMatch("anything", null, [rule]));
        var child = new FolderTreeEntry("child", "root/child", [new("leaf", "root/child/leaf")]);
        var root = new FolderTreeEntry("root", "root", [child], isRoot: true);
        root.ApplyNameRule(rule); child.ApplyNameRule(rule);
        Assert.True(root.IsExpanded); Assert.False(child.IsExpanded);
        root.ApplyNameRule(rule with { ApplyToRootFolders = true }); Assert.False(root.IsExpanded);
    }

    [Fact]
    public void HidingIsReversibleAndPreservesTreeCountsAndRootColor()
    {
        var child = new FolderTreeEntry("child", "root/child", directFileCount: 4);
        var root = new FolderTreeEntry("root", "root", [child], isRoot: true, directFileCount: 2);
        var rule = new FolderNameRule("", new(255, 0, 0)) { Kind = FolderRuleKind.AllFolders, HideFolder = true };
        rule.Validate(); root.ApplyNameRule(rule); child.ApplyNameRule(rule);
        Assert.False(root.IsHiddenByRule); Assert.True(child.IsHiddenByRule);
        Assert.Equal(rule.TextColor, root.RuleTextColor);
        Assert.Single(root.Children); Assert.Equal(6, root.TotalFileCount);
        root.ApplyNameRule(rule with { ApplyToRootFolders = true }); Assert.True(root.IsHiddenByRule);
        child.ApplyNameRule(null); root.ApplyNameRule(null);
        Assert.False(child.IsHiddenByRule); Assert.False(root.IsHiddenByRule); Assert.Equal(6, root.TotalFileCount);
        Assert.Throws<ArgumentException>(() => (rule with { ExpandedByDefault = false }).Validate());
        Assert.Equal(rule, JsonSerializer.Deserialize<FolderNameRule>(JsonSerializer.Serialize(rule)));
    }

    [Fact]
    public void NewDefaultMigratesConfiguredProfilesOnceWithoutResurrectingRemovedRules()
    {
        var old = new ApplicationSettings(FolderNameRules: [], FolderRulesConfigured: true);
        Assert.Equal(FolderNameRule.CollapseFoldersRuleId, Assert.Single(old.GetFolderRules()).Id);
        var removed = old with { FolderCollapseDefaultConfigured = true };
        Assert.Empty(JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(removed))!.GetFolderRules());
        var full = old with { FolderNameRules = Enumerable.Range(0, 256).Select(i => new FolderNameRule(i.ToString(), ExpandedByDefault: false)).ToArray() };
        Assert.Equal(256, full.GetFolderRules().Count);
        Assert.Equal(2, new ApplicationSettings().GetFolderRules().Count);
    }
}
