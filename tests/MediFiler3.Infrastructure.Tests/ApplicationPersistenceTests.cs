using MediFiler3.Application.Persistence;
using MediFiler3.Domain.Operations;
using MediFiler3.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace MediFiler3.Infrastructure.Tests;

public sealed class ApplicationPersistenceTests
{
    [Fact]
    public async Task FreshThumbnailSourceMarkerRoundTripsWithoutSchemaOrPayloadVersionChange()
    {
        using var temp = new TemporaryDatabase();
        var marker = new ThumbnailCacheEntry("fresh-key", "fresh-key.png", 0, DateTimeOffset.UtcNow,
            @"C:\library\photo.png", Invalidated: true, RequiresFreshSource: true);
        await new SqliteApplicationStateStore(temp.Database).SaveAsync(marker);
        var reopened = new SqliteApplicationStateStore(new SqliteDatabase(temp.DatabasePath));
        Assert.Equal(marker, Assert.Single(await ((IThumbnailCacheIndexStore)reopened).LoadAsync()));
        Assert.Equal(7L, await ScalarAsync(temp.DatabasePath, "PRAGMA user_version;"));
        Assert.Equal(1L, await ScalarAsync(temp.DatabasePath, "SELECT PayloadVersion FROM ThumbnailCacheIndex;"));
    }

    [Fact]
    public async Task GalleryDimensionsPersistIndependentlyAndOldSettingsUseDefaults()
    {
        using var temp = new TemporaryDatabase();
        var store = new SqliteApplicationStateStore(temp.Database);
        await store.SaveAsync(new ApplicationSettings(GalleryThumbnailWidth: 250, GalleryThumbnailHeight: 100));
        var service = new ApplicationSettingsService(new SqliteApplicationStateStore(new SqliteDatabase(temp.DatabasePath)));
        await service.InitializeAsync();
        Assert.Equal(250, service.Current.GalleryThumbnailWidth);
        Assert.Equal(100, service.Current.GalleryThumbnailHeight);
        await ExecuteAsync(temp.DatabasePath, "UPDATE ApplicationSettings SET PayloadJson = '{\"Muted\":true,\"Volume\":0.73,\"CarouselEnabled\":false}'");
        var old = await ((IApplicationSettingsStore)store).LoadAsync();
        Assert.Equal(150, old.GalleryThumbnailWidth);
        Assert.Equal(150, old.GalleryThumbnailHeight);
        Assert.True(old.Muted);
        Assert.False(old.CarouselEnabled);
        Assert.Equal(7L, await ScalarAsync(temp.DatabasePath, "PRAGMA user_version;"));
    }

    [Theory]
    [InlineData(63, 150)]
    [InlineData(401, 150)]
    [InlineData(150, 63)]
    [InlineData(150, 401)]
    [InlineData(125, 150)]
    [InlineData(150, 175)]
    public void InvalidGalleryDimensionsAreRejected(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(GalleryThumbnailWidth: width, GalleryThumbnailHeight: height).Validate());

    [Fact]
    public async Task LegacyGalleryDimensionsSnapWithoutLosingOtherSettingsOrOverwritingOnRead()
    {
        using var temp = new TemporaryDatabase();
        var store = new SqliteApplicationStateStore(temp.Database);
        await store.SaveAsync(new ApplicationSettings());
        await ExecuteAsync(temp.DatabasePath, "UPDATE ApplicationSettings SET PayloadJson = '{\"Muted\":true,\"Volume\":0.73,\"CarouselEnabled\":false,\"GalleryThumbnailWidth\":384,\"GalleryThumbnailHeight\":96}'");
        var settings = new ApplicationSettingsService(store);
        await settings.InitializeAsync();
        Assert.Equal(400, settings.Current.GalleryThumbnailWidth);
        Assert.Equal(100, settings.Current.GalleryThumbnailHeight);
        Assert.True(settings.Current.Muted);
        Assert.Equal(0.73, settings.Current.Volume);
        Assert.False(settings.Current.CarouselEnabled);
        await settings.FlushAsync();
        Assert.Contains("384", (string)(await ScalarAsync(temp.DatabasePath, "SELECT PayloadJson FROM ApplicationSettings;"))!);
        settings.Update(s => s with { GalleryThumbnailHeight = 350 });
        await settings.FlushAsync();
        var reopened = await ((IApplicationSettingsStore)new SqliteApplicationStateStore(new SqliteDatabase(temp.DatabasePath))).LoadAsync();
        Assert.Equal(400, reopened.GalleryThumbnailWidth);
        Assert.Equal(350, reopened.GalleryThumbnailHeight);
        Assert.True(reopened.Muted);
    }

