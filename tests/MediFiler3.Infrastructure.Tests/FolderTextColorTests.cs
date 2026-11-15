using System.Text.Json;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Persistence;

namespace MediFiler3.Infrastructure.Tests;

public sealed class FolderTextColorTests
{
    [Theory]
    [InlineData(0, 255)] [InlineData(1, 255)] [InlineData(2, 223)] [InlineData(3, 192)]
    [InlineData(4, 160)] [InlineData(5, 128)] [InlineData(100, 128)]
    public void DepthTonesAreBounded(int depth, byte expected) => Assert.Equal(expected, FolderTextColorPolicy.GetGray(depth));

    [Fact]
    public void DisablingShadingKeepsEveryDepthWhite()
    {
        foreach (var depth in new[] { 0, 1, 2, 5, 100 }) Assert.Equal(255, FolderTextColorPolicy.GetGray(depth, false));
    }

    [Fact]
    public void DepthUsesLoadedTreeAncestryAndNotFilesystemPath()
    {
        var leaf = new FolderTreeEntry("leaf", @"C:\a\b\c");
        var child = new FolderTreeEntry("child", @"C:\a\b", [leaf]);
        var changed = 0; leaf.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(leaf.Depth)) changed++; };
        var root = new FolderTreeEntry("root", @"C:\a", [child], isRoot: true);
        Assert.Equal(0, root.Depth); Assert.Equal(1, child.Depth); Assert.Equal(2, leaf.Depth);
        Assert.Equal(1, changed);
        Assert.Equal(0, new FolderTreeEntry("overlap", leaf.Path, isRoot: true).Depth);
    }

    [Fact]
    public void OlderSettingsDefaultToShadingAndDisabledSettingRoundTrips()
    {
        Assert.True(JsonSerializer.Deserialize<ApplicationSettings>("{}")!.FolderDepthShadingEnabled);
        var settings = new ApplicationSettings(Volume: .3, FolderDepthShadingEnabled: false);
        Assert.Equal(settings, JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(settings)));
    }
}
