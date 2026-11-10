using System.IO;
using MediFiler3.Application.Folders;
using MediFiler3.Infrastructure.Folders;

namespace MediFiler3.Infrastructure.Tests;

public sealed class FolderChangeTests
{
    [Fact]
    public async Task UnchangedLargeHierarchyRescanDoesNotFlickerKnownRootLabel()
    {
        var children = Enumerable.Range(0, 2000).Select(i => new FolderTreeEntry(i.ToString(), $"root/{i}", directFileCount: 1)).ToArray();
        var root = new FolderTreeEntry("root", "root", children, directFileCount: 3);
        var labels = new List<string>();
        root.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FolderTreeEntry.DisplayName)) labels.Add(root.DisplayName); };
        foreach (var child in children)
            await child.ReconcileChildrenAsync([]);
        await root.ReconcileChildrenAsync(children.Select(c => new FolderTreeEntry(c.Name, c.Path)).ToArray());
        Assert.Empty(labels);
        Assert.Equal(2003, root.TotalFileCount);
        children[0].UpdateDirectFileCount(2);
        Assert.Equal(new[] { "(3/2004) - root" }, labels);
    }

    [Fact]
    public void UnknownTotalDoesNotRepeatedlyPublishSameLabelDuringProgressiveDiscovery()
    {
        var root = new FolderTreeEntry("root", "root", directFileCount: 3, areChildrenLoaded: false);
        var labels = new List<string>();
        root.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FolderTreeEntry.DisplayName)) labels.Add(root.DisplayName); };
        for (var i = 0; i < 2000; i++) root.AddChild(new(i.ToString(), $"root/{i}", directFileCount: 1));
        Assert.Empty(labels);
        root.CompleteChildrenLoad();
        Assert.Equal(new[] { "(3/2003) - root" }, labels);
        root.UpdateDirectFileCount(4);
        Assert.Equal("(4/2004) - root", labels.Last());
    }

    [Fact]
    public void DebounceCoalescesAndContinuousEventsHaveMaximumLatency()
    {
        var start = DateTimeOffset.UtcNow;
        var buffer = new FolderChangeBuffer(["root"]);
        buffer.Add("a", false, start);
        buffer.Add("A", true, start.AddMilliseconds(100));
        Assert.Null(buffer.Take(start.AddMilliseconds(599)));
        var batch = Assert.IsType<FolderChangeBatch>(buffer.Take(start.AddMilliseconds(600)));
        Assert.Single(batch.Paths); Assert.True(batch.HierarchyChanged); Assert.Null(buffer.Take(start.AddSeconds(3)));
        for (var i = 0; i <= 20; i++) buffer.Add(i.ToString(), false, start.AddMilliseconds(i * 100));
        Assert.NotNull(buffer.Take(start.AddSeconds(2)));
    }

    [Fact]
    public void OverflowBoundsPathsAndRescansEveryCoverageRoot()
    {
        var buffer = new FolderChangeBuffer(["one", "two"]); var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 50000; i++) buffer.Add(i.ToString(), false, now);
        var batch = buffer.Take(now.AddSeconds(1))!;
        Assert.Empty(batch.Paths); Assert.Equal(new[] { "one", "two" }, batch.RescanRoots); Assert.True(batch.HierarchyChanged);
        buffer.Add("new", false, now, overflow: true);
        Assert.Equal(2, buffer.Take(now.AddSeconds(1))!.RescanRoots.Count);
    }

    [Fact]
    public void CoverageDeduplicatesAliasesAndNestedRootsWithoutPrefixConfusion()
    {
        var root = Path.Combine(Path.GetTempPath(), "root");
        var sibling = root + "sibling";
        var result = FileSystemFolderChangeMonitor.ReduceRoots([root, root.ToUpperInvariant(), root + "\\", Path.Combine(root, "nested"), sibling]);
        Assert.Equal(2, result.Count); Assert.Contains(sibling, result);
        Assert.Single(FileSystemFolderChangeMonitor.ReduceRoots([Path.GetPathRoot(root)!, root]));
    }

    [Fact]
    public void TreeReconciliationPreservesNodesCollapseAndCountPropagation()
    {
        var child = new FolderTreeEntry("child", "root/child", directFileCount: 3);
        var root = new FolderTreeEntry("root", "root", [child], directFileCount: 2) { IsExpanded = false };
        root.ReconcileChildren([new("child", "root/child"), new("added", "root/added", directFileCount: 4)]);
        Assert.Same(child, root.Children[0]); Assert.False(root.IsExpanded); Assert.Equal(9, root.TotalFileCount);
        child.UpdateDirectFileCount(5); Assert.Equal(11, root.TotalFileCount);
        root.ReconcileChildren([new("added", "root/added")]);
        child.UpdateDirectFileCount(100); Assert.Equal(6, root.TotalFileCount); Assert.Null(child.Parent);
    }

    [Fact]
    public async Task RealWatcherReportsNestedAndInactiveRootChangesAndStopsOnCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), "MediFiler3WatchTests-" + Guid.NewGuid().ToString("N"));
        var first = Path.Combine(root, "first"); var second = Path.Combine(root, "second");
        Directory.CreateDirectory(Path.Combine(first, "nested")); Directory.CreateDirectory(second);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nestedPath = Path.Combine(first, "nested", "test.txt"); var inactivePath = Path.Combine(second, "test.txt");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var task = new FileSystemFolderChangeMonitor().RunAsync([first, second], () => first, (batch, _) =>
        {
            if (batch.RescanRoots.Count > 0) ready.TrySetResult();
            foreach (var path in batch.Paths) paths.Add(path);
            if (paths.Contains(nestedPath) && paths.Contains(inactivePath)) observed.TrySetResult();
            return Task.CompletedTask;
        }, stop.Token);
        try
        {
            await ready.Task.WaitAsync(stop.Token);
            File.WriteAllText(nestedPath, "a"); File.WriteAllText(inactivePath, "b");
            await observed.Task.WaitAsync(stop.Token);
            stop.Cancel(); await task.WaitAsync(TimeSpan.FromSeconds(2));
            Directory.Delete(root, true);
        }
        finally { stop.Cancel(); await task; if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
