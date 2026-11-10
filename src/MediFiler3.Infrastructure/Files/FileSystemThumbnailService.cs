using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using MediFiler3.Application.Files;
using MediFiler3.Domain.Files;

namespace MediFiler3.Infrastructure.Files;

public sealed class FileSystemThumbnailService : IThumbnailService
{
    private const long MemoryCacheLimit = 32 * 1024 * 1024;
    private const int MemoryEntryLimit = 512;
    private const uint ThumbnailOnly = 0x00000008;
    private static readonly HashSet<string> WicExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    private readonly SemaphoreSlim _workers = new(3, 3);
    private readonly object _memoryLock = new();
    private readonly Dictionary<string, LinkedListNode<MemoryEntry>> _memoryEntries = new(StringComparer.Ordinal);
    private readonly LinkedList<MemoryEntry> _memoryLru = [];
    private long _memorySize;
    private readonly string _diskCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediFiler3", "ThumbnailCache");

    public async Task<byte[]?> GetThumbnailPngAsync(FileEntry file, int width, int height, CancellationToken cancellationToken)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var cacheKey = CreateCacheKey(file, width, height);
        if (TryGetMemory(cacheKey, out var memoryHit))
        {
            return memoryHit;
        }

        await _workers.WaitAsync(cancellationToken);
        try
        {
            if (TryGetMemory(cacheKey, out memoryHit))
            {
                return memoryHit;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var cacheFile = Path.Combine(_diskCachePath, cacheKey + ".png");
            try
            {
                if (File.Exists(cacheFile))
                {
                    var cachedBytes = await File.ReadAllBytesAsync(cacheFile, cancellationToken);
                    Remember(cacheKey, cachedBytes);
                    return cachedBytes;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Cache failures fall through to thumbnail generation.
            }

            var pngBytes = await Task.Run(() => GenerateThumbnail(file, width, height, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Remember(cacheKey, pngBytes);
            if (pngBytes is not null)
            {
                await TryWriteDiskCacheAsync(cacheFile, pngBytes, cancellationToken);
            }

            return pngBytes;
        }
        finally
        {
            _workers.Release();
        }
    }

    private byte[]? GenerateThumbnail(FileEntry file, int width, int height, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (WicExtensions.Contains(file.Extension))
        {
            try
            {
                var bitmap = DecodeWic(file.FullPath, width, height);
                cancellationToken.ThrowIfCancellationRequested();
                return EncodePng(bitmap);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               InvalidDataException or FormatException or NotSupportedException or COMException or
                                               System.Security.SecurityException)
            {
                // The Shell may still be able to provide a thumbnail for this file.
            }
        }

        try
        {
            return TryCreateShellThumbnail(file.FullPath, width, height);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException or
                                           System.Security.SecurityException or InvalidOperationException)
        {
            // The caller will display a placeholder when no thumbnail is available.
            return null;
        }
    }

    private static BitmapSource DecodeWic(string filePath, int maxWidth, int maxHeight)
    {
        int sourceWidth;
        int sourceHeight;
        using (var stream = File.OpenRead(filePath))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0)
            {
                throw new InvalidDataException("The image contains no frames.");
            }

            sourceWidth = decoder.Frames[0].PixelWidth;
            sourceHeight = decoder.Frames[0].PixelHeight;
        }

        var scale = Math.Min(1d, Math.Min((double)maxWidth / sourceWidth, (double)maxHeight / sourceHeight));
        var decodeWidth = Math.Max(1, (int)Math.Floor(sourceWidth * scale));
        using var imageStream = File.OpenRead(filePath);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.DecodePixelWidth = decodeWidth;
        bitmap.StreamSource = imageStream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[]? TryCreateShellThumbnail(string filePath, int width, int height)
    {
        var interfaceId = typeof(IShellItemImageFactory).GUID;
        var result = SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref interfaceId, out var imageFactory);
        if (result < 0 || imageFactory is null)
        {
            return null;
        }

        IntPtr bitmapHandle = IntPtr.Zero;
        try
        {
            var size = new NativeSize(width, height);
            result = imageFactory.GetImage(size, ThumbnailOnly, out bitmapHandle);
            if (result < 0 || bitmapHandle == IntPtr.Zero)
            {
                return null;
            }

            var bitmap = Imaging.CreateBitmapSourceFromHBitmap(bitmapHandle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return EncodePng(bitmap);
        }
        finally
        {
            if (bitmapHandle != IntPtr.Zero)
            {
                DeleteObject(bitmapHandle);
            }

            Marshal.ReleaseComObject(imageFactory);
        }
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private async Task TryWriteDiskCacheAsync(string cacheFile, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporaryFile = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(_diskCachePath);
            await File.WriteAllBytesAsync(temporaryFile, bytes, cancellationToken);
            File.Move(temporaryFile, cacheFile, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Disk caching is best-effort; the memory cache still serves the current session.
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

    private void Remember(string key, byte[]? bytes)
    {
        lock (_memoryLock)
        {
            if (_memoryEntries.Remove(key, out var existing))
            {
                _memoryLru.Remove(existing);
                _memorySize -= existing.Value.Bytes?.LongLength ?? 0;
            }

            var node = _memoryLru.AddFirst(new MemoryEntry(key, bytes));
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

    private sealed record MemoryEntry(string Key, byte[]? Bytes);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize(int width, int height)
    {
        public readonly int Width = width;
        public readonly int Height = height;
    }

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, uint flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory imageFactory);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}
