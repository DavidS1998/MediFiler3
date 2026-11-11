using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediFiler3.Application.Persistence;
using MediFiler3.Domain.Files;
using MediFiler3.Infrastructure.Files;

namespace MediFiler3.Infrastructure.Tests;

public sealed class ThumbnailMaintenanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MediFiler3CacheTests-" + Guid.NewGuid().ToString("N"));
    private string Cache => Path.Combine(_root, "cache");
    public ThumbnailMaintenanceTests() => Directory.CreateDirectory(Cache);
    public void Dispose() => Directory.Delete(_root, true);
    private static byte[] Png(byte value)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Gray8, null, new[] {value, value, value, value}, 2)));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private static FileEntry Entry(string path) { var info = new FileInfo(path); return new(path, info.Name, info.Length, info.LastWriteTimeUtc); }
    private string Seed(Store store, char key, DateTimeOffset access, long? size = null)
    {
        var hash = new string(key, 64);
        var path = Path.Combine(Cache, hash + ".png");
        File.WriteAllBytes(path, Png(80));
        if (size.HasValue) { using var file = File.OpenWrite(path); file.SetLength(size.Value); }
        store.Entries[hash] = new(hash, hash + ".png", new FileInfo(path).Length, access, Path.Combine(_root, "source.png"));
        return path;
    }

    [Fact]
    public async Task MatchingCarouselAndGalleryRequestsReuseTheSameCachedPng()
    {
        var source = Path.Combine(_root, "shared.png"); File.WriteAllBytes(source, Png(10));
        using var cache = new FileSystemThumbnailService(new Store(), Cache);
        var file = Entry(source);
        var carousel = await cache.GetThumbnailPngAsync(file, 400, 400, default);
        var gallery = await cache.GetThumbnailPngAsync(file, 400, 400, default);
        Assert.NotNull(carousel); Assert.Same(carousel, gallery);
        Assert.Single(Directory.GetFiles(Cache, "*.png"));
    }

    [Fact]
    public async Task TargetedInvalidationRetainsUnrelatedPositiveAndNegativeMemoryEntries()
    {
        var first = Path.Combine(_root, "first.png"); var second = Path.Combine(_root, "second.png");
        File.WriteAllBytes(first, Png(10)); File.WriteAllBytes(second, Png(20));
        using var cache = new FileSystemThumbnailService(new Store(), Cache);
        var a = Entry(first); var b = Entry(second);
        var before = await cache.GetThumbnailPngAsync(b, 400, 400, default);
        var missing = new FileEntry(Path.Combine(_root, "missing.png"), "missing.png", 0, DateTime.UnixEpoch);
        Assert.Null(await cache.GetThumbnailPngAsync(missing, 400, 400, default));
        // No hint for this path: its cached negative result must survive an unrelated event.
        File.WriteAllBytes(missing.FullPath, Png(30));
        await cache.InvalidateAsync(a.FullPath, false);
        Assert.Same(before, await cache.GetThumbnailPngAsync(b, 400, 400, default));
        Assert.Null(await cache.GetThumbnailPngAsync(missing, 400, 400, default));
        await cache.InvalidateAsync(missing.FullPath, false);
        Assert.NotNull(await cache.GetThumbnailPngAsync(missing, 400, 400, default));
    }

    [Fact]
    public async Task ConcurrentMatchingRequestsGenerateOneSharedResult()
    {
        var source = Path.Combine(_root, "shared.png"); File.WriteAllBytes(source, Png(10));
        using var cache = new FileSystemThumbnailService(new Store(), Cache);
        var workers = (SemaphoreSlim)typeof(FileSystemThumbnailService).GetField("_workers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(cache)!;
        for (var i = 0; i < 3; i++) await workers.WaitAsync();
        var file = Entry(source);
        var requests = Enumerable.Range(0, 5).Select(_ => cache.GetThumbnailPngAsync(file, 400, 400, default)).ToArray();
        workers.Release(3);
        var results = await Task.WhenAll(requests);
        Assert.NotNull(results[0]);
        Assert.All(results, result => Assert.Same(results[0], result));
        var gates = (System.Collections.IDictionary)typeof(FileSystemThumbnailService).GetField("_requestGates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(cache)!;
        Assert.Empty(gates);
    }

    [Fact]
    public async Task UnrelatedInvalidationDoesNotDiscardQueuedDecoder()
    {
        var source = Path.Combine(_root, "source.png"); File.WriteAllBytes(source, Png(10));
        using var cache = new FileSystemThumbnailService(new Store(), Cache);
        var workers = (SemaphoreSlim)typeof(FileSystemThumbnailService).GetField("_workers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(cache)!;
        for (var i = 0; i < 3; i++) await workers.WaitAsync();
        var request = cache.GetThumbnailPngAsync(Entry(source), 400, 400, default);
        try { await cache.InvalidateAsync(Path.Combine(_root, "unrelated.png"), false); }
        finally { workers.Release(3); }
        Assert.NotNull(await request);
    }

    [Fact]
    public async Task CancellingOneMatchingRequestDoesNotCancelAnotherOrLeakItsGate()
    {
        var source = Path.Combine(_root, "shared.png"); File.WriteAllBytes(source, Png(10));
        using var cache = new FileSystemThumbnailService(new Store(), Cache);
        var workers = (SemaphoreSlim)typeof(FileSystemThumbnailService).GetField("_workers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(cache)!;
        for (var i = 0; i < 3; i++) await workers.WaitAsync();
        using var stop = new CancellationTokenSource();
        var file = Entry(source);
        var first = cache.GetThumbnailPngAsync(file, 400, 400, stop.Token);
        var second = cache.GetThumbnailPngAsync(file, 400, 400, default);
        try
        {
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        }
        finally { workers.Release(3); }
        Assert.NotNull(await second);
        var gates = (System.Collections.IDictionary)typeof(FileSystemThumbnailService).GetField("_requestGates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(cache)!;
        Assert.Empty(gates);
    }

    [Fact]
    public async Task IdleHousekeepingRetainsWarmMemoryButExplicitClearEvictsIt()
    {
        var source = Path.Combine(_root, "warm.png"); File.WriteAllBytes(source, Png(10));
        using var cache = new FileSystemThumbnailService(new Store(), Cache);
        var file = Entry(source);
        var before = await cache.GetThumbnailPngAsync(file, 400, 400, default);
        await cache.MaintainAsync();
        Assert.Same(before, await cache.GetThumbnailPngAsync(file, 400, 400, default));
        await cache.MaintainAsync(clear: true);
        Assert.NotSame(before, await cache.GetThumbnailPngAsync(file, 400, 400, default));
    }

    [Fact]
    public async Task IdleHousekeepingDoesNotDiscardUnrelatedQueuedDecoder()
    {
        var source = Path.Combine(_root, "warm.png"); File.WriteAllBytes(source, Png(10));
        using var cache = new FileSystemThumbnailService(new Store(), Cache);
        var workers = (SemaphoreSlim)typeof(FileSystemThumbnailService).GetField("_workers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(cache)!;
        for (var i = 0; i < 3; i++) await workers.WaitAsync();
        var request = cache.GetThumbnailPngAsync(Entry(source), 400, 400, default);
        try { await cache.MaintainAsync(); }
        finally { workers.Release(3); }
        Assert.NotNull(await request);
    }

    [Fact]
    public async Task ExpiryAndClearPreserveSourcesUnknownFilesAndHistory()
    {
        var store = new Store();
        var old = Seed(store, 'A', DateTimeOffset.UtcNow.AddDays(-91));
        var recent = Seed(store, 'B', DateTimeOffset.UtcNow);
        var source = Path.Combine(_root, "source.png"); File.WriteAllBytes(source, Png(10));
        var unknown = Path.Combine(Cache, "unknown.png"); File.WriteAllBytes(unknown, Png(20));
        var history = Path.Combine(_root, "UndoBackups"); Directory.CreateDirectory(history); File.WriteAllText(Path.Combine(history, "keep"), "history");
        using var cache = new FileSystemThumbnailService(store, Cache);
        await cache.MaintainAsync();
        Assert.False(File.Exists(old)); Assert.True(File.Exists(recent));
        await cache.MaintainAsync(clear: true);
        Assert.False(File.Exists(recent)); Assert.True(File.Exists(unknown)); Assert.True(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(history, "keep"))); Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task QuotaUsesLeastRecentlyAccessedAndLowWaterMark()
    {
        var store = new Store();
        const long size = 300L * 1024 * 1024;
        var oldest = Seed(store, 'A', DateTimeOffset.UtcNow.AddDays(-4), size);
        Seed(store, 'B', DateTimeOffset.UtcNow.AddDays(-3), size);
        Seed(store, 'C', DateTimeOffset.UtcNow.AddDays(-2), size);
        var newest = Seed(store, 'D', DateTimeOffset.UtcNow.AddDays(-1), size);
        using var cache = new FileSystemThumbnailService(store, Cache);
        var usage = await cache.MaintainAsync();
        Assert.False(File.Exists(oldest)); Assert.True(File.Exists(newest));
        Assert.Equal(FileSystemThumbnailService.DiskLowWater, usage.SizeBytes);
    }

    [Fact]
    public async Task SameMetadataInvalidationRegeneratesAllDimensionsAndNegativeResults()
    {
        var source = Path.Combine(_root, "source.png"); File.WriteAllBytes(source, Png(10));
        var entry = Entry(source); var store = new Store();
        using var cache = new FileSystemThumbnailService(store, Cache);
        var before = await cache.GetThumbnailPngAsync(entry, 20, 20, default);
        await cache.GetThumbnailPngAsync(entry, 40, 40, default);
        File.WriteAllBytes(source, Png(200)); File.SetLastWriteTimeUtc(source, entry.LastModifiedUtc);
        await cache.InvalidateAsync(source, false);
        var after = await cache.GetThumbnailPngAsync(entry, 20, 20, default);
        Assert.NotEqual(before, after);
        await cache.FlushAsync(); Assert.Single(store.Entries);
        var missing = Path.Combine(_root, "missing.png"); var missingEntry = new FileEntry(missing, "missing.png", 0, DateTime.UtcNow);
        Assert.Null(await cache.GetThumbnailPngAsync(missingEntry, 20, 20, default));
        File.WriteAllBytes(missing, Png(50)); await cache.InvalidateAsync(missing, false);
        Assert.NotNull(await cache.GetThumbnailPngAsync(missingEntry, 20, 20, default));
    }

    [Fact]
    public async Task LockedInvalidationRemainsInvalidAcrossRestartAndCleanupCanRetry()
    {
        var store = new Store(); var source = Path.Combine(_root, "source.png"); File.WriteAllBytes(source, Png(10)); var entry = Entry(source);
        using (var cache = new FileSystemThumbnailService(store, Cache))
        {
            await cache.GetThumbnailPngAsync(entry, 20, 20, default); await cache.FlushAsync();
            var png = Assert.Single(store.Entries.Values);
            using var locked = File.Open(Path.Combine(Cache, png.CacheFileName), FileMode.Open, FileAccess.Read, FileShare.Read);
            await cache.InvalidateAsync(source, false); await cache.FlushAsync();
            Assert.True(Assert.Single(store.Entries.Values).Invalidated);
        }
        using var reopened = new FileSystemThumbnailService(store, Cache);
        await reopened.MaintainAsync(); Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task MissingCorruptAndLegacyEntriesRepairWithoutDeletingUnknownFiles()
    {
        var store = new Store(); var missing = Seed(store, 'A', DateTimeOffset.UtcNow); File.Delete(missing);
        var legacy = Path.Combine(Cache, new string('B', 64) + ".png"); File.WriteAllBytes(legacy, Png(30));
        var corrupt = Path.Combine(Cache, "unrecognized.png"); File.WriteAllText(corrupt, "broken");
        using var cache = new FileSystemThumbnailService(store, Cache);
        await cache.MaintainAsync();
        Assert.Equal(new string('B', 64), Assert.Single(store.Entries.Values).Key);
        Assert.True(File.Exists(corrupt));
        var source = Path.Combine(_root, "image.png"); File.WriteAllBytes(source, Png(10));
        await cache.GetThumbnailPngAsync(Entry(source), 20, 20, default); await cache.FlushAsync();
        var generated = store.Entries.Values.Single(e => e.SourcePath is not null);
        File.WriteAllText(Path.Combine(Cache, generated.CacheFileName), "broken");
        await cache.InvalidateAsync("unrelated", false);
        Assert.NotNull(await cache.GetThumbnailPngAsync(Entry(source), 20, 20, default));
    }

    [Fact]
    public async Task LinkedCacheDirectoryCannotDeleteExternalData()
    {
        var outside = Path.Combine(_root, "outside"); Directory.CreateDirectory(outside);
        var store = new Store(); var path = Seed(store, 'A', DateTimeOffset.UtcNow.AddDays(-100));
        File.Move(path, Path.Combine(outside, Path.GetFileName(path))); Directory.Delete(Cache);
        // Junction creation does not require developer mode/elevation.
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{Cache}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
        process!.WaitForExit(); Assert.Equal(0, process.ExitCode);
        using var cache = new FileSystemThumbnailService(store, Cache);
        var result = await cache.MaintainAsync(clear: true);
        Assert.True(result.RetainedCount > 0); Assert.True(File.Exists(Path.Combine(outside, Path.GetFileName(path))));
        Directory.Delete(Cache);
    }

    [Fact]
    public async Task DecoderQueuedBeforeInvalidationCannotRepopulateCache()
    {
        var source = Path.Combine(_root, "source.png"); File.WriteAllBytes(source, Png(10));
        using var cache = new FileSystemThumbnailService(new Store(), Cache);
        var workers = (SemaphoreSlim)typeof(FileSystemThumbnailService).GetField("_workers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(cache)!;
        for (var i = 0; i < 3; i++) await workers.WaitAsync();
        var request = cache.GetThumbnailPngAsync(Entry(source), 20, 20, default);
        try { Assert.False(request.IsCompleted); await cache.InvalidateAsync(source, false); }
        finally { workers.Release(3); }
        Assert.Null(await request);
        Assert.Empty(Directory.GetFiles(Cache));
        Assert.NotNull(await cache.GetThumbnailPngAsync(Entry(source), 20, 20, default));
    }

    [Fact]
    public async Task AccessesAreBatchedAndTouchProtectsAnOldDiskEntryFromExpiry()
    {
        var source = Path.Combine(_root, "source.png"); File.WriteAllBytes(source, Png(10)); var store = new Store();
        using (var cache = new FileSystemThumbnailService(store, Cache))
        {
            await cache.GetThumbnailPngAsync(Entry(source), 20, 20, default);
            Assert.Empty(store.Entries); await cache.FlushAsync();
        }
        var old = Assert.Single(store.Entries.Values);
        store.Entries[old.Key] = old with { LastAccessUtc = DateTimeOffset.UtcNow.AddDays(-100) };
        using var reopened = new FileSystemThumbnailService(store, Cache);
        Assert.NotNull(await reopened.GetThumbnailPngAsync(Entry(source), 20, 20, default));
        await reopened.MaintainAsync();
        Assert.Single(store.Entries); Assert.True(store.Entries[old.Key].LastAccessUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task TemporaryCleanupRequiresOwnedNameAndAgeAndDisposalIsIdempotent()
    {
        var owned = Path.Combine(Cache, new string('A', 64) + ".png." + Guid.NewGuid().ToString("N") + ".tmp");
        var unknown = Path.Combine(Cache, "unknown.tmp");
        File.WriteAllText(owned, "partial"); File.WriteAllText(unknown, "keep");
        File.SetLastWriteTimeUtc(owned, DateTime.UtcNow.AddDays(-2)); File.SetLastWriteTimeUtc(unknown, DateTime.UtcNow.AddDays(-2));
        using var cache = new FileSystemThumbnailService(new Store(), Cache);
        await cache.MaintainAsync();
        Assert.False(File.Exists(owned)); Assert.True(File.Exists(unknown));
        cache.Dispose(); cache.Dispose();
    }

    private sealed class Store : IThumbnailCacheIndexStore
    {
        public Dictionary<string, ThumbnailCacheEntry> Entries { get; } = [];
        public Task<IReadOnlyList<ThumbnailCacheEntry>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ThumbnailCacheEntry>>(Entries.Values.ToArray());
        public Task SaveAsync(ThumbnailCacheEntry entry, CancellationToken cancellationToken = default) { Entries[entry.Key] = entry; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) { Entries.Remove(key); return Task.CompletedTask; }
    }
}
