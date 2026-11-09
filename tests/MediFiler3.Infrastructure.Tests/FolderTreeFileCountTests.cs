using MediFiler3.Application.Folders;
using MediFiler3.Infrastructure.Folders;

namespace MediFiler3.Infrastructure.Tests;

public sealed class FolderTreeFileCountTests
{
    [Fact]
    public async Task CountsDirectAndNestedFilesUsingBrowserFiltering()
    {
        var root = Path.Combine(Path.GetTempPath(), "MediFiler3Counts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Directory.CreateDirectory(Path.Combine(root, "Subfolder1")).FullName;
            var nested = Directory.CreateDirectory(Path.Combine(first, "Nested")).FullName;
            var second = Directory.CreateDirectory(Path.Combine(root, "Subfolder2")).FullName;
            Directory.CreateDirectory(Path.Combine(root, "Empty"));
            for (var index = 0; index < 7; index++) File.WriteAllText(Path.Combine(root, $"{index}.txt"), "test");
            File.WriteAllText(Path.Combine(first, "one.txt"), "test");
            File.WriteAllText(Path.Combine(nested, "two.txt"), "test");
            File.WriteAllText(Path.Combine(second, "unsupported.xyz"), "test");
            foreach (var attribute in new[] { FileAttributes.Hidden, FileAttributes.System })
            {
                var path = Path.Combine(root, $"{attribute}.txt");
                File.WriteAllText(path, "excluded");
                File.SetAttributes(path, attribute);
            }

            var service = new FileSystemFolderTreeService();
            var result = await service.LoadRootsAsync([root], CancellationToken.None);

            Assert.Empty(result.Errors);
            var entry = Assert.Single(result.Roots);
            var entries = Flatten(entry).ToDictionary(item => item.Path);
            Assert.All(entries.Values, item => Assert.Equal($"(?/?) - {item.Name}", item.DisplayName));
            await foreach (var count in service.CountFilesAsync(entries.Keys, CancellationToken.None))
            {
                Assert.Empty(count.Errors);
                entries[count.Path].UpdateDirectFileCount(count.Count!.Value);
            }
            Assert.Equal(7, entry.DirectFileCount);
            Assert.Equal(10, entry.TotalFileCount);
            Assert.Equal($"(7/10) - {entry.Name}", entry.DisplayName);
            var child = Assert.Single(entry.Children, item => item.Name == "Subfolder1");
            Assert.Equal("(1/2) - Subfolder1", child.DisplayName);
            Assert.Equal("(1/1) - Subfolder2", Assert.Single(entry.Children, item => item.Name == "Subfolder2").DisplayName);
            Assert.Equal("(0/0) - Empty", Assert.Single(entry.Children, item => item.Name == "Empty").DisplayName);
        }
        finally
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    private static IEnumerable<FolderTreeEntry> Flatten(FolderTreeEntry entry)
    {
        yield return entry;
        foreach (var child in entry.Children)
            foreach (var descendant in Flatten(child)) yield return descendant;
    }

    [Fact]
    public void TotalsRemainUnknownUntilEveryDescendantCountIsLoaded()
    {
        var leaf = new FolderTreeEntry("Leaf", "root/leaf");
        var root = new FolderTreeEntry("Root", "root", [leaf]);
        Assert.Equal("(?/?) - Root", root.DisplayName);

        root.UpdateDirectFileCount(7);
        Assert.Equal("(7/?) - Root", root.DisplayName);
        leaf.UpdateDirectFileCount(0);
        Assert.Equal("(0/0) - Leaf", leaf.DisplayName);
        Assert.Equal("(7/7) - Root", root.DisplayName);

        var version = root.CountVersion;
        root.UpdateDirectFileCount(7);
        Assert.True(root.CountVersion > version);
    }

    [Fact]
    public async Task UnreadableCountRemainsUnknownAndDoesNotStopOtherFolders()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var results = new List<FolderFileCountResult>();
        await foreach (var result in new FileSystemFolderTreeService().CountFilesAsync(
                           [missing, Path.GetTempPath()], CancellationToken.None))
            results.Add(result);

        Assert.Equal(2, results.Count);
        Assert.Null(results[0].Count);
        Assert.NotEmpty(results[0].Errors);
        Assert.NotNull(results[1].Count);
    }

    [Fact]
    public async Task CountStreamHonorsCancellationBetweenResults()
    {
        using var cancellation = new CancellationTokenSource();
        await using var stream = new FileSystemFolderTreeService().CountFilesAsync(
            [Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "unused")], cancellation.Token).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stream.MoveNextAsync());
    }

    [Fact]
    public void UpdatingCountsNotifiesAllAncestorsAndKeepsFolderNamesUnchanged()
    {
        var leaf = new FolderTreeEntry("Leaf", "root/child/leaf", directFileCount: 2);
        var child = new FolderTreeEntry("Child", "root/child", [leaf], directFileCount: 1);
        var root = new FolderTreeEntry("Root", "root", [child], directFileCount: 7);
        var notifications = new List<string?>();
        root.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        leaf.UpdateDirectFileCount(5);

        Assert.Equal("(5/5) - Leaf", leaf.DisplayName);
        Assert.Equal("(1/6) - Child", child.DisplayName);
        Assert.Equal("(7/13) - Root", root.DisplayName);
        Assert.Equal("Root", root.Name);
        Assert.Contains(nameof(FolderTreeEntry.TotalFileCount), notifications);
        Assert.Contains(nameof(FolderTreeEntry.DisplayName), notifications);

        root.UpdateDirectFileCount(6);
        Assert.Equal("(6/12) - Root", root.DisplayName);
    }

    [Fact]
    public async Task CancelledScanDoesNotReturnPartialCounts()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FileSystemFolderTreeService().LoadRootsAsync([Path.GetTempPath()], cancellation.Token));
    }
}
