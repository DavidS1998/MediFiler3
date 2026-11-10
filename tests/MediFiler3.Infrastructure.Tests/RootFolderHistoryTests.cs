using MediFiler3.Application.Folders;
using MediFiler3.Application.Persistence;
using MediFiler3.Infrastructure.Folders;
using MediFiler3.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace MediFiler3.Infrastructure.Tests;

public sealed class RootFolderHistoryTests
{
    [Fact]
    public async Task OpensShareNormalizedIdentityIncrementCountsAndUpdateLastVisitAcrossRestart()
    {
        using var temp = new TemporaryDatabase();
        var clock = new Clock();
        var service = NewService(temp, clock: clock);
        await service.RecordOpenedRootsAsync([@"C:\Root\", @"c:\root", @"C:\other"]);
        var first = (await service.GetListsAsync()).Recent.Single(r => r.Path == @"C:\Root");
        Assert.Equal(1, first.OpenCount);
        clock.Now = clock.Now.AddHours(1);
        await service.RecordOpenedRootsAsync([@"c:\ROOT"]);
        var lists = await NewService(temp).GetListsAsync();
        Assert.Equal(2, lists.Recent[0].OpenCount);
        Assert.Equal(clock.Now, lists.Recent[0].LastOpenedUtc);
        Assert.NotEqual(first.LastOpenedUtc, lists.Recent[0].LastOpenedUtc);
        Assert.Equal(2, lists.Recent.Count);
    }

    [Fact]
    public async Task FavoritesAreSharedAlphabeticalAndReopeningKeepsFavorite()
    {
        using var temp = new TemporaryDatabase();
        var service = NewService(temp);
        await service.RecordOpenedRootsAsync([@"C:\Zebra", @"C:\alpha", @"C:\middle"]);
        await service.SetFavoriteAsync(@"C:\Zebra", true);
        await service.SetFavoriteAsync(@"C:\alpha", true);
        var lists = await service.GetListsAsync();
        Assert.Equal(new[] { @"C:\alpha", @"C:\Zebra" }, lists.Favorites.Select(r => r.Path));
        Assert.True(lists.Recent.Single(r => r.Path == @"C:\alpha").Favorite);
        Assert.True(lists.Frequent.Single(r => r.Path == @"C:\alpha").Favorite);
        await service.RecordOpenedRootsAsync([@"C:\alpha"]);
        Assert.Equal(2, (await service.GetListsAsync()).Recent.Single(r => r.Path == @"C:\alpha").OpenCount);
        await service.SetFavoriteAsync(@"C:\ALPHA", false);
        lists = await NewService(temp).GetListsAsync();
        Assert.False(lists.Recent.Single(r => r.Path == @"C:\alpha").Favorite);
        Assert.False(lists.Frequent.Single(r => r.Path == @"C:\alpha").Favorite);
        Assert.Single(lists.Favorites);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetFavoriteAsync(@"C:\alpha\child", true));
    }

    [Fact]
    public async Task ConcurrentOpensDoNotLoseVisitsAndOrderingBreaksTiesDeterministically()
    {
        using var temp = new TemporaryDatabase();
        var service = NewService(temp);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => service.RecordOpenedRootsAsync([@"C:\root"])));
        await service.RecordOpenedRootsAsync([@"C:\b", @"C:\a"]);
        var lists = await service.GetListsAsync();
        Assert.Equal(12, lists.Frequent[0].OpenCount);
        Assert.Equal(new[] { @"C:\root", @"C:\a", @"C:\b" }, lists.Frequent.Select(r => r.Path));
        await service.FlushAsync();
    }

    [Fact]
    public async Task MissingRootsRemainAndChecksDoNotChangePersistedHistory()
    {
        using var temp = new TemporaryDatabase();
        var availability = new Availability { Result = RootFolderAvailability.Missing };
        var service = NewService(temp, availability);
        await service.RecordOpenedRootsAsync([@"C:\missing"]);
        await service.SetFavoriteAsync(@"C:\missing", true);
        var before = (await service.GetListsAsync()).Recent[0];
        Assert.Equal(RootFolderAvailability.Missing, Assert.Single(await service.CheckAvailabilityAsync()).Availability);
        var reopened = NewService(temp, availability);
        Assert.Equal(before, (await reopened.GetListsAsync()).Recent[0]);
        Assert.Equal(RootFolderAvailability.Missing, Assert.Single(await reopened.CheckAvailabilityAsync()).Availability);
    }

    [Fact]
    public async Task LocatePreservesCountsTimestampAndFavoriteWithoutAddingAVisit()
    {
        using var temp = new TemporaryDatabase();
        var service = NewService(temp);
        await service.RecordOpenedRootsAsync([@"C:\old"]);
        await service.SetFavoriteAsync(@"C:\old", true);
        var before = (await service.GetListsAsync()).Recent[0];
        await service.LocateAsync(@"C:\old", @"C:\new\");
        var after = Assert.Single((await NewService(temp).GetListsAsync()).Recent);
        Assert.Equal(before with { Path = @"C:\new" }, after);
        await service.RecordOpenedRootsAsync([@"C:\new"]);
        Assert.Equal(2, Assert.Single((await service.GetListsAsync()).Recent).OpenCount);
    }

    [Fact]
    public async Task LocateRejectsUnavailableDestinationAndExistingSavedRecord()
    {
        using var temp = new TemporaryDatabase();
        var availability = new Availability();
        var service = NewService(temp, availability);
        await service.RecordOpenedRootsAsync([@"C:\old", @"C:\existing"]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LocateAsync(@"C:\old", @"C:\existing"));
        availability.Result = RootFolderAvailability.AccessDenied;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LocateAsync(@"C:\old", @"C:\new"));
        Assert.Equal(2, (await NewService(temp).GetListsAsync()).Recent.Count);
    }

    [Fact]
    public async Task StoreRelocationConflictIsAtomicAndDoesNotTouchJournal()
    {
        using var temp = new TemporaryDatabase();
        var store = new SqliteApplicationStateStore(temp.Database);
        var old = new RootFolderHistory(@"C:\old", 2, DateTimeOffset.UtcNow, true, true);
        await store.SaveAsync(old);
        await store.SaveAsync(old with { Path = @"C:\existing", OpenCount = 5 });
        await Assert.ThrowsAsync<SqliteException>(() => store.RelocateAsync(old.Path, old with { Path = @"C:\existing" }));
        var roots = await ((IRootFolderHistoryStore)store).LoadAsync();
        Assert.Equal(old, roots.Single(r => r.Path == old.Path));
        Assert.Equal(5, roots.Single(r => r.Path == @"C:\existing").OpenCount);
        Assert.Empty(await temp.Journal.GetIncompleteAsync());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.RelocateAsync(@"C:\absent", old));
    }

    [Fact]
    public async Task RemoveDeletesOnlySavedEntryAndNeverSourceFolder()
    {
        using var temp = new TemporaryDatabase();
        var path = Path.GetDirectoryName(temp.DatabasePath)!;
        var service = NewService(temp);
        await service.RecordOpenedRootsAsync([path]);
        await service.SetFavoriteAsync(path, true);
        await service.RemoveAsync(path);
        var lists = await NewService(temp).GetListsAsync();
        Assert.Empty(lists.Recent);
        Assert.Empty(lists.Frequent);
        Assert.Empty(lists.Favorites);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public async Task AvailabilityIsBoundedAndCancellableAndNeverPrunesSavedRoots()
    {
        using var temp = new TemporaryDatabase();
        var availability = new Availability { Result = RootFolderAvailability.TemporarilyUnavailable, Delay = true };
        var service = NewService(temp, availability);
        await service.RecordOpenedRootsAsync(Enumerable.Range(0, 12).Select(i => @"C:\root" + i));
        Assert.Equal(12, (await service.CheckAvailabilityAsync()).Count);
        Assert.InRange(availability.MaxActive, 1, 2);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CheckAvailabilityAsync(cancellation.Token));
        Assert.Equal(12, (await service.GetListsAsync()).Recent.Count);
    }

    [Fact]
    public async Task RealAvailabilityDistinguishesExistingDirectoryMissingPathAndFile()
    {
        using var temp = new TemporaryDatabase();
        await temp.Database.InitializeAsync();
        var availability = new FileSystemRootFolderAvailabilityService();
        Assert.Equal(RootFolderAvailability.Available, await availability.CheckAsync(Path.GetDirectoryName(temp.DatabasePath)!));
        Assert.Equal(RootFolderAvailability.Missing, await availability.CheckAsync(temp.DatabasePath + ".missing"));
        Assert.Equal(RootFolderAvailability.Missing, await availability.CheckAsync(temp.DatabasePath));
    }

    private static RootFolderHistoryService NewService(TemporaryDatabase temp, Availability? availability = null, Clock? clock = null) =>
        new(new SqliteApplicationStateStore(new SqliteDatabase(temp.DatabasePath)), availability ?? new Availability(), clock ?? new Clock());

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Availability : IRootFolderAvailabilityService
    {
        public RootFolderAvailability Result = RootFolderAvailability.Available;
        public bool Delay;
        private int _active;
        public int MaxActive;
        public async Task<RootFolderAvailability> CheckAsync(string path, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            lock (this) MaxActive = Math.Max(MaxActive, active);
            try { if (Delay) await Task.Delay(10, cancellationToken); return Result; }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
