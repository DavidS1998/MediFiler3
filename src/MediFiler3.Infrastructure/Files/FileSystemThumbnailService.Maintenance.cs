using System.IO;
using System.Windows.Media.Imaging;
using MediFiler3.Application.Files;
using MediFiler3.Application.Persistence;

namespace MediFiler3.Infrastructure.Files;

public sealed partial class FileSystemThumbnailService
{
    public const long DiskLimit = 1024L * 1024 * 1024;
    public const long DiskLowWater = 900L * 1024 * 1024;
    public static readonly TimeSpan MaximumAge = TimeSpan.FromDays(90);
    private readonly SemaphoreSlim _maintenancePassGate = new(1, 1);
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private readonly IThumbnailCacheIndexStore? _indexStore;
    private readonly Dictionary<string, ThumbnailCacheEntry> _index = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirty = [];
    private readonly HashSet<string> _removed = [];
    private readonly CancellationTokenSource _maintenanceStop = new();
    private readonly Task _maintenanceTask;
    private bool _initialized;
    private bool _legacyInvalidated;
    private int _disposed;
    private long _revision;
    private DateTimeOffset _lastRequest;
    public event Action<Exception>? MaintenanceFailed;

    public FileSystemThumbnailService(IThumbnailCacheIndexStore? indexStore = null, string? cachePath = null)
    {
        _indexStore = indexStore;
        _diskCachePath = Path.GetFullPath(cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediFiler3", "ThumbnailCache"));
        _maintenanceTask = Task.Run(MaintenanceLoopAsync);
    }

    private static bool IsOwnedTemporary(string name) => name.Length == 105 && OwnedName(name[..68]) &&
        name[68] == '.' && Guid.TryParseExact(name.Substring(69, 32), "N", out _) && name.EndsWith(".tmp", StringComparison.Ordinal);

    private static bool OwnedName(string name) => name.Length == 68 && name.EndsWith(".png", StringComparison.Ordinal) &&
        name[..64].All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    // Check every existing ancestor as well as each leaf; never traverse cache links.
    private bool SafeCacheDirectory()
    {
        for (var path = _diskCachePath; path is not null; path = Path.GetDirectoryName(path))
        {
            try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false; }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
        return true;
    }

    private bool SafeEntry(string name)
    {
        if (!OwnedName(name)) return false;
        try { return (File.GetAttributes(Path.Combine(_diskCachePath, name)) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool ValidPng(byte[] bytes)
    {
        try
        {
            if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137, 80, 78, 71, 13, 10, 26, 10})) return false;
            using var stream = new MemoryStream(bytes, false);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decoder.Frames.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or FormatException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException) { return false; }
    }

    private async Task InitializeIndexAsync(CancellationToken token)
    {
        if (_initialized) return;
        try
        {
            if (_indexStore is not null)
                foreach (var entry in await _indexStore.LoadAsync(token).ConfigureAwait(false))
                    if (OwnedName(entry.CacheFileName) && entry.CacheFileName == entry.Key + ".png")
                    {
                        _index[entry.Key] = entry;
                        if ((entry.RequiresFreshSource || entry.Invalidated) && entry.SourcePath is { } source) _freshSources.Add(source);
                    }
            _initialized = true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _initialized = true; MaintenanceFailed?.Invoke(ex); }
    }

    private void Record(string key, long size, string? source)
    {
        var fresh = source is not null && _freshSources.Contains(source);
        // A fresh valid PNG carries the source marker; redundant empty markers can retire.
        if (fresh)
            foreach (var old in _index.Values.Where(e => e.Key != key && e.Invalidated && e.SizeBytes == 0 &&
                string.Equals(e.SourcePath, source, StringComparison.OrdinalIgnoreCase)).ToArray())
            { _index.Remove(old.Key); _dirty.Remove(old.Key); _removed.Add(old.Key); }
        _index[key] = new ThumbnailCacheEntry(key, key + ".png", size, DateTimeOffset.UtcNow, source, RequiresFreshSource: fresh);
        _dirty.Add(key);
        _removed.Remove(key);
    }

    private void Touch(string key)
    {
        if (_index.TryGetValue(key, out var entry))
        {
            _index[key] = entry with { LastAccessUtc = DateTimeOffset.UtcNow };
            _dirty.Add(key);
        }
    }

    private bool RemoveOwned(string key)
    {
        if (!SafeCacheDirectory() || !SafeEntry(key + ".png")) return false;
        try { File.Delete(Path.Combine(_diskCachePath, key + ".png")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_index.TryGetValue(key, out var stale)) { _index[key] = stale with { Invalidated = true }; _dirty.Add(key); }
            return false;
        }
        _index.Remove(key);
        _dirty.Remove(key);
        _removed.Add(key);
        return true;
    }

    private void ClearMemory()
    {
        lock (_memoryLock) { _memoryEntries.Clear(); _memoryLru.Clear(); _memorySize = 0; }
        _revision++;
    }

    private void PreserveFreshHint(ThumbnailCacheEntry entry, bool removed, bool touch = false)
    {
        if (entry.SourcePath is { } source)
        {
            if (!ImageExtensions.Contains(Path.GetExtension(source))) return;
            _freshSources.Add(source);
        }
        _index[entry.Key] = entry with { Invalidated = true, RequiresFreshSource = true,
            SizeBytes = removed ? 0 : entry.SizeBytes, LastAccessUtc = touch ? DateTimeOffset.UtcNow : entry.LastAccessUtc };
        _dirty.Add(entry.Key); _removed.Remove(entry.Key);
    }

    public Task InvalidateAsync(string path, bool descendants, CancellationToken token = default) =>
        Task.Run(() => InvalidateCoreAsync(path, descendants, token), token);

    private async Task InvalidateCoreAsync(string path, bool descendants, CancellationToken token)
    {
        await _cacheGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await InitializeIndexAsync(token).ConfigureAwait(false);
            _legacyInvalidated = true;
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            bool Affected(string source) => string.Equals(source, normalized, StringComparison.OrdinalIgnoreCase) ||
                descendants && source.StartsWith(Path.EndsInDirectorySeparator(normalized) ? normalized : normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            lock (_memoryLock)
            {
                foreach (var entry in _memoryEntries.Values.ToArray())
                {
                    if (!Affected(entry.Value.SourcePath)) continue;
                    if (ImageExtensions.Contains(Path.GetExtension(entry.Value.SourcePath))) _freshSources.Add(entry.Value.SourcePath);
                    _memoryEntries.Remove(entry.Value.Key); _memoryLru.Remove(entry);
                    _memorySize -= entry.Value.Bytes?.LongLength ?? 0;
                }
            }
            foreach (var request in _pendingThumbnails)
                if (Affected(request.Path))
                {
                    request.Invalidated = true;
                    if (ImageExtensions.Contains(Path.GetExtension(request.Path))) _freshSources.Add(request.Path);
                }
            foreach (var entry in _index.Values.ToArray())
            {
                token.ThrowIfCancellationRequested();
                // Legacy entries have no source mapping; conservatively invalidate them.
                if (entry.SourcePath is null || Affected(entry.SourcePath))
                {
                    var removed = RemoveOwned(entry.Key);
                    PreserveFreshHint(entry, removed, touch: true);
                }
            }
        }
        finally { _cacheGate.Release(); }
    }

    public Task<ThumbnailCacheUsage> MaintainAsync(bool clear = false, CancellationToken token = default) =>
        Task.Run(async () =>
        {
            await _maintenancePassGate.WaitAsync(token).ConfigureAwait(false);
            try { return await MaintainCoreAsync(clear, token).ConfigureAwait(false); }
            finally { _maintenancePassGate.Release(); }
        }, token);

    private async Task<ThumbnailCacheUsage> MaintainCoreAsync(bool clear, CancellationToken token)
    {
        await _cacheGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await InitializeIndexAsync(token).ConfigureAwait(false);
            if (!SafeCacheDirectory()) return Usage(1);
            if (clear) ClearMemory(); // Idle disk housekeeping must not flush unrelated warm previews.
            var batchSize = 0;
            async Task YieldBatchAsync()
            {
                if (++batchSize % 32 != 0) return;
                _cacheGate.Release();
                // Let pending thumbnail reads/writes run between bounded maintenance batches.
                try { await Task.Delay(1, token).ConfigureAwait(false); }
                finally { await _cacheGate.WaitAsync(CancellationToken.None).ConfigureAwait(false); }
                token.ThrowIfCancellationRequested();
            }
            // Adopt only valid legacy hash-named PNGs. Unknown files are never deleted.
            if (Directory.Exists(_diskCachePath))
            {
                foreach (var file in new DirectoryInfo(_diskCachePath).EnumerateFiles())
                {
                    token.ThrowIfCancellationRequested();
                    await YieldBatchAsync().ConfigureAwait(false);
                    if (!SafeCacheDirectory()) return Usage(1);
                    if (IsOwnedTemporary(file.Name) && (file.Attributes & FileAttributes.ReparsePoint) == 0 && DateTime.UtcNow - file.LastWriteTimeUtc > TimeSpan.FromDays(1))
                    {
                        try { File.Delete(file.FullName); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                        continue;
                    }
                    if (!OwnedName(file.Name) || !SafeEntry(file.Name) || file.Length > 16 * 1024 * 1024) continue;
                    var key = file.Name[..64];
                    if (!_index.ContainsKey(key))
                    {
                        if (ValidPng(await File.ReadAllBytesAsync(file.FullName, token).ConfigureAwait(false)))
                        {
                            Record(key, file.Length, null);
                            if (_legacyInvalidated) _index[key] = _index[key] with { Invalidated = true };
                        }
                    }
                    // Yield between entries so maintenance remains cancellable on large caches.
                    await Task.Yield();
                }
            }
            foreach (var snapshot in _index.Values.ToArray())
            {
                token.ThrowIfCancellationRequested();
                await YieldBatchAsync().ConfigureAwait(false);
                if (!_index.TryGetValue(snapshot.Key, out var entry)) continue;
                if (!SafeCacheDirectory()) return Usage(1);
                var path = Path.Combine(_diskCachePath, entry.CacheFileName);
                if (!File.Exists(path) && SafeEntry(entry.CacheFileName))
                {
                    if (!entry.Invalidated || !entry.RequiresFreshSource) RemoveOwned(entry.Key);
                }
                else if (SafeEntry(entry.CacheFileName))
                {
                    var length = new FileInfo(path).Length;
                    if (length != entry.SizeBytes) { _index[entry.Key] = entry with { SizeBytes = length }; _dirty.Add(entry.Key); }
                }
            }
            var retained = 0;
            foreach (var snapshot in _index.Values.OrderBy(e => e.LastAccessUtc).ToArray())
            {
                await YieldBatchAsync().ConfigureAwait(false);
                if (!_index.TryGetValue(snapshot.Key, out var entry)) continue;
                if (clear || entry.Invalidated || DateTimeOffset.UtcNow - entry.LastAccessUtc > MaximumAge)
                {
                    var removed = RemoveOwned(entry.Key);
                    if (!removed) retained++;
                    if (entry.RequiresFreshSource && DateTimeOffset.UtcNow - entry.LastAccessUtc <= MaximumAge)
                        PreserveFreshHint(entry, removed);
                }
            }
            var total = Usage().SizeBytes;
            if (!clear && total > DiskLimit)
                foreach (var snapshot in _index.Values.OrderBy(e => e.LastAccessUtc).ToArray())
                {
                    await YieldBatchAsync().ConfigureAwait(false);
                    if (total <= DiskLowWater) break;
                    if (!_index.TryGetValue(snapshot.Key, out var entry) || entry.LastAccessUtc != snapshot.LastAccessUtc) continue;
                    if (RemoveOwned(entry.Key)) total -= entry.SizeBytes;
                    else retained++;
                }
            await FlushCoreAsync(token).ConfigureAwait(false);
            var liveFreshSources = _index.Values.Where(e => e.RequiresFreshSource && e.SourcePath is not null)
                .Select(e => e.SourcePath!).Concat(_pendingThumbnails.Select(p => p.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            lock (_memoryLock)
                foreach (var entry in _memoryEntries.Values) liveFreshSources.Add(entry.Value.SourcePath);
            _freshSources.IntersectWith(liveFreshSources);
            return Usage(retained);
        }
        finally { _cacheGate.Release(); }
    }

    private ThumbnailCacheUsage Usage(int retained = 0) => new(_index.Values.Sum(e => e.SizeBytes), _index.Values.Count(e => e.SizeBytes > 0), retained);

    public async Task<ThumbnailCacheUsage> GetUsageAsync(CancellationToken token = default)
    {
        await _cacheGate.WaitAsync(token).ConfigureAwait(false);
        try { await InitializeIndexAsync(token).ConfigureAwait(false); return Usage(); }
        finally { _cacheGate.Release(); }
    }

    public async Task FlushAsync(CancellationToken token = default)
    {
        await _cacheGate.WaitAsync(token).ConfigureAwait(false);
        try { await FlushCoreAsync(token).ConfigureAwait(false); }
        finally { _cacheGate.Release(); }
    }

    private async Task FlushCoreAsync(CancellationToken token)
    {
        if (_indexStore is null) return;
        foreach (var key in _removed.ToArray())
        {
            await _indexStore.RemoveAsync(key, token).ConfigureAwait(false);
            _removed.Remove(key);
        }
        foreach (var key in _dirty.ToArray())
        {
            await _indexStore.SaveAsync(_index[key], token).ConfigureAwait(false);
            _dirty.Remove(key);
        }
    }

    private async Task MaintenanceLoopAsync()
    {
        var nextMaintenance = DateTimeOffset.MinValue;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(_maintenanceStop.Token).ConfigureAwait(false))
            {
                try
                {
                    await FlushAsync(_maintenanceStop.Token).ConfigureAwait(false);
                    if (DateTimeOffset.UtcNow >= nextMaintenance && DateTimeOffset.UtcNow - _lastRequest >= TimeSpan.FromSeconds(30))
                    {
                        await MaintainAsync(token: _maintenanceStop.Token).ConfigureAwait(false);
                        nextMaintenance = DateTimeOffset.UtcNow.AddHours(1);
                    }
                }
                catch (OperationCanceledException) when (_maintenanceStop.IsCancellationRequested) { break; }
                catch (Exception ex) { MaintenanceFailed?.Invoke(ex); }
            }
        }
        catch (OperationCanceledException) when (_maintenanceStop.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _maintenanceStop.Cancel();
        _maintenanceTask.GetAwaiter().GetResult();
        try { FlushAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { MaintenanceFailed?.Invoke(ex); }
        _maintenanceStop.Dispose();
    }
}
