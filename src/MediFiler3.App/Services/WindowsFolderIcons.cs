using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MediFiler3.Services;

internal static class WindowsFolderIcons
{
    private const int CacheCapacity = 512;
    private const uint Icon = 0x100;
    private const uint SmallIcon = 0x1;
    private static readonly SemaphoreSlim Workers = new(2);
    private static readonly Dictionary<(string Path, bool Small), BitmapSource> Cache = new();
    private static readonly Queue<(string Path, bool Small)> CacheOrder = new();

    public static async Task<BitmapSource?> GetAsync(string path, bool small, CancellationToken token)
    {
        var key = (path.ToUpperInvariant(), small);
        lock (Cache)
            if (Cache.TryGetValue(key, out var cached)) return cached;

        await Workers.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                lock (Cache)
                    if (Cache.TryGetValue(key, out var cached)) return cached;

                var bitmap = ReadIcon(path, small);
                if (bitmap is not null)
                {
                    lock (Cache)
                    {
                        if (Cache.TryAdd(key, bitmap))
                        {
                            CacheOrder.Enqueue(key);
                            if (CacheOrder.Count > CacheCapacity) Cache.Remove(CacheOrder.Dequeue());
                        }
                    }
                }
                return bitmap;
            }, token).ConfigureAwait(false);
        }
        finally
        {
            // A native lookup must finish before its slot can be reused.
            Workers.Release();
        }
    }

    private static BitmapSource? ReadIcon(string path, bool small)
    {
        const uint apartmentThreaded = 2;
        const int changedMode = unchecked((int)0x80010106);
        var result = CoInitializeEx(0, apartmentThreaded);
        if (result < 0 && result != changedMode) return null;

        try
        {
            var found = SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<ShellFileInfo>(),
                Icon | (small ? SmallIcon : 0));
            if (info.IconHandle == 0) return null;

            try
            {
                if (found == 0) return null;
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(info.IconHandle, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                return bitmap;
            }
            catch (ExternalException)
            {
                return null;
            }
            finally
            {
                DestroyIcon(info.IconHandle);
            }
        }
        finally
        {
            if (result >= 0) CoUninitialize();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint IconHandle;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint flags);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
