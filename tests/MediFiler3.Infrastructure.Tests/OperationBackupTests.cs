using System.Text.Json;
using MediFiler3.Application.Operations;
using MediFiler3.Domain.Operations;
using MediFiler3.Infrastructure.Operations;
using MediFiler3.Infrastructure.Persistence;

namespace MediFiler3.Infrastructure.Tests;

public sealed class OperationBackupTests
{
    [Fact]
    public async Task InventoryMatchesCleanupAndReviewedIdsDoNotAuthorizeNewBackups()
    {
        using var temp = new TemporaryDatabase();
        await ReplaceAsync(temp);
        var item = Assert.Single(await temp.Backups.GetBackupsAsync());
        Assert.True(item.CanRemove); Assert.Null(item.RetainedReason);
        Assert.Equal(Path.Combine(Parent(temp), "target.txt"), item.OriginalPath);
        Assert.True(File.Exists(item.BackupPath));
        Assert.Equal(item.SavedUtc.AddDays(30), item.ExpiresUtc);
        await ReplaceAsync(temp);
        var all = await temp.Backups.GetBackupsAsync(); Assert.Equal(2, all.Count);
        var newer = all.Single(backup => backup.OperationId != item.OperationId);
        Assert.Equal(1, (await temp.Backups.ClearAsync([item.OperationId])).RemovedCount);
        Assert.False(File.Exists(item.BackupPath)); Assert.True(File.Exists(newer.BackupPath));
        Assert.Equal(newer.OperationId, Assert.Single(await temp.Backups.GetBackupsAsync()).OperationId);
    }

    [Fact]
    public async Task ExpiryUsesSavedJournalAgeAtThirtyDaysAndNeverOriginalFileTimestamps()
    {
        using var temp = new TemporaryDatabase();
        await ReplaceAsync(temp);
        var old = Assert.Single(await temp.Backups.GetBackupsAsync());
        File.SetLastWriteTimeUtc(old.BackupPath, DateTime.UtcNow.AddYears(1));
        await ReplaceAsync(temp);
        var recent = (await temp.Backups.GetBackupsAsync()).Single(item => item.OperationId != old.OperationId);
        File.SetLastWriteTimeUtc(recent.BackupPath, DateTime.UtcNow.AddYears(-1));
        var clock = new BackupClock(old.ExpiresUtc.AddTicks(-1));
        var store = new FileSystemOperationBackupStore(temp.Journal, new WindowsFileIdentityProvider(),
            Path.Combine(Parent(temp), "app-managed", "UndoBackups"), clock);
        Assert.Equal(0, (await store.ClearExpiredAsync()).RemovedCount);
        clock.Now = old.ExpiresUtc;
        Assert.Equal(1, (await store.ClearExpiredAsync()).RemovedCount);
        Assert.False(File.Exists(old.BackupPath)); Assert.True(File.Exists(recent.BackupPath));
        clock.Now = recent.ExpiresUtc;
        Assert.Equal(1, (await store.ClearExpiredAsync()).RemovedCount);
        Assert.Equal("new", File.ReadAllText(Path.Combine(Parent(temp), "target.txt")));
    }

    [Fact]
    public async Task ExpiryRetiresLockedHistoryBeforeFailureAndRetriesWithoutResettingAge()
    {
        using var temp = new TemporaryDatabase();
        var service = await ReplaceAsync(temp);
        var item = Assert.Single(await temp.Backups.GetBackupsAsync());
        var store = new FileSystemOperationBackupStore(temp.Journal, new WindowsFileIdentityProvider(),
            Path.Combine(Parent(temp), "app-managed", "UndoBackups"), new BackupClock(item.ExpiresUtc));
        using (var locked = File.Open(item.BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = await store.ClearExpiredAsync();
            Assert.Equal(0, result.RemovedCount); Assert.Single(result.Issues);
            Assert.Null(await service.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo));
        }
        Assert.Equal(1, (await store.ClearExpiredAsync()).RemovedCount);
    }