    [Fact]
    public async Task CarouselVisibilityPersistsAndOldSettingsDefaultToVisible()
    {
        using var temp = new TemporaryDatabase();
        var store = new SqliteApplicationStateStore(new SqliteDatabase(temp.DatabasePath));
        var settings = new ApplicationSettings(CarouselEnabled: false);
        await store.SaveAsync(settings);
        var reopened = new SqliteApplicationStateStore(new SqliteDatabase(temp.DatabasePath));
        Assert.False((await ((IApplicationSettingsStore)reopened).LoadAsync()).CarouselEnabled);
        await ExecuteAsync(temp.DatabasePath, "UPDATE ApplicationSettings SET PayloadJson = '{\"Muted\":false,\"Volume\":0.5,\"CarouselIconCount\":11,\"FolderTreePinned\":false}'");
        Assert.True((await ((IApplicationSettingsStore)reopened).LoadAsync()).CarouselEnabled);
    }

    [Fact]
    public async Task OverscrollPreferencePersistsAndOlderSettingsKeepItEnabled()
    {
        using var temp = new TemporaryDatabase();
        var store = new SqliteApplicationStateStore(new SqliteDatabase(temp.DatabasePath));
        await store.SaveAsync(new ApplicationSettings(BrowseOverscrollEnabled: false));
        var reopened = (IApplicationSettingsStore)new SqliteApplicationStateStore(new SqliteDatabase(temp.DatabasePath));
        Assert.False((await reopened.LoadAsync()).BrowseOverscrollEnabled);
        await ExecuteAsync(temp.DatabasePath, "UPDATE ApplicationSettings SET PayloadJson = '{\"Muted\":false,\"Volume\":0.5}'");
        Assert.True((await reopened.LoadAsync()).BrowseOverscrollEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VersionOneUpgradePreservesLegacyStatesAndRollsBackOnFailure(bool failFirst)
    {
        using var temp = new TemporaryDatabase();
        var completed = Guid.NewGuid();
        var failed = Guid.NewGuid();
        var pending = Guid.NewGuid();
        await ExecuteAsync(temp.DatabasePath, $$"""
            CREATE TABLE FileOperations (
                Id TEXT NOT NULL PRIMARY KEY, BatchId TEXT NULL, Kind TEXT NOT NULL, Status TEXT NOT NULL,
                SourcePath TEXT NOT NULL, DestinationPath TEXT NULL, CreatedUtc TEXT NOT NULL,
                FinishedUtc TEXT NULL, Error TEXT NULL, PayloadJson TEXT NULL);
            INSERT INTO FileOperations VALUES
                ('{{completed:D}}', NULL, 'Move', 'Completed', 'source', 'target',
                 '2026-01-02T03:04:05.0000000+00:00', NULL, NULL, '{}'),
                ('{{failed:D}}', NULL, 'Move', 'Failed', 'source', 'target',
                 '2026-01-02T03:04:05.0000000+00:00', NULL, 'conflict', '{}'),
                ('{{pending:D}}', NULL, 'Move', 'Interrupted', 'source', 'target',
                 '2026-01-02T03:04:05.0000000+00:00', NULL, NULL, '{}');
            PRAGMA user_version = 1;
            """);
        var database = new SqliteDatabase(temp.DatabasePath);
        if (failFirst)
        {
            await ExecuteAsync(temp.DatabasePath, "CREATE TABLE WindowLayout (Unexpected TEXT);");
            await Assert.ThrowsAsync<SqliteException>(() => database.InitializeAsync());
            Assert.Equal(1L, await ScalarAsync(temp.DatabasePath, "PRAGMA user_version;"));
            Assert.Equal(3L, await ScalarAsync(temp.DatabasePath, "SELECT count(*) FROM FileOperations;"));
            Assert.Equal(0L, await ScalarAsync(temp.DatabasePath,
                "SELECT count(*) FROM sqlite_master WHERE name IN ('FileOperationsV1', 'FileOperationEvents', 'ApplicationSettings');"));
            await ExecuteAsync(temp.DatabasePath, "DROP TABLE WindowLayout;");
        }
        await database.InitializeAsync();
        var reopened = new SqliteDatabase(temp.DatabasePath);
        var journal = new SqliteFileOperationJournal(reopened);
        Assert.Equal(completed, (await journal.GetNextUndoAsync())!.Operation.Id);
        Assert.Null(await journal.GetNextRedoAsync());
        Assert.Equal(pending, Assert.Single(await journal.GetIncompleteAsync()).Operation.Id);
        Assert.Equal(FileOperationEventKind.Failed, (await journal.GetAsync(failed))!.State);
        Assert.Contains("conflict", (await journal.GetAsync(failed))!.Events.Last().DetailsJson);
        Assert.Equal(0, (await journal.GetAsync(completed))!.Operation.PayloadVersion);
        Assert.Equal(3L, await ScalarAsync(temp.DatabasePath, "SELECT count(*) FROM FileOperationsV1;"));
        await new SqliteApplicationStateStore(reopened).SaveAsync(new ApplicationSettings(Muted: true));
        Assert.Equal(7L, await ScalarAsync(temp.DatabasePath, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task DefaultsAndAllStateRoundTripAcrossReopen()
    {
        using var temp = new TemporaryDatabase();
        var store = new SqliteApplicationStateStore(temp.Database);
        Assert.Equal(new ApplicationSettings(), await ((IApplicationSettingsStore)store).LoadAsync());
        Assert.Null(await ((IWindowLayoutStore)store).LoadAsync());
        Assert.Empty(await ((IRootFolderHistoryStore)store).LoadAsync());
        Assert.Empty(await ((IThumbnailCacheIndexStore)store).LoadAsync());

        var settings = new ApplicationSettings(true, 0.73, 25, true);
        var layout = new WindowLayout(-1200, 20, 1000, 700, true, 280);
        var root = new RootFolderHistory(@"C:\missing\O'Brien 日本語", 7,
            DateTimeOffset.UtcNow, false, true);
        var cache = new ThumbnailCacheEntry("key'1", "thumbnail.png", 456, DateTimeOffset.UtcNow);
        await store.SaveAsync(settings);
        await store.SaveAsync(layout);
        await store.SaveAsync(root);
        await store.SaveAsync(cache);

        var reopened = new SqliteApplicationStateStore(new SqliteDatabase(temp.DatabasePath));
        Assert.Equal(settings, await ((IApplicationSettingsStore)reopened).LoadAsync());
        Assert.Equal(layout, await ((IWindowLayoutStore)reopened).LoadAsync());
        Assert.Equal(root, Assert.Single(await ((IRootFolderHistoryStore)reopened).LoadAsync()));
        Assert.Equal(cache, Assert.Single(await ((IThumbnailCacheIndexStore)reopened).LoadAsync()));
        await reopened.SaveAsync(settings with { Muted = false });
        Assert.Equal(0.73, (await ((IApplicationSettingsStore)reopened).LoadAsync()).Volume);
        await ((IThumbnailCacheIndexStore)reopened).RemoveAsync(cache.Key);
        Assert.Empty(await ((IThumbnailCacheIndexStore)reopened).LoadAsync());
    }

    [Fact]
    public async Task RootKeysIgnoreCaseAndTrailingSeparatorWithoutAvailabilityChecks()
    {
        using var temp = new TemporaryDatabase();
        var store = new SqliteApplicationStateStore(temp.Database);
        await store.SaveAsync(new RootFolderHistory(@"C:\MissingRoot\", 1, null, true, false));
        await store.SaveAsync(new RootFolderHistory(@"c:\missingroot", 2, null, false, true));
        var roots = await ((IRootFolderHistoryStore)store).LoadAsync();
        Assert.Equal(2, Assert.Single(roots).OpenCount);
        Assert.True(roots[0].Favorite);
        await ((IRootFolderHistoryStore)store).RemoveAsync(@"C:\MISSINGROOT\");
        Assert.Empty(await ((IRootFolderHistoryStore)store).LoadAsync());
    }

    [Fact]
    public async Task RejectsInvalidStateBeforeWriting()
    {
        using var temp = new TemporaryDatabase();
        var store = new SqliteApplicationStateStore(temp.Database);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(new ApplicationSettings(Volume: double.NaN)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(new ApplicationSettings(Volume: 1.1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(new ApplicationSettings(CarouselIconCount: 2)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(new WindowLayout(0, 0, 0, 100, false, 200)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new RootFolderHistory("relative", 0, null, false, false)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(new RootFolderHistory(@"C:\root", -1, null, false, false)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new ThumbnailCacheEntry("key", "../source.png", 0, DateTimeOffset.UtcNow)));
        Assert.Equal(new ApplicationSettings(), await ((IApplicationSettingsStore)store).LoadAsync());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task UpgradePreservesJournalRecordsEventsAndHistoryCandidates(int version)
    {
        using var temp = new TemporaryDatabase();
        var undo = NewOperation();
        var redo = NewOperation();
        var compensation = NewOperation(redo.Id);
        var pending = NewOperation(compensation: undo.Id);
        await temp.Journal.AppendPreparedAsync(undo);
        await temp.Journal.AppendEventAsync(undo.Id, FileOperationEventKind.Completed, "{\"proof\":1}");
        await temp.Journal.AppendPreparedAsync(redo);
        await temp.Journal.AppendEventAsync(redo.Id, FileOperationEventKind.Completed);
        await temp.Journal.AppendEventAsync(redo.Id, FileOperationEventKind.Undone);
        await temp.Journal.AppendPreparedAsync(compensation);
        await temp.Journal.AppendEventAsync(compensation.Id, FileOperationEventKind.Completed);
        await temp.Journal.AppendPreparedAsync(pending);
        var before = new List<JournaledFileOperation>();
        foreach (var operation in new[] { undo, redo, compensation, pending })
            before.Add((await temp.Journal.GetAsync(operation.Id))!);

        await MakeLegacyAsync(temp, version);
        SqliteConnection.ClearAllPools(); // Simulate restarting after constructing the legacy schema.
        var database = new SqliteDatabase(temp.DatabasePath);
        await database.InitializeAsync();
        await database.InitializeAsync();
        var journal = new SqliteFileOperationJournal(new SqliteDatabase(temp.DatabasePath));
        foreach (var original in before)
        {
            var actual = (await journal.GetAsync(original.Operation.Id))!;
            Assert.Equal(version == 2 ? original.Operation with { CompensatesOperationId = null } : original.Operation,
                actual.Operation);
            Assert.Equal(original.Events.ToArray(), actual.Events.ToArray());
        }
        // Version 2 did not support compensation links; its candidate semantics are preserved as such.
        Assert.Equal(version == 2 ? compensation.Id : undo.Id, (await journal.GetNextUndoAsync())!.Operation.Id);
        Assert.Equal(redo.Id, (await journal.GetNextRedoAsync())!.Operation.Id);
        Assert.Equal(pending.Id, Assert.Single(await journal.GetIncompleteAsync()).Operation.Id);
        await new SqliteApplicationStateStore(database).SaveAsync(new ApplicationSettings(Volume: 0.9));
        Assert.Equal(7L, await ScalarAsync(temp.DatabasePath, "PRAGMA user_version;"));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task FailedMigrationRollsBackAndCanBeRetried(int version)
    {
        using var temp = new TemporaryDatabase();
        await temp.Database.InitializeAsync();
        await MakeLegacyAsync(temp, version);
        SqliteConnection.ClearAllPools();
        // Force failure after journal migration and the first new state table have executed.
        await ExecuteAsync(temp.DatabasePath, "CREATE TABLE WindowLayout (Unexpected TEXT);");
        var database = new SqliteDatabase(temp.DatabasePath);
        await Assert.ThrowsAsync<SqliteException>(() => database.InitializeAsync());
        Assert.Equal((long)version, await ScalarAsync(temp.DatabasePath, "PRAGMA user_version;"));
        Assert.Equal(0L, await ScalarAsync(temp.DatabasePath,
            "SELECT count(*) FROM sqlite_master WHERE name = 'ApplicationSettings';"));
        if (version == 2)
            Assert.Equal(0L, await ScalarAsync(temp.DatabasePath,
                "SELECT count(*) FROM pragma_table_info('FileOperations') WHERE name = 'CompensatesOperationId';"));
        await ExecuteAsync(temp.DatabasePath, "DROP TABLE WindowLayout;");
        await database.InitializeAsync();
        Assert.Equal(7L, await ScalarAsync(temp.DatabasePath, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task RejectsNewerSchemaAndUnknownPayloadWithoutOverwritingThem()
    {
        using var temp = new TemporaryDatabase();
        await temp.Database.InitializeAsync();
        await ExecuteAsync(temp.DatabasePath, "PRAGMA user_version = 99;");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteDatabase(temp.DatabasePath).InitializeAsync());
        Assert.Equal(99L, await ScalarAsync(temp.DatabasePath, "PRAGMA user_version;"));
        await ExecuteAsync(temp.DatabasePath, "PRAGMA user_version = 6; INSERT INTO ApplicationSettings VALUES ('current', 99, '{}');");
        var store = (IApplicationSettingsStore)new SqliteApplicationStateStore(new SqliteDatabase(temp.DatabasePath));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        Assert.Equal(99L, await ScalarAsync(temp.DatabasePath, "SELECT PayloadVersion FROM ApplicationSettings;"));
    }

    private static FileOperationRecord NewOperation(Guid? compensation = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), FileOperationKind.Move, "source", "destination",
        DateTimeOffset.UtcNow, 1, "{\"identity\":\"preserved\"}", compensation);

    private static Task MakeLegacyAsync(TemporaryDatabase temp, int version) => ExecuteAsync(temp.DatabasePath,
        "DROP TABLE ApplicationSettings; DROP TABLE WindowLayout; DROP TABLE RootFolderHistory; DROP TABLE ThumbnailCacheIndex;" +
        "DROP INDEX IX_FileOperations_RootScope; ALTER TABLE FileOperations DROP COLUMN RootScope;" +
        (version == 2 ? """
            PRAGMA foreign_keys = OFF;
            CREATE TABLE LegacyOperations (
                Id TEXT NOT NULL PRIMARY KEY, BatchId TEXT NULL, Kind TEXT NOT NULL,
                SourcePath TEXT NULL, DestinationPath TEXT NULL, CreatedUtc TEXT NOT NULL,
                PayloadVersion INTEGER NOT NULL, PayloadJson TEXT NULL);
            INSERT INTO LegacyOperations SELECT Id, BatchId, Kind, SourcePath, DestinationPath,
                CreatedUtc, PayloadVersion, PayloadJson FROM FileOperations;
            DROP TABLE FileOperations;
            ALTER TABLE LegacyOperations RENAME TO FileOperations;
            """ : "") +
        $"PRAGMA user_version = {version};");

    private static async Task ExecuteAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
