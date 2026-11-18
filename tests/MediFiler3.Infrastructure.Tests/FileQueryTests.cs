using MediFiler3.Application.Files;
using MediFiler3.Domain.Files;

namespace MediFiler3.Infrastructure.Tests;

public sealed class FileQueryTests
{
    private static FileEntry Entry(string name, int size = 0, int day = 1) =>
        new(Path.Combine("root", name), name, size, new DateTime(2026, 1, 1))
        { CreatedUtc = new DateTime(2026, 1, day) };

    [Fact]
    public void LiteralSearchAndIncludesAndExcludesCombineWithCategoryUnion()
    {
        var image = Entry("MY [SET] 2.png");
        var text = Entry("my [set] 3.txt");
        FileEntry[] files = [image, text, Entry("my [set] 4.mp4"), Entry("my [set] draft.png"), Entry("my new [set].png")];
        var query = new FileQuery("my [set]", ".", "draft", FileCategories.Images | FileCategories.Text);

        Assert.Equal([image, text], query.Apply(files, CancellationToken.None));
        Assert.Empty((query with { Search = "root" }).Apply(files, CancellationToken.None));
    }

    [Fact]
    public void SortPreservesNaturalOrderOfEqualKeysAndUsesCreationMetadata()
    {
        var first = Entry("page 2.jpg", 4, 2);
        var second = Entry("page 10.jpg", 4, 3);
        var third = Entry("page 20.jpg", 12, 1);
        FileEntry[] inventory = [first, second, third];

        Assert.Same(inventory, FileQuery.AllFiles.Apply(inventory, CancellationToken.None));
        Assert.Equal([third, second, first], new FileQuery(Descending: true).Apply(inventory, CancellationToken.None));
        Assert.Equal([third, first, second], new FileQuery(Sort: FileSort.Size, Descending: true).Apply(inventory, CancellationToken.None));
        Assert.Equal([third, first, second], new FileQuery(Sort: FileSort.Created).Apply(inventory, CancellationToken.None));
        Assert.Equal(inventory, new FileQuery(Sort: FileSort.Modified, Descending: true).Apply(inventory, CancellationToken.None));
    }

    [Fact]
    public void RoutingIncludesExtensionlessTextAndUnknownFiles()
    {
        var text = Entry("LICENSE");
        var audio = Entry("track.OPUS");
        var other = Entry("scene.blend");
        FileEntry[] inventory = [text, audio, other];

        Assert.Equal([text, other], new FileQuery(Categories: FileCategories.Text | FileCategories.Other).Apply(inventory, CancellationToken.None));
        Assert.Equal([audio], new FileQuery(Categories: FileCategories.Audio).Apply(inventory, CancellationToken.None));
        Assert.Throws<OperationCanceledException>(() => FileQuery.AllFiles.Apply(inventory, new CancellationToken(true)));
    }

    [Fact]
    public void SavedFiltersValidateLimitsAndRecentReferences()
    {
        var id = Guid.NewGuid();
        var filter = new SavedFileFilter(id, "Images", new FileQuery(Categories: FileCategories.Images));
        SavedFileFilter.Validate([filter], [id]);

        Assert.Throws<InvalidDataException>(() => SavedFileFilter.Validate([filter], [Guid.NewGuid()]));
        Assert.Throws<InvalidDataException>(() => SavedFileFilter.Validate([filter, filter with { Id = Guid.NewGuid(), Name = "IMAGES" }], []));
        Assert.Throws<InvalidDataException>(() => (filter.Query with { Search = new string('a', 257) }).Validate());
    }

    [Fact]
    public void RecursiveOrderKeepsFolderPreorderOrSortsAcrossFolders()
    {
        var root = Entry("B.png");
        var folder2 = root with { FullPath = Path.Combine("root", "Folder 2", "A.png"), Name = "A.png" };
        var folder10 = folder2 with { FullPath = Path.Combine("root", "Folder 10", "A.png") };
        var later = folder10 with { FullPath = Path.Combine("root", "Folder 10", "C.png"), Name = "C.png" };
        FileEntry[] inventory = [root, folder2, folder10, later];
        var query = new FileQuery(IncludeSubfolders: true);
        Assert.Equal(inventory, query.Apply(inventory, CancellationToken.None));
        Assert.Equal([root, folder2, later, folder10], (query with { Descending = true }).Apply(inventory, CancellationToken.None));
        Assert.Equal([folder2, folder10, root, later], (query with { CombineFolders = true }).Apply(inventory, CancellationToken.None));
    }

