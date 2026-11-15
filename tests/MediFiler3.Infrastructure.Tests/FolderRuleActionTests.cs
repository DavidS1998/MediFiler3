using MediFiler3.Application.Folders;

namespace MediFiler3.Infrastructure.Tests;

public sealed class FolderRuleActionTests
{
    [Fact]
    public void UnspecifiedActionsFallThroughIndependentlyAndFirstExplicitActionWins()
    {
        var pink = new FolderRuleColor(255, 113, 206);
        var color = new FolderNameRule("SET", pink);
        var collapse = FolderNameRule.CollapseFoldersDefault() with { Enabled = true };
        var later = collapse with { ExpandedByDefault = true, TextColor = new(255, 0, 0) };
        Assert.Equal(new FolderRuleActions(pink, false), FolderNameRule.ResolveActions("[SET]", null, false, [color, collapse, later]));
        Assert.Equal(new FolderRuleActions(pink, true), FolderNameRule.ResolveActions("[SET]", null, false, [later with { TextColor = null }, color, collapse]));
        var hide = collapse with { ExpandedByDefault = null, HideFolder = true };
        Assert.Equal(new FolderRuleActions(pink, null, true), FolderNameRule.ResolveActions("[SET]", null, false, [color, hide, collapse]));
        Assert.Equal(new FolderRuleActions(pink, false), FolderNameRule.ResolveActions("[SET]", null, false, [color, collapse, hide]));
    }

    [Fact]
    public void RootExclusionDoesNotBlockALowerRootActionOrRootColor()
    {
        var first = new FolderNameRule("root", new(255, 0, 0), false);
        var rootAction = FolderNameRule.CollapseFoldersDefault() with { Enabled = true, ApplyToRootFolders = true, ExpandedByDefault = true };
        Assert.Equal(new FolderRuleActions(first.TextColor, true), FolderNameRule.ResolveActions("root", null, true, [first, rootAction]));
        Assert.Equal(new FolderRuleActions(first.TextColor), FolderNameRule.ResolveActions("root", null, true, [first]));
    }

    [Fact]
    public void ExplicitSettingsApplicationOverridesManualExpansionButBackgroundUpdatesDoNot()
    {
        var node = new FolderTreeEntry("folder", "folder", [new("child", "folder/child")]);
        node.IsExpanded = false;
        node.ApplyRuleActions(new(ExpandedByDefault: true)); Assert.False(node.IsExpanded);
        node.ApplyRuleActions(new(ExpandedByDefault: true), applyImmediately: true); Assert.True(node.IsExpanded);
        node.ApplyRuleActions(new(ExpandedByDefault: false), applyImmediately: true); Assert.False(node.IsExpanded);
        node.ApplyRuleActions(new()); Assert.False(node.IsExpanded);
        node.IsExpanded = true;
        node.ApplyRuleActions(new()); Assert.True(node.IsExpanded);
    }
}
