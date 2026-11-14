using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using MediFiler3.Application.Files;
using MediFiler3.Domain.Files;

namespace MediFiler3.Infrastructure.Files;

public sealed partial class FileSystemThumbnailService : IThumbnailService, IThumbnailCacheMaintenance, IDisposable
{
    private const long MemoryCacheLimit = 32 * 1024 * 1024;
    private const int MemoryEntryLimit = 512;

    private readonly SemaphoreSlim _workers = new(3, 3);
    private readonly object _memoryLock = new();
    private readonly Dictionary<string, LinkedListNode<MemoryEntry>> _memoryEntries = new(StringComparer.Ordinal);
    private readonly LinkedList<MemoryEntry> _memoryLru = [];
    private long _memorySize;
    private readonly string _diskCachePath;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" };
    private readonly HashSet<string> _freshSources = new(StringComparer.OrdinalIgnoreCase);
    private sealed class PendingThumbnail(string path)
    {
        public string Path { get; } = path;
        public bool Invalidated { get; set; }
    }
    private sealed class RequestGate
    {
        public SemaphoreSlim Mutex { get; } = new(1, 1);
        public int Users { get; set; }
    }
    // Protected by _cacheGate. Entries exist only while requests are outstanding.
    private readonly HashSet<PendingThumbnail> _pendingThumbnails = [];
    private readonly Dictionary<string, RequestGate> _requestGates = [];

    public Task<byte[]?> GetThumbnailPngAsync(FileEntry file, int width, int height, CancellationToken cancellationToken) =>
        GetThumbnailCoreAsync(file, Math.Max(1, width), Math.Max(1, height), windowsDefaults: false, cancellationToken);

    public Task<byte[]?> GetDefaultThumbnailPngAsync(FileEntry file, CancellationToken cancellationToken) =>
        GetThumbnailCoreAsync(file, 0, 0, windowsDefaults: true, cancellationToken);

    private async Task<byte[]?> GetThumbnailCoreAsync(FileEntry file, int width, int height, bool windowsDefaults, CancellationToken cancellationToken)
    {
        // The private 0x0 bucket separates platform-default previews from old fixed-size PNGs.
        var key = CreateCacheKey(file, width, height);
        long revision;
        PendingThumbnail pending;
        RequestGate requestGate;
        bool freshSource;
        await _cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeIndexAsync(cancellationToken).ConfigureAwait(false);
            _lastRequest = DateTimeOffset.UtcNow;
            revision = _revision;
            if (TryGetMemory(key, out var memoryHit))
            {
                Touch(key);
                return memoryHit;
            }
            if ((!_index.TryGetValue(key, out var indexed) ? !_legacyInvalidated : !indexed.Invalidated) && SafeCacheDirectory() && SafeEntry(key + ".png"))
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(Path.Combine(_diskCachePath, key + ".png"), cancellationToken).ConfigureAwait(false);
                    if (ValidPng(bytes))
                    {
                        Record(key, bytes.LongLength, file.FullPath);
                        Remember(key, bytes, file.FullPath);
                        return bytes;
                    }
                    RemoveOwned(key);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            pending = new PendingThumbnail(Path.GetFullPath(file.FullPath));
            _pendingThumbnails.Add(pending);
            if (!_requestGates.TryGetValue(key, out requestGate!)) _requestGates[key] = requestGate = new RequestGate();
            requestGate.Users++;
            freshSource = _freshSources.Contains(pending.Path) ||
                _index.TryGetValue(key, out var previousSource) && (previousSource.Invalidated || previousSource.RequiresFreshSource);
            if (freshSource && ImageExtensions.Contains(file.Extension)) _freshSources.Add(pending.Path);
        }
        finally { _cacheGate.Release(); }

