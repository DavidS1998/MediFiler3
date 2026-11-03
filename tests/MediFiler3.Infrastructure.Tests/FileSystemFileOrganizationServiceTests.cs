using MediFiler3.Application.Operations;
using MediFiler3.Domain.Operations;
using MediFiler3.Infrastructure.Operations;
using MediFiler3.Infrastructure.Persistence;

namespace MediFiler3.Infrastructure.Tests;

public sealed class FileSystemFileOrganizationServiceTests
{
    [Fact]
    public async Task MovesFileWithoutOverwritingAndRecordsVerifiedCompletion()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var source = Path.Combine(workspace.Root, "source.txt");
        var destinationFolder = Directory.CreateDirectory(Path.Combine(workspace.Root, "target")).FullName;
        await File.WriteAllTextAsync(source, "preserve this content");

        var result = await workspace.Service.MoveFileAsync(source, destinationFolder);
        var destination = Path.Combine(destinationFolder, "source.txt");

        Assert.True(result.Succeeded, result.Message);
        Assert.False(File.Exists(source));
        Assert.Equal("preserve this content", await File.ReadAllTextAsync(destination));
        var history = await tempDb.Journal.GetAsync(result.OperationId!.Value);
        Assert.NotNull(history);
        Assert.Equal(FileOperationEventKind.Completed, history.State);
        Assert.Equal(FileOperationKind.Move, history.Operation.Kind);
    }

    [Fact]
    public async Task FileNameConflictLeavesBothFilesUntouchedAndDoesNotJournalAnAttempt()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var source = Path.Combine(workspace.Root, "same.txt");
        var targetFolder = Directory.CreateDirectory(Path.Combine(workspace.Root, "target")).FullName;
        var destination = Path.Combine(targetFolder, "same.txt");
        await File.WriteAllTextAsync(source, "source");
        await File.WriteAllTextAsync(destination, "destination");

        await Assert.ThrowsAsync<IOException>(() => workspace.Service.MoveFileAsync(source, targetFolder));

        Assert.Equal("source", await File.ReadAllTextAsync(source));
        Assert.Equal("destination", await File.ReadAllTextAsync(destination));
        Assert.Empty(await tempDb.Journal.GetIncompleteAsync());
        Assert.Null(await tempDb.Journal.GetNextUndoAsync());
    }

    [Fact]
    public async Task RenamesFileAndPreservesItsContentsAndIdentity()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var source = Path.Combine(workspace.Root, "before.txt");
        await File.WriteAllTextAsync(source, "file contents");
        var identity = new WindowsFileIdentityProvider().GetIdentity(source).Identity;

        var result = await workspace.Service.RenameFileAsync(source, "after.txt");
        var destination = Path.Combine(workspace.Root, "after.txt");

        Assert.True(result.Succeeded, result.Message);
        Assert.False(File.Exists(source));
        Assert.Equal("file contents", await File.ReadAllTextAsync(destination));
        Assert.Equal(identity, new WindowsFileIdentityProvider().GetIdentity(destination).Identity);
    }

    [Fact]
    public async Task CreatesAndMovesFoldersWithoutOverwritingContents()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var created = await workspace.Service.CreateFolderAsync(workspace.Root, "created");
        Assert.True(created.Succeeded, created.Message);
        Assert.True(Directory.Exists(Path.Combine(workspace.Root, "created")));

        var sourceFolder = Directory.CreateDirectory(Path.Combine(workspace.Root, "source-folder")).FullName;
        await File.WriteAllTextAsync(Path.Combine(sourceFolder, "inside.txt"), "inside");
        var destinationParent = Directory.CreateDirectory(Path.Combine(workspace.Root, "destination-parent")).FullName;

        var moved = await workspace.Service.MoveFolderAsync(sourceFolder, destinationParent);
        var movedPath = Path.Combine(destinationParent, "source-folder");

        Assert.True(moved.Succeeded, moved.Message);
        Assert.False(Directory.Exists(sourceFolder));
        Assert.Equal("inside", await File.ReadAllTextAsync(Path.Combine(movedPath, "inside.txt")));
        Assert.Equal(FileOperationEventKind.Completed, (await tempDb.Journal.GetAsync(moved.OperationId!.Value))!.State);
    }

    [Fact]
    public async Task FolderOperationsRejectOverwriteAndMovingIntoDescendants()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var source = Directory.CreateDirectory(Path.Combine(workspace.Root, "source")).FullName;
        var existing = Directory.CreateDirectory(Path.Combine(workspace.Root, "existing")).FullName;
        var child = Directory.CreateDirectory(Path.Combine(source, "child")).FullName;

        await Assert.ThrowsAsync<IOException>(() => workspace.Service.RenameFolderAsync(source, "existing"));
        var nestedMove = await workspace.Service.MoveFolderAsync(source, child);

        Assert.Equal(FileOrganizationOutcome.Failed, nestedMove.Outcome);
        Assert.True(Directory.Exists(source));
        Assert.True(Directory.Exists(existing));
    }

    [Fact]
    public async Task RequiresYesNoConfirmationBeforeJournalingNonEmptyFolderDeletion()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var folder = Directory.CreateDirectory(Path.Combine(workspace.Root, "not-empty")).FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, "keep.txt"), "keep");

        var result = await workspace.Service.SendToRecycleBinAsync(folder, isDirectory: true);

        Assert.Equal(FileOrganizationOutcome.ConfirmationRequired, result.Outcome);
        Assert.True(File.Exists(Path.Combine(folder, "keep.txt")));
        Assert.Empty(await tempDb.Journal.GetIncompleteAsync());
    }

    [Fact]
    public async Task RefusesFolderMoveAcrossVolumesWithoutChangingSource()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var source = Directory.CreateDirectory(Path.Combine(workspace.Root, "source")).FullName;
        await File.WriteAllTextAsync(Path.Combine(source, "inside.txt"), "data");
        var differentVolume = Path.GetPathRoot(Environment.SystemDirectory)!;
        if (string.Equals(Path.GetPathRoot(source), differentVolume, StringComparison.OrdinalIgnoreCase))
            return;
        var target = differentVolume;

        var result = await workspace.Service.MoveFolderAsync(source, target);

        Assert.Equal(FileOrganizationOutcome.Failed, result.Outcome);
        Assert.True(File.Exists(Path.Combine(source, "inside.txt")));
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace(TemporaryDatabase database)
        {
            Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MediFiler3Tests", Guid.NewGuid().ToString("N"))).FullName;
            Service = new FileSystemFileOrganizationService(database.Journal, new WindowsFileIdentityProvider());
        }

        public string Root { get; }
        public IFileOrganizationService Service { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
