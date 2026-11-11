using MediFiler3.Application.Operations;
using MediFiler3.Domain.Operations;
using MediFiler3.Infrastructure.Operations;
using MediFiler3.Infrastructure.Persistence;
using System.Text.Json;

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
    public async Task UndoAndRedoRenameAreVerifiedAndAppendHistoryEvents()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var source = Path.Combine(workspace.Root, "before.txt");
        var destination = Path.Combine(workspace.Root, "after.txt");
        await File.WriteAllTextAsync(source, "contents");
        var renamed = await workspace.Service.RenameFileAsync(source, "after.txt");

        var undone = await workspace.Service.UndoAsync();
        Assert.True(undone.Succeeded, undone.Message);
        Assert.Equal("contents", await File.ReadAllTextAsync(source));
        Assert.False(File.Exists(destination));
        Assert.Equal(FileOperationEventKind.Undone,
            (await tempDb.Journal.GetAsync(renamed.OperationId!.Value))!.State);

        var redone = await workspace.Service.RedoAsync();
        Assert.True(redone.Succeeded, redone.Message);
        Assert.Equal("contents", await File.ReadAllTextAsync(destination));
        Assert.False(File.Exists(source));
        Assert.Equal(FileOperationEventKind.Redone,
            (await tempDb.Journal.GetAsync(renamed.OperationId.Value))!.State);
    }

    [Fact]
    public async Task UndoRefusesToOverwriteAFileCreatedAtTheOriginalPath()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var source = Path.Combine(workspace.Root, "before.txt");
        await File.WriteAllTextAsync(source, "original");
        var renamed = await workspace.Service.RenameFileAsync(source, "after.txt");
        await File.WriteAllTextAsync(source, "external");

        var undone = await workspace.Service.UndoAsync();

        Assert.False(undone.Succeeded);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(workspace.Root, "after.txt")));
        Assert.Equal("external", await File.ReadAllTextAsync(source));
        Assert.Equal(FileOperationEventKind.Completed,
            (await tempDb.Journal.GetAsync(renamed.OperationId!.Value))!.State);
    }

    [Fact]
    public async Task UndoAndRedoFolderMovePreserveNestedContents()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var source = Directory.CreateDirectory(Path.Combine(workspace.Root, "source")).FullName;
        var destinationParent = Directory.CreateDirectory(Path.Combine(workspace.Root, "destination")).FullName;
        await File.WriteAllTextAsync(Path.Combine(source, "inside.txt"), "keep");
        var moved = await workspace.Service.MoveFolderAsync(source, destinationParent);
        var movedPath = Path.Combine(destinationParent, "source");

        var undone = await workspace.Service.UndoAsync();
        Assert.True(undone.Succeeded, undone.Message);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(source, "inside.txt")));
        Assert.False(Directory.Exists(movedPath));

        var redone = await workspace.Service.RedoAsync();
        Assert.True(redone.Succeeded, redone.Message);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(movedPath, "inside.txt")));
        Assert.False(Directory.Exists(source));
        Assert.Equal(FileOperationEventKind.Redone,
            (await tempDb.Journal.GetAsync(moved.OperationId!.Value))!.State);
    }

    [Fact]
    public async Task NewFilesystemOperationAfterUndoInvalidatesRedo()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var first = Path.Combine(workspace.Root, "first.txt");
        await File.WriteAllTextAsync(first, "one");
        await workspace.Service.RenameFileAsync(first, "renamed.txt");
        Assert.True((await workspace.Service.UndoAsync()).Succeeded);

        var second = Path.Combine(workspace.Root, "second.txt");
        await File.WriteAllTextAsync(second, "two");
        var newOperation = await workspace.Service.RenameFileAsync(second, "second-renamed.txt");

        Assert.Null(await tempDb.Journal.GetNextRedoAsync());
        var redo = await workspace.Service.RedoAsync();
        Assert.False(redo.Succeeded);
        Assert.True(File.Exists(Path.Combine(workspace.Root, "first.txt")));
        Assert.True(File.Exists(Path.Combine(workspace.Root, "second-renamed.txt")));
        Assert.Equal(FileOperationEventKind.Completed,
            (await tempDb.Journal.GetAsync(newOperation.OperationId!.Value))!.State);
    }

    [Fact]
    public async Task UndoDoesNotRemoveContentAddedToACreatedFolder()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var created = await workspace.Service.CreateFolderAsync(workspace.Root, "created");
        var createdPath = created.AffectedPath!;
        await File.WriteAllTextAsync(Path.Combine(createdPath, "user-content.txt"), "keep");

        var undone = await workspace.Service.UndoAsync();

        Assert.False(undone.Succeeded);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(createdPath, "user-content.txt")));
        Assert.Equal(FileOperationEventKind.Completed,
            (await tempDb.Journal.GetAsync(created.OperationId!.Value))!.State);
    }

    [Fact]
    public async Task RestoresRecycleBinItemOnlyAfterUniqueIdentityMatchAndVerifiesJournalState()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var originalPath = Path.Combine(workspace.Root, "restored.txt");
        var stagedRecycleItem = Path.Combine(workspace.Root, "recycle-item.bin");
        await File.WriteAllTextAsync(stagedRecycleItem, "restore me");
        var identities = new WindowsFileIdentityProvider();
        var originalIdentity = identities.GetIdentity(stagedRecycleItem).Identity!;
        var operation = await RecordRecycleBinOperationAsync(tempDb.Journal, originalPath, originalIdentity);
        var restoreService = new FakeRecycleBinRestoreService(() =>
        {
            File.Move(stagedRecycleItem, originalPath);
            return Task.CompletedTask;
        });
        var service = new FileSystemFileOrganizationService(tempDb.Journal, identities, restoreService);

        var result = await service.UndoAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("restore me", await File.ReadAllTextAsync(originalPath));
        Assert.Equal(FileOperationEventKind.Undone, (await tempDb.Journal.GetAsync(operation.Id))!.State);
        Assert.Equal(1, restoreService.CallCount);
    }

    [Fact]
    public async Task RecycleBinRestoreConflictLeavesTheExistingDestinationUntouched()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var originalPath = Path.Combine(workspace.Root, "restored.txt");
        var stagedRecycleItem = Path.Combine(workspace.Root, "recycle-item.bin");
        await File.WriteAllTextAsync(stagedRecycleItem, "recycle contents");
        var identities = new WindowsFileIdentityProvider();
        var originalIdentity = identities.GetIdentity(stagedRecycleItem).Identity!;
        var operation = await RecordRecycleBinOperationAsync(tempDb.Journal, originalPath, originalIdentity);
        await File.WriteAllTextAsync(originalPath, "existing contents");
        var restoreService = new FakeRecycleBinRestoreService(() => Task.CompletedTask);
        var service = new FileSystemFileOrganizationService(tempDb.Journal, identities, restoreService);

        var result = await service.UndoAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("existing contents", await File.ReadAllTextAsync(originalPath));
        Assert.Equal(0, restoreService.CallCount);
        Assert.Equal(FileOperationEventKind.Completed, (await tempDb.Journal.GetAsync(operation.Id))!.State);
    }

    [Fact]
    public async Task RecycleBinRestoreAcceptsMatchingContentWhenTheShellChangesTheFileIdentity()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var originalPath = Path.Combine(workspace.Root, "restored.txt");
        var stagedRecycleItem = Path.Combine(workspace.Root, "recycle-item.bin");
        await File.WriteAllTextAsync(stagedRecycleItem, "same verified bytes");
        var identities = new WindowsFileIdentityProvider();
        var originalIdentity = identities.GetIdentity(stagedRecycleItem).Identity!;
        var operation = await RecordRecycleBinOperationAsync(tempDb.Journal, originalPath, originalIdentity);
        var restoreService = new FakeRecycleBinRestoreService(() =>
        {
            File.Copy(stagedRecycleItem, originalPath);
            File.Delete(stagedRecycleItem);
            return Task.CompletedTask;
        }, restoredContentVerified: true);
        var service = new FileSystemFileOrganizationService(tempDb.Journal, identities, restoreService);

        var result = await service.UndoAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("content verified", result.Results.Single().Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("same verified bytes", await File.ReadAllTextAsync(originalPath));
        Assert.Equal(FileOperationEventKind.Undone, (await tempDb.Journal.GetAsync(operation.Id))!.State);
    }

    [Fact]
    public async Task MissingRecycleBinItemIsReportedWithoutChangingTheOriginalHistoryState()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var originalPath = Path.Combine(workspace.Root, "missing.txt");
        var stagedRecycleItem = Path.Combine(workspace.Root, "identity-source.bin");
        await File.WriteAllTextAsync(stagedRecycleItem, "identity source");
        var identities = new WindowsFileIdentityProvider();
        var identity = identities.GetIdentity(stagedRecycleItem).Identity!;
        var operation = await RecordRecycleBinOperationAsync(tempDb.Journal, originalPath, identity);
        var fake = new FakeRecycleBinRestoreService(() => Task.CompletedTask, RecycleBinRestoreOutcome.NotFound);
        var service = new FileSystemFileOrganizationService(tempDb.Journal, identities, fake);

        var result = await service.UndoAsync();

        Assert.Equal(FileOrganizationOutcome.Conflict, result.Results.Single().Outcome);
        Assert.False(File.Exists(originalPath));
        Assert.Equal(FileOperationEventKind.Completed, (await tempDb.Journal.GetAsync(operation.Id))!.State);
    }

    [Fact]
    public async Task BatchMoveUndoAndRedoOperateAsOneGroupInSafeOrder()
    {
        using var tempDb = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(tempDb);
        var sourceA = Path.Combine(workspace.Root, "a.txt");
        var sourceB = Path.Combine(workspace.Root, "b.txt");
        var destination = Directory.CreateDirectory(Path.Combine(workspace.Root, "target")).FullName;
        await File.WriteAllTextAsync(sourceA, "A");
        await File.WriteAllTextAsync(sourceB, "B");

        var moved = await workspace.Service.MoveFilesAsync([sourceA, sourceB], destination);
        Assert.True(moved.Succeeded, moved.Message);
        Assert.Equal(2, moved.Results.Count);
        Assert.NotNull(moved.BatchId);
        var grouped = await tempDb.Journal.GetBatchAsync(moved.BatchId!.Value);
        Assert.Equal(2, grouped.Count);

        var undone = await workspace.Service.UndoAsync();
        Assert.True(undone.Succeeded, undone.Message);
        Assert.Equal("A", await File.ReadAllTextAsync(sourceA));
        Assert.Equal("B", await File.ReadAllTextAsync(sourceB));

        var redone = await workspace.Service.RedoAsync();
        Assert.True(redone.Succeeded, redone.Message);
        Assert.Equal("A", await File.ReadAllTextAsync(Path.Combine(destination, "a.txt")));
        Assert.Equal("B", await File.ReadAllTextAsync(Path.Combine(destination, "b.txt")));

        var undoneAgain = await workspace.Service.UndoAsync();
        Assert.True(undoneAgain.Succeeded, undoneAgain.Message);
        Assert.Equal("A", await File.ReadAllTextAsync(sourceA));
        Assert.Equal("B", await File.ReadAllTextAsync(sourceB));
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

    [Fact]
    public async Task CancelledBatchRetainsCompletedMoveAndReportsEveryRemainingItem()
    {
        using var db = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(db);
        var target = Directory.CreateDirectory(Path.Combine(workspace.Root, "target")).FullName;
        var paths = new[] { "first.txt", "collision.txt", "last.txt" }.Select(name => Path.Combine(workspace.Root, name)).ToArray();
        foreach (var path in paths) File.WriteAllText(path, "source");
        File.WriteAllText(Path.Combine(target, "collision.txt"), "blocker");
        using var cancel = new CancellationTokenSource();
        var service = workspace.Service.WithCollisionResolver(new BatchCollisionResolver(() =>
        { cancel.Cancel(); return new(FileCollisionChoice.Cancel); }));
        var result = await service.MoveFilesAsync(paths, target, cancel.Token);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(FileOrganizationOutcome.Cancelled, result.Results[1].Outcome);
        Assert.Equal(1, result.UnattemptedCount);
        Assert.True(File.Exists(paths[1])); Assert.True(File.Exists(paths[2]));
        Assert.Equal("blocker", File.ReadAllText(Path.Combine(target, "collision.txt")));
        Assert.True((await workspace.Service.UndoAsync()).Succeeded);
        Assert.True(File.Exists(paths[0]));
        Assert.True((await workspace.Service.RedoAsync()).Succeeded);
    }

    [Fact]
    public async Task PreCancelledMoveAndRecycleReportUnattemptedWithoutMutations()
    {
        using var db = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(db);
        var source = Path.Combine(workspace.Root, "keep.txt"); File.WriteAllText(source, "keep");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var move = await workspace.Service.MoveFilesAsync([source], workspace.Root, cancel.Token);
        var recycle = await workspace.Service.SendFilesToRecycleBinAsync([source], cancel.Token);
        Assert.Equal(1, move.UnattemptedCount); Assert.Equal(1, recycle.UnattemptedCount);
        Assert.Empty(move.Results); Assert.Empty(recycle.Results);
        Assert.True(File.Exists(source)); Assert.Null(await db.Journal.GetNextUndoAsync());
    }

    [Fact]
    public async Task BatchContinuesAfterMissingSourceAndKeepsOneUndoGroupAcrossReopen()
    {
        using var db = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(db);
        var target = Directory.CreateDirectory(Path.Combine(workspace.Root, "target")).FullName;
        var a = Path.Combine(workspace.Root, "a.txt"); var b = Path.Combine(workspace.Root, "b.txt");
        File.WriteAllText(a, "a"); File.WriteAllText(b, "b");
        var result = await workspace.Service.MoveFilesAsync([a, Path.Combine(workspace.Root, "missing.txt"), b], target);
        Assert.Equal(2, result.SucceededCount); Assert.False(result.Succeeded); Assert.Equal(0, result.UnattemptedCount);
        var reopened = new FileSystemFileOrganizationService(new SqliteFileOperationJournal(new SqliteDatabase(db.DatabasePath)), new WindowsFileIdentityProvider());
        var undo = await reopened.UndoAsync(); Assert.Equal(result.BatchId, undo.BatchId); Assert.Equal(2, undo.SucceededCount);
        Assert.True(File.Exists(a)); Assert.True(File.Exists(b));
    }

    [Fact]
    public async Task GroupedRecycleIsJournaledAndRestoresAsOneGroupAcrossReopen()
    {
        using var db = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(db);
        var identities = new WindowsFileIdentityProvider();
        var service = new FileSystemFileOrganizationService(db.Journal, identities, new WindowsRecycleBinRestoreService(identities));
        var a = Path.Combine(workspace.Root, "recycle-a.txt"); var b = Path.Combine(workspace.Root, "recycle-b.txt");
        File.WriteAllText(a, "a"); File.WriteAllText(b, "b");
        var result = await service.SendFilesToRecycleBinAsync([a, b]);
        Assert.True(result.Succeeded, result.Message); Assert.False(File.Exists(a)); Assert.False(File.Exists(b));
        Assert.All(await db.Journal.GetBatchAsync(result.BatchId!.Value), item => Assert.Equal(FileOperationEventKind.Completed, item.State));
        var reopened = new FileSystemFileOrganizationService(new SqliteFileOperationJournal(new SqliteDatabase(db.DatabasePath)), identities, new WindowsRecycleBinRestoreService(identities));
        var undo = await reopened.UndoAsync(); Assert.True(undo.Succeeded, undo.Message); Assert.Equal(2, undo.SucceededCount);
        Assert.Equal("a", File.ReadAllText(a)); Assert.Equal("b", File.ReadAllText(b));
        var redo = await reopened.RedoAsync(); Assert.True(redo.Succeeded, redo.Message);
        Assert.True((await reopened.UndoAsync()).Succeeded);
    }

    [Fact]
    public async Task PartialGroupedRestoreRetainsFailedMemberForRetryAfterReopen()
    {
        using var db = new TemporaryDatabase();
        using var workspace = new TemporaryWorkspace(db);
        var identities = new WindowsFileIdentityProvider();
        var batch = Guid.NewGuid();
        var originals = new[] { "a.txt", "b.txt" }.Select(name => Path.Combine(workspace.Root, name)).ToArray();
        var staged = originals.Select(path => path + ".staged").ToArray();
        for (var i = 0; i < originals.Length; i++)
        {
            File.WriteAllText(staged[i], $"version {i}");
            var operation = new FileOperationRecord(Guid.NewGuid(), batch, FileOperationKind.SendToRecycleBin,
                originals[i], null, DateTimeOffset.UtcNow, 1,
                JsonSerializer.Serialize(new RecycleBinPayload(OriginalItemId: identities.GetIdentity(staged[i]).Identity, IsDirectory: false)));
            await db.Journal.AppendPreparedAsync(operation);
            await db.Journal.AppendEventAsync(operation.Id, FileOperationEventKind.Completed);
        }
        var restore = new PartialBatchRestore(originals, staged);
        var service = new FileSystemFileOrganizationService(db.Journal, identities, restore);
        var first = await service.UndoAsync();
        Assert.Equal(1, first.SucceededCount); Assert.False(first.Succeeded);
        Assert.False(File.Exists(originals[0])); Assert.True(File.Exists(originals[1]));
        restore.AllowFirst = true;
        var reopened = new FileSystemFileOrganizationService(new SqliteFileOperationJournal(new SqliteDatabase(db.DatabasePath)), identities, restore);
        var retry = await reopened.UndoAsync();
        Assert.Equal(batch, retry.BatchId); Assert.Single(retry.Results); Assert.True(retry.Succeeded, retry.Message);
        Assert.All(originals, path => Assert.True(File.Exists(path)));
    }

    private sealed class PartialBatchRestore(string[] originals, string[] staged) : IRecycleBinRestoreService
    {
        public bool AllowFirst;
        public Task<RecycleBinRestoreResult> RestoreAsync(string originalPath, string itemIdentity, bool isDirectory, CancellationToken cancellationToken = default)
        {
            var index = Array.IndexOf(originals, originalPath);
            if (index == 0 && !AllowFirst) return Task.FromResult(new RecycleBinRestoreResult(RecycleBinRestoreOutcome.NotFound, "Missing item retained in history."));
            File.Move(staged[index], originalPath);
            return Task.FromResult(new RecycleBinRestoreResult(RecycleBinRestoreOutcome.Restored, "Restored fixture."));
        }
    }

    private sealed class BatchCollisionResolver(Func<FileCollisionResolution> resolve) : IFileCollisionResolver
    {
        public Task<FileCollisionResolution> ResolveAsync(FileCollision collision, CancellationToken cancellationToken = default) => Task.FromResult(resolve());
    }

    [Fact]
    public async Task StructuralEchoVersionCheckRejectsReplacementWithIdenticalMetadata()
    {
        using var db = new TemporaryDatabase(); using var workspace = new TemporaryWorkspace(db);
        var path = Path.Combine(workspace.Root, "version.txt"); File.WriteAllText(path, "old");
        var identity = new WindowsFileIdentityProvider().GetIdentity(path).Identity!;
        var modified = File.GetLastWriteTimeUtc(path);
        Assert.True(await workspace.Service.MatchesFileVersionAsync(path, identity, 3, modified));
        File.Move(path, path + ".saved");
        Assert.True(await workspace.Service.MatchesFileVersionAsync(path, identity, 3, modified));
        File.WriteAllText(path, "new"); File.SetLastWriteTimeUtc(path, modified);
        Assert.False(await workspace.Service.MatchesFileVersionAsync(path, identity, 3, modified));
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

    private static async Task<FileOperationRecord> RecordRecycleBinOperationAsync(IFileOperationJournal journal,
        string originalPath, string identity)
    {
        var operation = new FileOperationRecord(Guid.NewGuid(), null, FileOperationKind.SendToRecycleBin,
            originalPath, null, DateTimeOffset.UtcNow, 1,
            JsonSerializer.Serialize(new RecycleBinPayload(OriginalItemId: identity, IsDirectory: false)));
        await journal.AppendPreparedAsync(operation);
        await journal.AppendEventAsync(operation.Id, FileOperationEventKind.Completed);
        return operation;
    }

    private sealed class FakeRecycleBinRestoreService(Func<Task> restore,
        RecycleBinRestoreOutcome outcome = RecycleBinRestoreOutcome.Restored,
        bool restoredContentVerified = false) : IRecycleBinRestoreService
    {
        public int CallCount { get; private set; }

        public async Task<RecycleBinRestoreResult> RestoreAsync(string originalPath, string itemIdentity, bool isDirectory,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            await restore();
            return new RecycleBinRestoreResult(outcome, $"Test adapter outcome: {outcome}.", restoredContentVerified);
        }
    }
}
