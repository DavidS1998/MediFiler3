using MediFiler3.Application.Operations;
using MediFiler3.Domain.Operations;
using MediFiler3.Infrastructure.Operations;
using MediFiler3.Infrastructure.Persistence;

namespace MediFiler3.Infrastructure.Tests;

public sealed class FileCollisionTests
{
    [Theory]
    [InlineData(FileCollisionChoice.Rename)]
    [InlineData(FileCollisionChoice.Overwrite)]
    public async Task CreateFolderCollisionsPreserveTheExistingTreeAndRoundTrip(FileCollisionChoice choice)
    {
        using var temp = new TemporaryDatabase();
        var parent = Path.GetDirectoryName(temp.DatabasePath)!;
        var existing = Directory.CreateDirectory(Path.Combine(parent, "docs")).FullName;
        File.WriteAllText(Path.Combine(existing, "old.txt"), "old folder contents");
        var service = new FileSystemFileOrganizationService(temp.Journal, new WindowsFileIdentityProvider(), backupStore: temp.Backups);
        var result = await service.WithCollisionResolver(new Resolver(_ => new(choice, "new-docs"))).CreateFolderAsync(parent, "docs");
        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(result.AffectedPath!));
        if (choice == FileCollisionChoice.Overwrite)
        {
            var roots = await new MediFiler3.Infrastructure.Folders.FileSystemFolderTreeService().LoadRootsAsync([parent], default);
            await foreach (var children in new MediFiler3.Infrastructure.Folders.FileSystemFolderTreeService().LoadHierarchyAsync(roots.Roots, default))
                Assert.DoesNotContain(children.Children, c => c.Name.StartsWith(".MediFiler3-Undo-"));
        }
        Assert.True((await service.UndoAsync()).Succeeded);
        Assert.Equal("old folder contents", File.ReadAllText(Path.Combine(existing, "old.txt")));
        Assert.True((await service.RedoAsync()).Succeeded);
    }

    [Fact]
    public async Task RecreateFolderRedoCollisionPreservesNewOccupantForNextUndo()
    {
        using var temp = new TemporaryDatabase();
        var parent = Path.GetDirectoryName(temp.DatabasePath)!;
        var service = new FileSystemFileOrganizationService(temp.Journal, new WindowsFileIdentityProvider(), backupStore: temp.Backups);
        var created = await service.CreateFolderAsync(parent, "docs");
        Assert.True((await service.UndoAsync()).Succeeded);
        Directory.CreateDirectory(created.AffectedPath!);
        File.WriteAllText(Path.Combine(created.AffectedPath!, "blocker.txt"), "blocker");
        var interactive = service.WithCollisionResolver(new Resolver(_ => new(FileCollisionChoice.Overwrite)));
        Assert.True((await interactive.RedoAsync()).Succeeded);
        Assert.Empty(Directory.EnumerateFileSystemEntries(created.AffectedPath!));
        Assert.True((await interactive.UndoAsync()).Succeeded);
        Assert.Equal("blocker", File.ReadAllText(Path.Combine(created.AffectedPath!, "blocker.txt")));
    }

    [Fact]
    public async Task RecycleBinRestoreCollisionPreservesTheOccupantAndReturnsItOnRedo()
    {
        using var temp = new TemporaryDatabase();
        var parent = Path.GetDirectoryName(temp.DatabasePath)!;
        var original = Path.Combine(parent, "recycled.txt");
        var holding = Path.Combine(parent, "recycled-held.txt");
        File.WriteAllText(original, "recycled contents");
        var identities = new WindowsFileIdentityProvider();
        var identity = identities.GetIdentity(original).Identity!;
        var operation = new FileOperationRecord(Guid.NewGuid(), null, FileOperationKind.SendToRecycleBin, original, null,
            DateTimeOffset.UtcNow, 1, System.Text.Json.JsonSerializer.Serialize(new RecycleBinPayload(OriginalItemId: identity)));
        await temp.Journal.AppendPreparedAsync(operation);
        File.Move(original, holding);
        await temp.Journal.AppendEventAsync(operation.Id, FileOperationEventKind.Completed);
        File.WriteAllText(original, "new occupant");
        var restore = new RestoreAdapter(() => File.Move(holding, original));
        var service = new FileSystemFileOrganizationService(temp.Journal, identities, restore,
            new Resolver(_ => new(FileCollisionChoice.Overwrite)), temp.Backups);
        Assert.True((await service.UndoAsync()).Succeeded);
        Assert.Equal("recycled contents", File.ReadAllText(original));
        Assert.True((await service.RedoAsync()).Succeeded);
        Assert.Equal("new occupant", File.ReadAllText(original));
    }

    [Fact]
    public async Task CancellingReplacementUndoStopsBeforeAnyBackupRestoration()
    {
        using var temp = new TemporaryDatabase();
        var (_, source, target, service) = Setup(temp);
        Assert.True((await service.WithCollisionResolver(new Resolver(_ => new(FileCollisionChoice.Overwrite)))
            .MoveFileAsync(source, Path.GetDirectoryName(target)!)).Succeeded);
        File.WriteAllText(source, "new source occupant");
        var cancelled = await service.WithCollisionResolver(new Resolver(_ => new(FileCollisionChoice.Cancel))).UndoAsync();
        Assert.Equal(FileOrganizationOutcome.Cancelled, Assert.Single(cancelled.Results).Outcome);
        Assert.Equal("incoming", File.ReadAllText(target));
        Assert.Equal("new source occupant", File.ReadAllText(source));
        Assert.Equal("existing", File.ReadAllText(await StoredBackupAsync(temp)));
    }

    [Fact]
    public async Task TamperedHistoryBackupIsNotRestoredAndHistoryRequiresReview()
    {
        using var temp = new TemporaryDatabase();
        var (_, source, _, service) = Setup(temp);
        var renamed = await service.RenameFileAsync(source, "renamed.txt");
        File.WriteAllText(source, "blocking");
        var interactive = service.WithCollisionResolver(new Resolver(_ => new(FileCollisionChoice.Overwrite)));
        Assert.True((await interactive.UndoAsync()).Succeeded);
        var entry = (await temp.Journal.GetAsync(renamed.OperationId!.Value))!;
        var details = System.Text.Json.JsonSerializer.Deserialize<OperationEventDetails>(entry.Events.Last().DetailsJson!)!;
        var backup = details.DisplacedItem!.BackupPath;
        File.Move(backup, backup + ".held");
        File.WriteAllText(backup, "unrelated replacement");
        var replay = await interactive.RedoAsync();
        Assert.Equal(FileOrganizationOutcome.RecoveryRequired, Assert.Single(replay.Results).Outcome);
        Assert.Equal("unrelated replacement", File.ReadAllText(backup));
        Assert.Equal("blocking", File.ReadAllText(backup + ".held"));
        Assert.Equal(FileOperationEventKind.RecoveryRequired, (await temp.Journal.GetAsync(entry.Operation.Id))!.State);
    }
    [Fact]
    public async Task CancelLeavesBothFilesAndJournalUntouched()
    {
        using var temp = new TemporaryDatabase();
        var (root, source, target, service) = Setup(temp);
        var resolver = new Resolver(_ => new(FileCollisionChoice.Cancel));
        var result = await service.WithCollisionResolver(resolver).MoveFileAsync(source, Path.GetDirectoryName(target)!);
        Assert.Equal(FileOrganizationOutcome.Cancelled, result.Outcome);
        Assert.Equal("incoming", File.ReadAllText(source));
        Assert.Equal("existing", File.ReadAllText(target));
        Assert.Null(await temp.Journal.GetNextUndoAsync());
        Assert.Empty(await temp.Journal.GetIncompleteAsync());
        Assert.Empty(Directory.GetFileSystemEntries(root, ".MediFiler3-Undo-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RenameIncomingRechecksCollisionsAndMovesWithoutChangingExistingFiles()
    {
        using var temp = new TemporaryDatabase();
        var (_, source, target, service) = Setup(temp);
        var folder = Path.GetDirectoryName(target)!;
        File.WriteAllText(Path.Combine(folder, "other.txt"), "other");
        var answers = new Queue<FileCollisionResolution>([new(FileCollisionChoice.Rename, "other.txt"), new(FileCollisionChoice.Rename, "unique.txt")]);
        var resolver = new Resolver(_ => answers.Dequeue());
        var result = await service.WithCollisionResolver(resolver).MoveFileAsync(source, folder);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(Path.Combine(folder, "unique.txt"), result.AffectedPath);
        Assert.Equal(2, resolver.Calls.Count);
        Assert.Equal("existing", File.ReadAllText(target));
        Assert.Equal("other", File.ReadAllText(Path.Combine(folder, "other.txt")));
        Assert.Equal("incoming", File.ReadAllText(result.AffectedPath!));
        Assert.True((await service.UndoAsync()).Succeeded);
        Assert.Equal("incoming", File.ReadAllText(source));
    }

    [Fact]
    public async Task MoveOverwriteIsOneUndoGroupAndRestoresBothVersionsAcrossReopen()
    {
        using var temp = new TemporaryDatabase();
        var (root, source, target, service) = Setup(temp);
        var beforeAttributes = File.GetAttributes(target);
        var overwrite = service.WithCollisionResolver(new Resolver(_ => new(FileCollisionChoice.Overwrite)));
        var result = await overwrite.MoveFileAsync(source, Path.GetDirectoryName(target)!);
        Assert.True(result.Succeeded, result.Message);
        Assert.False(File.Exists(source));
        Assert.Equal("incoming", File.ReadAllText(target));
        var backup = await StoredBackupAsync(temp);
        Assert.False(backup.StartsWith(Path.GetDirectoryName(target)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("existing", File.ReadAllText(backup));
        Assert.True(File.GetAttributes(backup).HasFlag(FileAttributes.Hidden));
        Assert.Equal(2, (await service.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo))!.Items.Count);
        var reopened = new FileSystemFileOrganizationService(new SqliteFileOperationJournal(new SqliteDatabase(temp.DatabasePath)), new WindowsFileIdentityProvider(), backupStore: temp.Backups);
        Assert.True((await reopened.UndoAsync()).Succeeded);
        Assert.Equal("incoming", File.ReadAllText(source));
        Assert.Equal("existing", File.ReadAllText(target));
        Assert.Equal(beforeAttributes, File.GetAttributes(target));
        Assert.False(File.Exists(backup));
        Assert.True((await reopened.RedoAsync()).Succeeded);
        Assert.Equal("incoming", File.ReadAllText(target));
        Assert.Equal("existing", File.ReadAllText(backup));
        Assert.True(File.GetAttributes(backup).HasFlag(FileAttributes.Hidden));
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public async Task RenameOverwriteAlsoRestoresBothNamesAndContents()
    {
        using var temp = new TemporaryDatabase();
        var (_, source, _, service) = Setup(temp);
        var destination = Path.Combine(Path.GetDirectoryName(source)!, "rename.txt");
        File.WriteAllText(destination, "existing rename");
        var result = await service.WithCollisionResolver(new Resolver(_ => new(FileCollisionChoice.Overwrite))).RenameFileAsync(source, "rename.txt");
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("incoming", File.ReadAllText(destination));
        Assert.True((await service.UndoAsync()).Succeeded);
        Assert.Equal("existing rename", File.ReadAllText(destination));
        Assert.Equal("incoming", File.ReadAllText(source));
        Assert.True((await service.RedoAsync()).Succeeded);
    }

    [Fact]
    public async Task FolderOverwritePreservesWholeDestinationAndRoundTrips()
    {
        using var temp = new TemporaryDatabase();
        var parent = Path.GetDirectoryName(temp.DatabasePath)!;
        var source = Directory.CreateDirectory(Path.Combine(parent, "incoming")).FullName;
        var destinationParent = Directory.CreateDirectory(Path.Combine(parent, "target")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(destinationParent, "incoming")).FullName;
        File.WriteAllText(Path.Combine(source, "new.txt"), "new");
        File.WriteAllText(Path.Combine(destination, "old.txt"), "old");
        var service = new FileSystemFileOrganizationService(temp.Journal, new WindowsFileIdentityProvider(), backupStore: temp.Backups);
        var result = await service.WithCollisionResolver(new Resolver(_ => new(FileCollisionChoice.Overwrite))).MoveFolderAsync(source, destinationParent);
        Assert.True(result.Succeeded, result.Message);
        Assert.True(File.Exists(Path.Combine(destination, "new.txt")));
        Assert.False(File.Exists(Path.Combine(destination, "old.txt")));
        var backup = await StoredBackupAsync(temp);
        Assert.Equal("old", File.ReadAllText(Path.Combine(backup, "old.txt")));
        Assert.True((await service.UndoAsync()).Succeeded);
        Assert.True(File.Exists(Path.Combine(source, "new.txt")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(destination, "old.txt")));
        Assert.False(File.GetAttributes(destination).HasFlag(FileAttributes.Hidden));
        Assert.True((await service.RedoAsync()).Succeeded);
    }

    [Fact]
    public async Task TypeMismatchCannotOverwriteEvenIfResolverRequestsIt()
    {
        using var temp = new TemporaryDatabase();
        var (_, source, target, service) = Setup(temp);
        File.Delete(target); Directory.CreateDirectory(target);
        var resolver = new Resolver(_ => new(FileCollisionChoice.Overwrite));
        var result = await service.WithCollisionResolver(resolver).MoveFileAsync(source, Path.GetDirectoryName(target)!);
        Assert.Equal(FileOrganizationOutcome.Conflict, result.Outcome);
        Assert.False(Assert.Single(resolver.Calls).CanOverwrite);
        Assert.True(File.Exists(source));
        Assert.True(Directory.Exists(target));
        Assert.Null(await temp.Journal.GetNextUndoAsync());
    }

    [Fact]
    public async Task DestinationIdentityChangeDuringPromptNeverDisplacesTheNewItem()
    {
        using var temp = new TemporaryDatabase();
        var (_, source, target, service) = Setup(temp);
        var resolver = new Resolver(_ =>
        {
            File.Move(target, target + ".old");
            File.WriteAllText(target, "appeared while prompting");
            return new(FileCollisionChoice.Overwrite);
        });
        await Assert.ThrowsAsync<IOException>(() => service.WithCollisionResolver(resolver).MoveFileAsync(source, Path.GetDirectoryName(target)!));
        Assert.Equal("incoming", File.ReadAllText(source));
        Assert.Equal("appeared while prompting", File.ReadAllText(target));
        Assert.Equal("existing", File.ReadAllText(target + ".old"));
        Assert.Null(await temp.Journal.GetNextUndoAsync());
    }

    [Fact]
    public async Task FailedIncomingMoveRollsBackThePreservedDestination()
    {
        using var temp = new TemporaryDatabase();
        var (_, source, target, service) = Setup(temp);
        // Allow identity reads, but deny deletion/renaming of the incoming file.
        using var locked = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = await service.WithCollisionResolver(new Resolver(_ => new(FileCollisionChoice.Overwrite))).MoveFileAsync(source, Path.GetDirectoryName(target)!);
        Assert.False(result.Succeeded);
        Assert.Equal("existing", File.ReadAllText(target));
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, ".MediFiler3-Undo-*"));
        Assert.Null(await temp.Journal.GetNextRedoAsync());
    }

    [Theory]
    [InlineData(FileCollisionChoice.Rename)]
    [InlineData(FileCollisionChoice.Overwrite)]
    public async Task UndoCollisionPreservesBlockingItemAndRedoRestoresIt(FileCollisionChoice choice)
    {
        using var temp = new TemporaryDatabase();
        var (_, source, _, service) = Setup(temp);
        var result = await service.RenameFileAsync(source, "renamed.txt");
        File.WriteAllText(source, "blocking original name");
        var resolver = new Resolver(_ => new(choice, "blocking-renamed.txt"));
        var interactive = service.WithCollisionResolver(resolver);
        Assert.True((await interactive.UndoAsync()).Succeeded);
        Assert.True(Assert.Single(resolver.Calls).IsHistory);
        Assert.Equal("incoming", File.ReadAllText(source));
        Assert.True((await interactive.RedoAsync()).Succeeded);
        Assert.Equal("blocking original name", File.ReadAllText(source));
        Assert.Equal("incoming", File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, "renamed.txt")));
        Assert.NotNull(await temp.Journal.GetAsync(result.OperationId!.Value));
    }

    [Fact]
    public async Task CancellingUndoCollisionDoesNotChangeAnyFilesOrHistory()
    {
        using var temp = new TemporaryDatabase();
        var (_, source, _, service) = Setup(temp);
        var result = await service.RenameFileAsync(source, "renamed.txt");
        File.WriteAllText(source, "blocking");
        var answer = await service.WithCollisionResolver(new Resolver(_ => new(FileCollisionChoice.Cancel))).UndoAsync();
        Assert.Equal(FileOrganizationOutcome.Cancelled, Assert.Single(answer.Results).Outcome);
        Assert.Equal("blocking", File.ReadAllText(source));
        Assert.Equal(FileOperationEventKind.Completed, (await temp.Journal.GetAsync(result.OperationId!.Value))!.State);
    }

    [Fact]
    public async Task RedoCollisionPreservesBlockerAndNextUndoRestoresIt()
    {
        using var temp = new TemporaryDatabase();
        var (_, source, _, service) = Setup(temp);
        var renamed = Path.Combine(Path.GetDirectoryName(source)!, "renamed.txt");
        await service.RenameFileAsync(source, "renamed.txt");
        Assert.True((await service.UndoAsync()).Succeeded);
        File.WriteAllText(renamed, "blocking redo");
        var interactive = service.WithCollisionResolver(new Resolver(_ => new(FileCollisionChoice.Overwrite)));
        Assert.True((await interactive.RedoAsync()).Succeeded);
        Assert.Equal("incoming", File.ReadAllText(renamed));
        Assert.True((await interactive.UndoAsync()).Succeeded);
        Assert.Equal("blocking redo", File.ReadAllText(renamed));
        Assert.Equal("incoming", File.ReadAllText(source));
    }

    private static (string Root, string Source, string Target, IFileOrganizationService Service) Setup(TemporaryDatabase temp)
    {
        var root = Path.GetDirectoryName(temp.DatabasePath)!;
        var a = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
        var source = Path.Combine(a, "file.txt"); var target = Path.Combine(b, "file.txt");
        File.WriteAllText(source, "incoming"); File.WriteAllText(target, "existing");
        return (root, source, target, new FileSystemFileOrganizationService(temp.Journal, new WindowsFileIdentityProvider(), backupStore: temp.Backups));
    }
    private sealed class Resolver(Func<FileCollision, FileCollisionResolution> answer) : IFileCollisionResolver
    {
        public List<FileCollision> Calls { get; } = [];
        public Task<FileCollisionResolution> ResolveAsync(FileCollision collision, CancellationToken cancellationToken = default)
        { Calls.Add(collision); return Task.FromResult(answer(collision)); }
    }
    private static async Task<string> StoredBackupAsync(TemporaryDatabase temp) => Assert.Single(
        await temp.Journal.GetAllAsync(), e => e.Operation.Kind is FileOperationKind.Move or FileOperationKind.Rename &&
            System.Text.Json.JsonSerializer.Deserialize<PathOperationPayload>(e.Operation.PayloadJson ?? "{}")?.IsReplacementBackup == true &&
            (File.Exists(e.Operation.DestinationPath) || Directory.Exists(e.Operation.DestinationPath))).Operation.DestinationPath!;
    private sealed class RestoreAdapter(Action restore) : IRecycleBinRestoreService
    {
        public Task<RecycleBinRestoreResult> RestoreAsync(string originalPath, string itemIdentity, bool isDirectory, CancellationToken cancellationToken = default)
        { restore(); return Task.FromResult(new RecycleBinRestoreResult(RecycleBinRestoreOutcome.Restored, "Test restore")); }
    }
}
