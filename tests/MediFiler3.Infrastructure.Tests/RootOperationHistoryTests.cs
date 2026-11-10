using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Operations;
using MediFiler3.Application.Persistence;
using MediFiler3.Domain.Operations;
using MediFiler3.Infrastructure.Operations;
using MediFiler3.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace MediFiler3.Infrastructure.Tests;

public sealed class RootOperationHistoryTests
{
    [Fact]
    public async Task UndoRedoAndBranchInvalidationAreIndependentAcrossRootsAndReopen()
    {
        using var temp = new TemporaryDatabase();
        var (a, b, service) = Setup(temp);
        File.WriteAllText(Path.Combine(a, "a.txt"), "A");
        File.WriteAllText(Path.Combine(b, "b.txt"), "B");
        var sa = await service.ForRootAsync(a);
        var sb = await service.ForRootAsync(b);
        Assert.True((await sa.RenameFileAsync(Path.Combine(a, "a.txt"), "a1.txt")).Succeeded);
        Assert.True((await sb.RenameFileAsync(Path.Combine(b, "b.txt"), "b1.txt")).Succeeded);
        Assert.True((await sa.UndoAsync()).Succeeded);
        Assert.True(File.Exists(Path.Combine(a, "a.txt")));
        Assert.True(File.Exists(Path.Combine(b, "b1.txt")));
        Assert.True((await sb.UndoAsync()).Succeeded);
        Assert.NotNull(await sa.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Redo));
        Assert.True((await sa.RenameFileAsync(Path.Combine(a, "a.txt"), "a2.txt")).Succeeded);
        Assert.Null(await sa.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Redo));
        Assert.NotNull(await sb.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Redo));
        var reopened = new FileSystemFileOrganizationService(new SqliteFileOperationJournal(new SqliteDatabase(temp.DatabasePath)),
            new WindowsFileIdentityProvider());
        Assert.True((await (await reopened.ForRootAsync(b)).RedoAsync()).Succeeded);
        Assert.True(File.Exists(Path.Combine(b, "b1.txt")));
        Assert.True(File.Exists(Path.Combine(a, "a2.txt")));
        Assert.Null(await temp.Journal.GetNextUndoAsync()); // No root-scoped records leak into legacy history.
    }

    [Fact]
    public async Task CrossRootBatchBelongsToSourceRootIncludingCompensations()
    {
        using var temp = new TemporaryDatabase();
        var (a, b, service) = Setup(temp);
        var files = new[] { Path.Combine(a, "1.txt"), Path.Combine(a, "2.txt") };
        foreach (var path in files) File.WriteAllText(path, path);
        var sa = await service.ForRootAsync(a);
        var sb = await service.ForRootAsync(b);
        var moved = await sa.MoveFilesAsync(files, b);
        Assert.True(moved.Succeeded);
        Assert.Null(await sb.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo));
        Assert.False((await sb.UndoAsync()).Succeeded);
        Assert.All(files, path => Assert.False(File.Exists(path)));
        var preview = await sa.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo);
        Assert.Equal(2, preview!.Items.Count);
        Assert.True((await sa.UndoAsync()).Succeeded);
        Assert.All(files, path => Assert.True(File.Exists(path)));
        Assert.True((await sa.RedoAsync()).Succeeded);
        Assert.All(files, path => Assert.True(File.Exists(Path.Combine(b, Path.GetFileName(path)))));
        var scope = new WindowsFileIdentityProvider().GetIdentity(a).Identity!;
        var entries = await temp.Journal.ForRoot(scope).GetBatchAsync(moved.BatchId!.Value);
        Assert.All(entries, entry => Assert.Equal(scope, entry.Operation.RootScope));
        Assert.Contains(entries, entry => entry.Operation.CompensatesOperationId is not null);
    }

    [Fact]
    public async Task RenamingRootRetainsHistoryByFilesystemIdentity()
    {
        using var temp = new TemporaryDatabase();
        var (a, _, service) = Setup(temp);
        var renamed = Path.Combine(Path.GetDirectoryName(a)!, "renamed");
        Assert.True((await (await service.ForRootAsync(a)).RenameFolderAsync(a, "renamed")).Succeeded);
        var renamedScope = await service.ForRootAsync(renamed);
        Assert.NotNull(await renamedScope.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo));
        Assert.True((await renamedScope.UndoAsync()).Succeeded);
        Assert.True(Directory.Exists(a));
        Assert.True((await (await service.ForRootAsync(a)).RedoAsync()).Succeeded);
        Assert.True(Directory.Exists(renamed));
    }

    [Fact]
    public async Task JournalFiltersMixedBatchesAndRejectsCrossRootCompensationAndEvents()
    {
        using var temp = new TemporaryDatabase();
        var a = temp.Journal.ForRoot("A");
        var b = temp.Journal.ForRoot("B");
        var batch = Guid.NewGuid();
        var first = Record(batch);
        var second = Record(batch);
        await a.AppendPreparedAsync(first);
        await a.AppendEventAsync(first.Id, FileOperationEventKind.Completed);
        await b.AppendPreparedAsync(second);
        await b.AppendEventAsync(second.Id, FileOperationEventKind.Completed);
        Assert.Equal(first.Id, Assert.Single(await a.GetBatchAsync(batch)).Operation.Id);
        Assert.Equal(second.Id, Assert.Single(await b.GetBatchAsync(batch)).Operation.Id);
        Assert.Null(await a.GetAsync(second.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => a.AppendEventAsync(second.Id, FileOperationEventKind.Undone));
        await Assert.ThrowsAsync<InvalidOperationException>(() => a.AppendPreparedAsync(Record(batch) with { CompensatesOperationId = second.Id }));
        Assert.Equal(FileOperationEventKind.Completed, (await b.GetAsync(second.Id))!.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VersionFourUpgradePreservesLegacyJournalAndSettingsWithRollbackRetry(bool failFirst)
    {
        using var temp = new TemporaryDatabase();
        var operation = Record(null);
        await temp.Journal.AppendPreparedAsync(operation);
        await temp.Journal.AppendEventAsync(operation.Id, FileOperationEventKind.Completed);
        var before = (await temp.Journal.GetAsync(operation.Id))!;
        var settings = new ApplicationSettings(true, 0.7, 17, true);
        await new SqliteApplicationStateStore(temp.Database).SaveAsync(settings);
        await SqlAsync(temp.DatabasePath, "DROP INDEX IX_FileOperations_RootScope; ALTER TABLE FileOperations DROP COLUMN RootScope; PRAGMA user_version = 4;");
        SqliteConnection.ClearAllPools(); // Simulate process restart after constructing the old-schema fixture.
        var database = new SqliteDatabase(temp.DatabasePath);
        if (failFirst)
        {
            await SqlAsync(temp.DatabasePath, "CREATE TABLE MigrationBlock (Value TEXT); CREATE INDEX IX_FileOperations_RootScope ON MigrationBlock(Value);");
            await Assert.ThrowsAsync<SqliteException>(() => database.InitializeAsync());
            Assert.Equal(4L, await ScalarAsync(temp.DatabasePath, "PRAGMA user_version;"));
            Assert.Equal(0L, await ScalarAsync(temp.DatabasePath, "SELECT count(*) FROM pragma_table_info('FileOperations') WHERE name='RootScope';"));
            await SqlAsync(temp.DatabasePath, "DROP INDEX IX_FileOperations_RootScope;");
        }
        await database.InitializeAsync();
        var journal = new SqliteFileOperationJournal(new SqliteDatabase(temp.DatabasePath));
        var after = (await journal.GetAsync(operation.Id))!;
        Assert.Equal(before.Operation, after.Operation);
        Assert.Equal(before.Events.ToArray(), after.Events.ToArray());
        Assert.NotNull(await journal.GetNextUndoAsync());
        Assert.Null(await journal.ForRoot("root-A").GetNextUndoAsync());
        Assert.Equal(settings, await ((IApplicationSettingsStore)new SqliteApplicationStateStore(database)).LoadAsync());
        Assert.Equal(7L, await ScalarAsync(temp.DatabasePath, "PRAGMA user_version;"));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public async Task CompatibilityUpgradePreservesScopedHistoryAndSettings(int version)
    {
        using var temp = new TemporaryDatabase();
        var journal = temp.Journal.ForRoot("root-A");
        var operation = Record(null);
        await journal.AppendPreparedAsync(operation);
        await journal.AppendEventAsync(operation.Id, FileOperationEventKind.Completed);
        var before = (await journal.GetAsync(operation.Id))!;
        var settings = new ApplicationSettings(true, 0.7, 17, true);
        await new SqliteApplicationStateStore(temp.Database).SaveAsync(settings);
        await SqlAsync(temp.DatabasePath, $"PRAGMA user_version = {version};");
        SqliteConnection.ClearAllPools();

        var reopened = new SqliteDatabase(temp.DatabasePath);
        var migrated = new SqliteFileOperationJournal(reopened).ForRoot("root-A");
        var after = (await migrated.GetAsync(operation.Id))!;
        Assert.Equal(before.Operation, after.Operation);
        Assert.Equal(before.Events.ToArray(), after.Events.ToArray());
        Assert.NotNull(await migrated.GetNextUndoAsync());
        Assert.Equal(settings, await ((IApplicationSettingsStore)new SqliteApplicationStateStore(reopened)).LoadAsync());
        Assert.Equal(7L, await ScalarAsync(temp.DatabasePath, "PRAGMA user_version;"));
    }

    [Fact]
    public void ActiveRootUsesNodeAncestryForOverlappingRoots()
    {
        var outerChild = new FolderTreeEntry("inner", @"C:\outer\inner");
        var outer = new FolderTreeEntry("outer", @"C:\outer", [outerChild], isRoot: true);
        var innerChild = new FolderTreeEntry("child", @"C:\outer\inner\child");
        var inner = new FolderTreeEntry("inner", @"C:\outer\inner", [innerChild], isRoot: true);
        var state = new NavigationSessionState();
        state.ReplaceRoots([outer, inner]);
        state.SetActiveFolder(outerChild);
        Assert.Same(outer, state.ActiveRoot);
        state.SetActiveFolder(innerChild);
        Assert.Same(inner, state.ActiveRoot);
        state.ReplaceRoots([outer], preserveActiveContext: true);
        Assert.Null(state.ActiveRoot);
    }

    private static (string A, string B, IFileOrganizationService Service) Setup(TemporaryDatabase temp)
    {
        var parent = Path.GetDirectoryName(temp.DatabasePath)!;
        return (Directory.CreateDirectory(Path.Combine(parent, "A")).FullName,
            Directory.CreateDirectory(Path.Combine(parent, "B")).FullName,
            new FileSystemFileOrganizationService(temp.Journal, new WindowsFileIdentityProvider()));
    }
    private static FileOperationRecord Record(Guid? batch) => new(Guid.NewGuid(), batch, FileOperationKind.Move,
        "source", "target", DateTimeOffset.UtcNow, 1, "{}");
    private static async Task SqlAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<object?> ScalarAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
