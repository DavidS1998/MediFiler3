using MediFiler3.Application.Files;
using MediFiler3.Application.Operations;
using MediFiler3.Domain.Files;
using MediFiler3.Infrastructure.Operations;
using MediFiler3.Infrastructure.Persistence;

namespace MediFiler3.Infrastructure.Tests;

public sealed class CollisionPresentationTests
{
    [Theory]
    [InlineData("Test.png", "Test", "Changed.png")]
    [InlineData("archive.tar.gz", "archive.tar", "Changed.gz")]
    [InlineData("README", "README", "Changed")]
    [InlineData(".gitignore", ".gitignore", "Changed")]
    [InlineData("Test.PNG", "Test", "Changed.PNG")]
    public void EditableNamePreservesTheOriginalExtension(string original, string editable, string completed)
    {
        Assert.Equal(editable, FileRenameName.EditableName(original));
        Assert.Equal(completed, FileRenameName.CompleteName(original, "Changed"));
    }

    [Theory]
    [InlineData("Test.png", false, "Test (2).png")]
    [InlineData("Test (1).png", false, "Test (2).png")]
    [InlineData("Test (7).png", false, "Test (8).png")]
    [InlineData("Folder.png", true, "Folder.png (1)")]
    [InlineData("README", false, "README (1)")]
    public void NumberedSuggestionsSkipOccupiedNamesAndKeepExtensions(string original, bool directory, string expected)
    {
        Assert.Equal(expected, FileRenameName.SuggestNumberedName(original, directory,
            candidate => candidate.Equals("Test (1).png", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task CollisionSuggestionUsesRequestedDestinationAndSkipsFilesAndDirectories()
    {
        using var temp = new TemporaryDatabase();
        var parent = Path.GetDirectoryName(temp.DatabasePath)!;
        var source = Path.Combine(parent, "Other.png");
        File.WriteAllText(source, "incoming");
        File.WriteAllText(Path.Combine(parent, "Test.png"), "existing");
        Directory.CreateDirectory(Path.Combine(parent, "Test (1).png"));
        File.WriteAllText(Path.Combine(parent, "Test (2).png"), "occupied");
        var service = new FileSystemFileOrganizationService(temp.Journal, new WindowsFileIdentityProvider())
            .WithCollisionResolver(new AcceptSuggestion());
        var result = await service.RenameFileAsync(source, "Test.png");
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("incoming", File.ReadAllText(Path.Combine(parent, "Test (3).png")));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(parent, "Test.png")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewRetainsExactSizeEvenWithoutThumbnail(bool failThumbnail)
    {
        using var temp = new TemporaryDatabase();
        var path = Path.Combine(Path.GetDirectoryName(temp.DatabasePath)!, "file.png");
        File.WriteAllBytes(path, new byte[1234]);
        var thumbnails = new Thumbnails(failThumbnail);
        var preview = await new FileSystemCollisionPreviewService(thumbnails).LoadAsync(path, default);
        Assert.False(preview.IsDirectory);
        Assert.Equal(1234, preview.Size);
        Assert.Equal(failThumbnail ? null : new byte[] { 1, 2, 3 }, preview.ThumbnailPng);
        Assert.Equal(path, thumbnails.File?.FullPath);
        Assert.Equal(1234, thumbnails.File?.Size);
    }

    [Fact]
    public async Task FolderPreviewCountsNestedFilesAndBytesOnBothSidesWithoutRequestingThumbnails()
    {
        using var temp = new TemporaryDatabase();
        var thumbnails = new Thumbnails(false);
        var parent = Path.GetDirectoryName(temp.DatabasePath)!;
        var service = new FileSystemCollisionPreviewService(thumbnails);
        foreach (var side in new[] { "incoming", "existing" })
        {
            var folder = Directory.CreateDirectory(Path.Combine(parent, side)).FullName;
            var child = Directory.CreateDirectory(Path.Combine(folder, "child")).FullName;
            File.WriteAllBytes(Path.Combine(folder, "a.bin"), new byte[1234]);
            var hidden = Path.Combine(child, "b.bin"); File.WriteAllBytes(hidden, new byte[5678]);
            File.SetAttributes(hidden, FileAttributes.Hidden);
            var preview = await service.LoadAsync(folder, default);
            Assert.True(preview.IsDirectory);
            Assert.True(preview.IsComplete);
            Assert.Equal(2, preview.FileCount);
            Assert.Equal(6912, preview.Size);
        }
        Assert.Null(thumbnails.File);
    }

    [Fact]
    public async Task EmptyFolderPreviewHasZeroFilesAndBytes()
    {
        using var temp = new TemporaryDatabase();
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(temp.DatabasePath)!, "empty")).FullName;
        var preview = await new FileSystemCollisionPreviewService(new Thumbnails(false)).LoadAsync(folder, default);
        Assert.Equal(0, preview.FileCount);
        Assert.Equal(0, preview.Size);
        Assert.True(preview.IsComplete);
    }

    [Fact]
    public async Task ClosedDialogCanCancelPreviewBeforeFilesystemWork()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FileSystemCollisionPreviewService(new Thumbnails(false)).LoadAsync("missing", cancellation.Token));
    }

    private sealed class AcceptSuggestion : IFileCollisionResolver
    {
        public Task<FileCollisionResolution> ResolveAsync(FileCollision collision, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileCollisionResolution(FileCollisionChoice.Rename, collision.SuggestedName));
    }

    private sealed class Thumbnails(bool fail) : IThumbnailService
    {
        public FileEntry? File;
        public Task<byte[]?> GetThumbnailPngAsync(FileEntry file, int width, int height, CancellationToken cancellationToken)
        {
            File = file;
            if (fail) throw new IOException("Preview not available");
            return Task.FromResult<byte[]?>([1, 2, 3]);
        }
    }
}
