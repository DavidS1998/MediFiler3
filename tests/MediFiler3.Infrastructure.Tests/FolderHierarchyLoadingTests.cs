using MediFiler3.Application.Folders;
using MediFiler3.Infrastructure.Folders;

namespace MediFiler3.Infrastructure.Tests;

public sealed class FolderHierarchyLoadingTests
{
    [Fact]
    public async Task PublishesShallowRootsThenDiscoversSortedChildrenBeforeDescendants()
    {
        using var workspace = new Workspace();
        Directory.CreateDirectory(Path.Combine(workspace.Root, "B", "Nested"));
        Directory.CreateDirectory(Path.Combine(workspace.Root, "A"));
        var service = new FileSystemFolderTreeService();
        var loaded = await service.LoadRootsAsync([workspace.Root], CancellationToken.None);
        var root = Assert.Single(loaded.Roots);
        Assert.Empty(root.Children);
        Assert.False(root.AreChildrenLoaded);

        await using var stream = service.LoadHierarchyAsync(loaded.Roots, CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        Assert.Same(root, stream.Current.Parent);
        Assert.Equal(new[] { "A", "B" }, stream.Current.Children.Select(child => child.Name));
        Assert.All(stream.Current.Children, child => Assert.Empty(child.Children));
        foreach (var child in stream.Current.Children) root.AddChild(child);
        root.CompleteChildrenLoad();
        Assert.True(root.IsExpanded);
        var discovered = new List<string>();
        while (await stream.MoveNextAsync())
        {
            discovered.Add(stream.Current.Parent.Name);
            foreach (var child in stream.Current.Children) stream.Current.Parent.AddChild(child);
            stream.Current.Parent.CompleteChildrenLoad();
        }
        Assert.Equal(new[] { "A", "B", "Nested" }, discovered);
        Assert.Single(root.Children[1].Children);
    }

    [Fact]
    public async Task CancelsDiscoveryBetweenFolders()
    {
        using var workspace = new Workspace();
        Directory.CreateDirectory(Path.Combine(workspace.Root, "Child"));
        var service = new FileSystemFolderTreeService();
        var loaded = await service.LoadRootsAsync([workspace.Root], CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await using var stream = service.LoadHierarchyAsync(loaded.Roots, cancellation.Token).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stream.MoveNextAsync());
    }

    [Fact]
    public void DynamicChildrenKeepTotalsUnknownUntilDiscoveryAndCountsComplete()
    {
        var root = new FolderTreeEntry("Root", "root", directFileCount: 7, areChildrenLoaded: false);
        Assert.Equal("(7/?) - Root", root.DisplayName);
        var child = new FolderTreeEntry("Child", "root/child", directFileCount: 2, areChildrenLoaded: false);
        root.AddChild(child);
        root.CompleteChildrenLoad();
        Assert.Null(root.TotalFileCount);
        child.CompleteChildrenLoad();
        Assert.Equal("(7/9) - Root", root.DisplayName);
        root.IsExpanded = false;
        root.AddChild(new FolderTreeEntry("Second", "root/second", directFileCount: 1));
        Assert.False(root.IsExpanded);
        Assert.Equal("(7/10) - Root", root.DisplayName);
    }

    [Fact]
    public async Task RemovedFolderReportsErrorWithoutPreventingOtherRootsLoading()
    {
        using var workspace = new Workspace();
        var missing = new FolderTreeEntry("Missing", Path.Combine(workspace.Root, "missing"), areChildrenLoaded: false);
        var existing = new FolderTreeEntry("Existing", workspace.Root, areChildrenLoaded: false);
        var results = new List<FolderChildrenResult>();
        await foreach (var result in new FileSystemFolderTreeService().LoadHierarchyAsync([missing, existing], CancellationToken.None))
            results.Add(result);
        Assert.Equal(2, results.Count);
        Assert.NotEmpty(results[0].Errors);
        Assert.Empty(results[1].Errors);
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MediFiler3Hierarchy", Guid.NewGuid().ToString("N"))).FullName;
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