    private sealed class BackupClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task HostedRetentionRunsAtStartupAndStopsCleanly()
    {
        using var temp = new TemporaryDatabase();
        await ReplaceAsync(temp);
        var item = Assert.Single(await temp.Backups.GetBackupsAsync());
        var store = new FileSystemOperationBackupStore(temp.Journal, new WindowsFileIdentityProvider(),
            Path.Combine(Parent(temp), "app-managed", "UndoBackups"), new BackupClock(item.ExpiresUtc));
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Changed += () => completed.TrySetResult();
        using var worker = new OperationBackupRetentionService(store,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OperationBackupRetentionService>.Instance);
        await worker.StartAsync(CancellationToken.None);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
        Assert.False(File.Exists(item.BackupPath));
        Assert.True(File.Exists(Path.Combine(Parent(temp), "target.txt")));
    }

    [Fact]
    public async Task UndoRemovesTheStoredCandidateAndRedoStartsANewRetentionPeriod()
    {
        using var temp = new TemporaryDatabase();
        var service = await ReplaceAsync(temp);
        var first = Assert.Single(await temp.Backups.GetBackupsAsync());
        var clock = new BackupClock(first.ExpiresUtc);
        var store = new FileSystemOperationBackupStore(temp.Journal, new WindowsFileIdentityProvider(),
            Path.Combine(Parent(temp), "app-managed", "UndoBackups"), clock);
        Assert.True((await service.UndoAsync()).Succeeded);
        Assert.Empty(await store.GetBackupsAsync());
        Assert.Equal(0, (await store.ClearExpiredAsync()).RemovedCount);
        Assert.NotNull(await service.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Redo));
        Assert.True((await service.RedoAsync()).Succeeded);
        var savedAgain = Assert.Single(await store.GetBackupsAsync());
        Assert.True(savedAgain.SavedUtc > first.SavedUtc);
        Assert.Equal(0, (await store.ClearExpiredAsync()).RemovedCount);
        clock.Now = savedAgain.ExpiresUtc;
        Assert.Equal(1, (await store.ClearExpiredAsync()).RemovedCount);
    }
    [Fact]
    public async Task ClearingBackupsRetiresWholeGroupsAcrossRootsButKeepsUnrelatedHistoryAndFiles()
    {
        using var temp = new TemporaryDatabase();
        var a = Directory.CreateDirectory(Path.Combine(Parent(temp), "A")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(Parent(temp), "B")).FullName;
        var c = Directory.CreateDirectory(Path.Combine(Parent(temp), "C")).FullName;
        var service = Service(temp);
        var sa = await service.ForRootAsync(a);
        var sb = await service.ForRootAsync(b);
        var sc = await service.ForRootAsync(c);
        foreach (var (root, scoped) in new[] { (a, sa), (b, sb) })
        {
            File.WriteAllText(Path.Combine(root, "incoming.txt"), "new");
            File.WriteAllText(Path.Combine(root, "target.txt"), "old");
            Assert.True((await scoped.WithCollisionResolver(new Overwrite()).RenameFileAsync(Path.Combine(root, "incoming.txt"), "target.txt")).Succeeded);
        }
        File.WriteAllText(Path.Combine(c, "unrelated.txt"), "keep");
        await sc.RenameFileAsync(Path.Combine(c, "unrelated.txt"), "renamed.txt");
        var before = await temp.Journal.GetAllAsync();
        var backups = BackupEntries(before).ToArray();
        Assert.Equal(2, backups.Length);
        Assert.All(backups, entry => Assert.Contains(Path.Combine("app-managed", "UndoBackups"), entry.Operation.DestinationPath));
        var result = await temp.Backups.ClearAsync();
        Assert.Equal(2, result.RemovedCount);
        Assert.Empty(result.Issues);
        Assert.Null(await sa.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo));
        Assert.Null(await sb.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo));
        Assert.NotNull(await sc.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo));
        Assert.All(new[] { a, b }, root => Assert.Equal("new", File.ReadAllText(Path.Combine(root, "target.txt"))));
        Assert.All(backups, entry => Assert.False(File.Exists(entry.Operation.DestinationPath)));
        var reopened = new SqliteFileOperationJournal(new SqliteDatabase(temp.DatabasePath));
        Assert.Equal(before.Count, (await reopened.GetAllAsync()).Count); // Intents and events are retained.
        Assert.Null(await reopened.ForRoot(new WindowsFileIdentityProvider().GetIdentity(a).Identity!).GetNextUndoAsync());
        Assert.Empty(await reopened.GetIncompleteAsync());
        Assert.True((await sc.UndoAsync()).Succeeded);
    }

    [Fact]
    public async Task FolderCleanupRemovesOwnedTreeIncludingReadonlyFiles()
    {
        using var temp = new TemporaryDatabase();
        var parent = Parent(temp);
        var source = Directory.CreateDirectory(Path.Combine(parent, "tree")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(parent, "target", "tree")).FullName;
        File.WriteAllText(Path.Combine(source, "new.txt"), "new");
        var old = Path.Combine(destination, "old.txt");
        File.WriteAllText(old, "old");
        File.SetAttributes(old, FileAttributes.ReadOnly);
        var service = Service(temp).WithCollisionResolver(new Overwrite());
        Assert.True((await service.MoveFolderAsync(source, Path.GetDirectoryName(destination)!)).Succeeded);
        var backup = Assert.Single(BackupEntries(await temp.Journal.GetAllAsync())).Operation.DestinationPath!;
        Assert.True(Directory.Exists(backup));
        var result = await temp.Backups.ClearAsync();
        Assert.Equal(1, result.RemovedCount);
        Assert.Empty(result.Issues);
        Assert.False(Directory.Exists(backup));
        Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "new.txt")));
        Assert.Null(await service.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo));
    }

    [Fact]
    public async Task UndoneBackupIsNeverDeletedFromItsRestoredUserPathAndRedoStillWorks()
    {
        using var temp = new TemporaryDatabase();
        var service = await ReplaceAsync(temp);
        Assert.True((await service.UndoAsync()).Succeeded);
        var result = await temp.Backups.ClearAsync();
        Assert.Equal(0, result.RemovedCount);
        Assert.Empty(result.Issues);
        Assert.Equal("old", File.ReadAllText(Path.Combine(Parent(temp), "target.txt")));
        Assert.Empty(await temp.Backups.GetBackupsAsync());
        Assert.NotNull(await service.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Redo));
        Assert.True((await service.RedoAsync()).Succeeded);
    }

    [Fact]
    public async Task HistoryOverwriteCleanupRetiresTheOwningOperation()
    {
        using var temp = new TemporaryDatabase();
        var path = Path.Combine(Parent(temp), "file.txt");
        File.WriteAllText(path, "original");
        var service = Service(temp).WithCollisionResolver(new Overwrite());
        await service.RenameFileAsync(path, "renamed.txt");
        File.WriteAllText(path, "blocker");
        Assert.True((await service.UndoAsync()).Succeeded);
        Assert.NotNull(await service.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Redo));
        var result = await temp.Backups.ClearAsync();
        Assert.Equal(1, result.RemovedCount);
        Assert.Empty(result.Issues);
        Assert.Null(await service.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Redo));
        Assert.Equal("original", File.ReadAllText(path));
    }

    [Fact]
    public async Task ChangedBackupIdentityIsRetainedAndReported()
    {
        using var temp = new TemporaryDatabase();
        var service = await ReplaceAsync(temp);
        var backup = Assert.Single(BackupEntries(await temp.Journal.GetAllAsync())).Operation.DestinationPath!;
        File.Move(backup, backup + ".held");
        File.WriteAllText(backup, "unrelated");
        var result = await temp.Backups.ClearAsync();
        Assert.Equal(0, result.RemovedCount);
        Assert.Single(result.Issues);
        Assert.Equal("unrelated", File.ReadAllText(backup));
        Assert.Equal("old", File.ReadAllText(backup + ".held"));
        Assert.NotNull(await service.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo));
    }

    [Fact]
    public async Task LockedBackupRetiresHistoryBeforeFailureAndCanBeClearedOnRetry()
    {
        using var temp = new TemporaryDatabase();
        var service = await ReplaceAsync(temp);
        var backup = Assert.Single(BackupEntries(await temp.Journal.GetAllAsync())).Operation.DestinationPath!;
        using (var locked = File.Open(backup, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = await temp.Backups.ClearAsync();
            Assert.Single(result.Issues);
            Assert.Equal(0, result.RemovedCount);
            Assert.Null(await service.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo));
        }
        var retry = await temp.Backups.ClearAsync();
        Assert.Equal(1, retry.RemovedCount);
        Assert.Empty(retry.Issues);
    }

    [Fact]
    public async Task LegacySiblingBackupCanBeClearedButUntrackedLookalikesAreUntouched()
    {
        using var temp = new TemporaryDatabase();
        var path = Path.Combine(Parent(temp), ".MediFiler3-Undo-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "old backup");
        var untracked = Path.Combine(Parent(temp), ".MediFiler3-Undo-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(untracked, "user file");
        var operation = new FileOperationRecord(Guid.NewGuid(), null, FileOperationKind.Rename,
            Path.Combine(Parent(temp), "old.txt"), path, DateTimeOffset.UtcNow, 1,
            JsonSerializer.Serialize(new PathOperationPayload(false, false, new WindowsFileIdentityProvider().GetIdentity(path).Identity, true)));
        await temp.Journal.AppendPreparedAsync(operation);
        await temp.Journal.AppendEventAsync(operation.Id, FileOperationEventKind.Completed);
        var result = await temp.Backups.ClearAsync();
        Assert.Equal(1, result.RemovedCount);
        Assert.Empty(result.Issues);
        Assert.False(File.Exists(path));
        Assert.Equal("user file", File.ReadAllText(untracked));
    }

    [Fact]
    public async Task RecoveryRequiredBackupIsRetained()
    {
        using var temp = new TemporaryDatabase();
        await ReplaceAsync(temp);
        var backup = Assert.Single(BackupEntries(await temp.Journal.GetAllAsync()));
        await temp.Journal.AppendEventAsync(backup.Operation.Id, FileOperationEventKind.RecoveryRequired);
        Assert.False(Assert.Single(await temp.Backups.GetBackupsAsync()).CanRemove);
        var result = await temp.Backups.ClearAsync();
        Assert.Equal(0, result.RemovedCount);
        Assert.Single(result.Issues);
        Assert.True(File.Exists(backup.Operation.DestinationPath));
    }

    [Fact]
    public async Task UnresolvedCompensationPreventsGroupRetirementAndDeletion()
    {
        using var temp = new TemporaryDatabase();
        var service = await ReplaceAsync(temp);
        var backup = Assert.Single(BackupEntries(await temp.Journal.GetAllAsync()));
        var compensation = new FileOperationRecord(Guid.NewGuid(), backup.Operation.BatchId, FileOperationKind.Rename,
            "source", "target", DateTimeOffset.UtcNow, 1, "{}", backup.Operation.Id);
        await temp.Journal.AppendPreparedAsync(compensation);
        await temp.Journal.AppendEventAsync(compensation.Id, FileOperationEventKind.RecoveryRequired);
        Assert.False(Assert.Single(await temp.Backups.GetBackupsAsync()).CanRemove);
        var result = await temp.Backups.ClearAsync();
        Assert.Equal(0, result.RemovedCount);
        Assert.Single(result.Issues);
        Assert.True(File.Exists(backup.Operation.DestinationPath));
        Assert.DoesNotContain(await temp.Journal.GetAllAsync(), entry => entry.State == FileOperationEventKind.HistoryDiscarded);
        Assert.NotNull(await service.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo));
    }

    [Fact]
    public async Task UnavailableManagedStorageLeavesBothOriginalsUnchanged()
    {
        using var temp = new TemporaryDatabase();
        var parent = Parent(temp);
        var source = Path.Combine(parent, "incoming.txt");
        var destination = Path.Combine(parent, "target.txt");
        File.WriteAllText(source, "new");
        File.WriteAllText(destination, "old");
        var blocked = Path.Combine(parent, "blocked-store");
        File.WriteAllText(blocked, "not a directory");
        var store = new FileSystemOperationBackupStore(temp.Journal, new WindowsFileIdentityProvider(), blocked);
        var service = new FileSystemFileOrganizationService(temp.Journal, new WindowsFileIdentityProvider(),
            collisionResolver: new Overwrite(), backupStore: store);
        await Assert.ThrowsAsync<IOException>(() => service.RenameFileAsync(source, "target.txt"));
        Assert.Equal("new", File.ReadAllText(source));
        Assert.Equal("old", File.ReadAllText(destination));
        Assert.Empty(await temp.Journal.GetAllAsync());
    }

    [Fact]
    public async Task CleanupWaitsForInFlightReplacement()
    {
        using var temp = new TemporaryDatabase();
        var parent = Parent(temp);
        File.WriteAllText(Path.Combine(parent, "incoming.txt"), "new");
        File.WriteAllText(Path.Combine(parent, "target.txt"), "old");
        var resolver = new PausedResolver();
        var service = Service(temp).WithCollisionResolver(resolver);
        var replace = service.RenameFileAsync(Path.Combine(parent, "incoming.txt"), "target.txt");
        await resolver.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cleanup = temp.Backups.ClearAsync();
        Assert.False(cleanup.IsCompleted);
        resolver.Answer.SetResult(new(FileCollisionChoice.Overwrite));
        Assert.True((await replace).Succeeded);
        var result = await cleanup;
        Assert.Equal(1, result.RemovedCount);
        Assert.Null(await service.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo));
    }

    private static string Parent(TemporaryDatabase temp) => Path.GetDirectoryName(temp.DatabasePath)!;
    private static IFileOrganizationService Service(TemporaryDatabase temp) =>
        new FileSystemFileOrganizationService(temp.Journal, new WindowsFileIdentityProvider(), backupStore: temp.Backups);
    private static IEnumerable<JournaledFileOperation> BackupEntries(IReadOnlyList<JournaledFileOperation> entries) =>
        entries.Where(e => e.Operation.Kind is FileOperationKind.Move or FileOperationKind.Rename &&
            JsonSerializer.Deserialize<PathOperationPayload>(e.Operation.PayloadJson ?? "{}")?.IsReplacementBackup == true);
    private static async Task<IFileOrganizationService> ReplaceAsync(TemporaryDatabase temp)
    {
        var service = Service(temp).WithCollisionResolver(new Overwrite());
        File.WriteAllText(Path.Combine(Parent(temp), "incoming.txt"), "new");
        File.WriteAllText(Path.Combine(Parent(temp), "target.txt"), "old");
        Assert.True((await service.RenameFileAsync(Path.Combine(Parent(temp), "incoming.txt"), "target.txt")).Succeeded);
        return service;
    }
    private sealed class Overwrite : IFileCollisionResolver
    {
        public Task<FileCollisionResolution> ResolveAsync(FileCollision collision, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileCollisionResolution(FileCollisionChoice.Overwrite));
    }
    private sealed class PausedResolver : IFileCollisionResolver
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<FileCollisionResolution> Answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<FileCollisionResolution> ResolveAsync(FileCollision collision, CancellationToken cancellationToken = default)
        { Started.SetResult(); return Answer.Task; }
    }
}