        var enteredKey = false;
        var enteredWorker = false;
        try
        {
            await requestGate.Mutex.WaitAsync(cancellationToken).ConfigureAwait(false); enteredKey = true;
            await _cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (pending.Invalidated || revision != _revision) return null;
                if (TryGetMemory(key, out var hit)) { Touch(key); return hit; }
            }
            finally { _cacheGate.Release(); }
            await _workers.WaitAsync(cancellationToken).ConfigureAwait(false); enteredWorker = true;
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await Task.Run(() => GenerateThumbnailAsync(file, width, height, windowsDefaults, freshSource, cancellationToken), cancellationToken).ConfigureAwait(false);
            await _cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Invalidation/clear wins over a decoder that was already running.
                if (pending.Invalidated || revision != _revision) return null;
                Remember(key, bytes, file.FullPath);
                if (bytes is not null && SafeCacheDirectory() && SafeEntry(key + ".png"))
                {
                    if (await TryWriteDiskCacheAsync(Path.Combine(_diskCachePath, key + ".png"), bytes, cancellationToken).ConfigureAwait(false))
                        Record(key, bytes.LongLength, file.FullPath);
                }
                return bytes;
            }
            finally { _cacheGate.Release(); }
        }
        finally
        {
            if (enteredWorker) _workers.Release();
            if (enteredKey) requestGate.Mutex.Release();
            await _cacheGate.WaitAsync().ConfigureAwait(false);
            try
            {
                _pendingThumbnails.Remove(pending);
                if (--requestGate.Users == 0) _requestGates.Remove(key);
            }
            finally { _cacheGate.Release(); }
        }
    }

    private static async Task<byte[]?> GenerateThumbnailAsync(FileEntry file, int width, int height, bool windowsDefaults, bool freshSource, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var item = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(file.FullPath)).AsTask(token).ConfigureAwait(false);
            // Known invalidations still bypass stale Windows previews. Keep this source
            // fallback bounded using SingleItem's default preferred size, without upscaling.
            var fallbackWidth = windowsDefaults ? 256 : width;
            var fallbackHeight = windowsDefaults ? 256 : height;
            if (freshSource && ImageExtensions.Contains(file.Extension))
            {
                // Windows can retain a stale system thumbnail after same-size/timestamp edits.
                using var source = await item.OpenAsync(FileAccessMode.Read).AsTask(token).ConfigureAwait(false);
                return await EncodeThumbnailPngAsync(source, fallbackWidth, fallbackHeight, token).ConfigureAwait(false);
            }
            using var thumbnail = windowsDefaults
                ? await item.GetThumbnailAsync(ThumbnailMode.SingleItem).AsTask(token).ConfigureAwait(false)
                : await item.GetThumbnailAsync(ThumbnailMode.SingleItem,
                    (uint)Math.Max(width, height), ThumbnailOptions.ResizeThumbnail).AsTask(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            // Miscellaneous files may have only Explorer's generic/type icon. Keep that
            // usable visual; supported images still prefer decoded source pixels to icons.
            if (thumbnail is null || thumbnail.Size == 0 || thumbnail.Type != ThumbnailType.Image && ImageExtensions.Contains(file.Extension))
            {
                if (!ImageExtensions.Contains(file.Extension)) return await GenerateFileIconAsync(file, token).ConfigureAwait(false);
                // Keep supported image previews available if Windows supplies only an icon.
                using var source = await item.OpenAsync(FileAccessMode.Read).AsTask(token).ConfigureAwait(false);
                return await EncodeThumbnailPngAsync(source, fallbackWidth, fallbackHeight, token).ConfigureAwait(false);
            }
            return await EncodeThumbnailPngAsync(thumbnail, width, height, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException or
            ArgumentException or InvalidOperationException or NotSupportedException or System.Security.SecurityException)
        {
            return ImageExtensions.Contains(file.Extension) ? null : await GenerateFileIconAsync(file, token).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]?> GenerateFileIconAsync(FileEntry file, CancellationToken token)
    {
        try
        {
            var item = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(file.FullPath)).AsTask(token).ConfigureAwait(false);
            // ListView requests the Shell file/type icon even when the preview provider
            // cannot produce a SingleItem thumbnail (for example a PSD without a codec).
            using var icon = await item.GetThumbnailAsync(ThumbnailMode.ListView, 96, ThumbnailOptions.UseCurrentScale).AsTask(token).ConfigureAwait(false);
            return icon is null || icon.Size == 0 ? null : await EncodeThumbnailPngAsync(icon, 0, 0, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException or
            ArgumentException or InvalidOperationException or NotSupportedException or System.Security.SecurityException) { return null; }
    }

    private static async Task<byte[]> EncodeThumbnailPngAsync(IRandomAccessStream source, int width, int height, CancellationToken token)
    {
        var decoder = await BitmapDecoder.CreateAsync(source).AsTask(token).ConfigureAwait(false);
        // Default previews keep the exact dimensions Windows returned; PNG normalization
        // must not silently resize them back into the previous 400px bucket.
        var scale = width == 0 && height == 0 ? 1d : Math.Min(1d,
            Math.Min((double)width / decoder.OrientedPixelWidth, (double)height / decoder.OrientedPixelHeight));
        var transform = new BitmapTransform
        {
            InterpolationMode = BitmapInterpolationMode.Fant,
            ScaledWidth = (uint)Math.Max(1, Math.Floor(decoder.PixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Floor(decoder.PixelHeight * scale))
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(token).ConfigureAwait(false);
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output).AsTask(token).ConfigureAwait(false);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync().AsTask(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        output.Seek(0);
        using var input = output.AsStreamForRead();
        using var bytes = new MemoryStream();
        await input.CopyToAsync(bytes, token).ConfigureAwait(false);
        return bytes.ToArray();
    }

    private async Task<bool> TryWriteDiskCacheAsync(string cacheFile, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporaryFile = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(_diskCachePath);
            await File.WriteAllBytesAsync(temporaryFile, bytes, cancellationToken);
            File.Move(temporaryFile, cacheFile, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Disk caching is best-effort; the memory cache still serves the current session.
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryFile))
                {
                    File.Delete(temporaryFile);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A stale temporary cache file is harmless and will be replaced by a later request.
            }
        }
    }

    private bool TryGetMemory(string key, out byte[]? bytes)
    {
        lock (_memoryLock)
        {
            if (!_memoryEntries.TryGetValue(key, out var node))
            {
                bytes = null;
                return false;
            }

            _memoryLru.Remove(node);
            _memoryLru.AddFirst(node);
            bytes = node.Value.Bytes;
            return true;
        }
    }

    private void Remember(string key, byte[]? bytes, string sourcePath)
    {
        lock (_memoryLock)
        {
            if (_memoryEntries.Remove(key, out var existing))
            {
                _memoryLru.Remove(existing);
                _memorySize -= existing.Value.Bytes?.LongLength ?? 0;
            }

            var node = _memoryLru.AddFirst(new MemoryEntry(key, bytes, Path.GetFullPath(sourcePath)));
            _memoryEntries.Add(key, node);
            _memorySize += bytes?.LongLength ?? 0;
            while ((_memorySize > MemoryCacheLimit || _memoryEntries.Count > MemoryEntryLimit) && _memoryLru.Last is { } last)
            {
                _memoryLru.RemoveLast();
                _memoryEntries.Remove(last.Value.Key);
                _memorySize -= last.Value.Bytes?.LongLength ?? 0;
            }
        }
    }

    private static string CreateCacheKey(FileEntry file, int width, int height)
    {
        var normalizedPath = Path.GetFullPath(file.FullPath).ToUpperInvariant();
        var keyMaterial = $"{normalizedPath}\n{file.Size}\n{file.LastModifiedUtc.Ticks}\n{width}x{height}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial)));
    }

    private sealed record MemoryEntry(string Key, byte[]? Bytes, string SourcePath);

}