    [Fact]
    public async Task SubtreeDiscoveryUsesPreorderSkipsManagedStorageAndCanCancel()
    {
        var root = Path.Combine(Path.GetTempPath(), $"MediFiler3-recursive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var folder2 = Directory.CreateDirectory(Path.Combine(root, "Folder 2"));
        var folder10 = Directory.CreateDirectory(Path.Combine(root, "Folder 10"));
        var backup = Directory.CreateDirectory(Path.Combine(root, ".MediFiler3-UndoBackups-0123456789abcdef"));
        backup.Attributes |= FileAttributes.Hidden;
        var link = Path.Combine(root, "Folder 1 link");
        try
        {
            File.WriteAllText(Path.Combine(root, "B.txt"), "scope");
            File.WriteAllText(Path.Combine(folder2.FullName, "A.txt"), "second");
            File.WriteAllText(Path.Combine(folder10.FullName, "A.txt"), "tenth");
            File.WriteAllText(Path.Combine(backup.FullName, "backup.txt"), "managed");
            var hidden = Path.Combine(folder2.FullName, "hidden.txt");
            File.WriteAllText(hidden, "hidden");
            File.SetAttributes(hidden, FileAttributes.Hidden);
            using (var junction = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "cmd.exe", $"/c mklink /J \"{link}\" \"{folder10.FullName}\"")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true }))
            {
                Assert.NotNull(junction);
                await junction.WaitForExitAsync();
                Assert.Equal(0, junction.ExitCode);
            }
            var service = new MediFiler3.Infrastructure.Files.FileSystemFileListingService();
            var files = new List<string>();
            await foreach (var batch in service.GetSubtreeFilesAsync(root, CancellationToken.None))
            {
                Assert.Empty(batch.Errors);
                files.AddRange(batch.Files.Select(file => Path.GetRelativePath(root, file.FullPath)));
            }
            Assert.Equal(["B.txt", Path.Combine("Folder 2", "A.txt"), Path.Combine("Folder 10", "A.txt")], files);

            using var cancel = new CancellationTokenSource();
            await using var enumeration = service.GetSubtreeFilesAsync(root, cancel.Token).GetAsyncEnumerator();
            Assert.True(await enumeration.MoveNextAsync());
            cancel.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => { await enumeration.MoveNextAsync(); });
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RecursiveDiscoveryAndCombinedQueryHandleAThousandPhysicalFiles()
    {
        var root = Directory.CreateTempSubdirectory("MediFiler3-query-scale-");
        try
        {
            const int folderCount = 20;
            const int filesPerFolder = 50;
            for (var group = 0; group < folderCount; group++)
            {
                var folder = root.CreateSubdirectory($"Folder {group}");
                for (var file = 0; file < filesPerFolder; file++)
                    File.WriteAllText(Path.Combine(folder.FullName, $"seed {file}.txt"), "test");
            }

            var service = new MediFiler3.Infrastructure.Files.FileSystemFileListingService();
            var inventory = new List<FileEntry>();
            await foreach (var batch in service.GetSubtreeFilesAsync(root.FullName, CancellationToken.None))
            {
                Assert.Empty(batch.Errors);
                inventory.AddRange(batch.Files);
            }

            Assert.Equal(folderCount * filesPerFolder, inventory.Count);
            var results = new FileQuery(Search: "seed 10.", IncludeSubfolders: true, CombineFolders: true)
                .Apply(inventory, CancellationToken.None);
            Assert.Equal(folderCount, results.Count);
            Assert.Equal("Folder 2", Path.GetFileName(Path.GetDirectoryName(results[2].FullPath)));
        }
        finally { root.Delete(recursive: true); }
    }
}
