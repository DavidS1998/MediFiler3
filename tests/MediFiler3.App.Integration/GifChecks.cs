using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MediFiler3.Services;
using XamlAnimatedGif;

internal static partial class Program
{
    private static async Task RunGifChecksAsync()
    {
        await Check("animated GIF duration describes one cycle regardless of looping", async () =>
        {
            foreach (ushort? loops in new ushort?[] { null, 0, 3 })
            {
                var path = MakeGif("duration-" + (loops?.ToString() ?? "once") + ".gif", loops,
                    new(0, 0, 1, 1, 120, 1, [1]), new(1, 0, 1, 1, 230, 1, [2]));
                await _viewer.LoadAsync(Entry(path), true);
                Require(_viewer.Metadata?.Duration == TimeSpan.FromMilliseconds(3500), "GIF duration included repeats or lost delays");
                Require(MediFiler3.Application.Viewing.ViewerMetadataFormatter.Format(_viewer.Metadata).EndsWith(" - [4×2] - [00:03]"), "GIF duration formatting");
                _viewer.ZoomInFromToolbar();
                Require(_viewer.Metadata?.Duration == TimeSpan.FromMilliseconds(3500), "Zoom cleared GIF duration");
            }
        });
        await Check("GIF duration matches playback fallback for zero and absent delays", async () =>
        {
            var path = MakeGif("default-delays.gif", 0,
                new(0, 0, 1, 1, 0, 1, [1]), new(1, 0, 1, 1, 0, 1, [2], false),
                new(2, 0, 1, 1, 150, 1, [3]));
            await _viewer.LoadAsync(Entry(path), true);
            Require(_viewer.Metadata?.Duration == TimeSpan.FromMilliseconds(1700), "Incorrect default frame duration");
            await _viewer.LoadAsync(Fixtures[".gif"], true);
            Require(_viewer.Metadata?.Duration is null, "Static GIF retained animation duration");
            await _viewer.LoadAsync(Fixtures[".png"], true);
            Require(_viewer.Metadata?.Duration is null, "GIF duration leaked to another image");
        });
        var animated = MakeGif("animation.gif", 0,
            new(0, 0, 4, 2, 20, 1, [1, 1, 1, 1, 1, 1, 1, 1]),
            new(1, 0, 2, 1, 30, 3, [2, 0]),
            new(3, 1, 1, 1, 40, 2, [3]));
        await Check("animated GIF timing, transparent offsets and restore-previous", async () =>
        {
            await _viewer.LoadAsync(Entry(animated), true);
            await WaitUntil(() => GifSurface()?.Source is BitmapSource);
            Require(_viewer.Metadata?.PixelWidth == 4 && _viewer.Metadata.PixelHeight == 2 && _viewer.CanZoom, "GIF canvas metadata/zoom");
            await WaitUntil(() => Pixel(1, 0) == "FF0000FF");
            await WaitUntil(() => Pixel(1, 0) == "00FF00FF");
            Require(Pixel(2, 0) == "FF0000FF", "Transparent partial frame replaced the underlying pixel");
            var delay = Stopwatch.StartNew();
            await WaitUntil(() => Pixel(3, 1) == "0000FFFF");
            Require(delay.ElapsedMilliseconds >= 200, "Frame delay ignored");
            Require(Pixel(1, 0) == "FF0000FF", "Restore-previous disposal left a green trail");
            await WaitUntil(() => Pixel(3, 1) == "FF0000FF"); // Embedded zero loop count repeats.
            Require(AnimationBehavior.GetAnimator(GifSurface()!).IsComplete == false, "Infinite GIF stopped");
        });
        await Check("animated GIF zoom and resize retain animation", async () =>
        {
            var surface = GifSurface();
            _viewer.ZoomInFromToolbar();
            var window = System.Windows.Window.GetWindow(_viewer);
            window.Width += 40;
            await Task.Delay(250);
            Require(ReferenceEquals(surface, GifSurface()) && _viewer.CurrentZoomLevel > 1, "Zoom/resize restarted GIF");
            _viewer.ResetZoomToFit();
            Require(ReferenceEquals(surface, GifSurface()) && _viewer.CurrentZoomLevel == 1, "Fit restarted GIF");
            window.Width -= 40;
        });
        await Check("animated GIF does not lock the file and releases on navigation", async () =>
        {
            var old = GifSurface()!;
            using (File.Open(animated, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            var moved = animated + ".moved";
            File.Move(animated, moved); File.Move(moved, animated);
            await _viewer.LoadAsync(Fixtures[".txt"], true);
            await Task.Delay(200);
            Require(AnimationBehavior.GetAnimator(old) is null && AnimationBehavior.GetSourceStream(old) is null, "Old animator/stream retained");
            Require(!Visible("AnimatedImageHost") && Visible("TextPanel"), "Animation affected new content");
        });
        await Check("animated GIF release and reload on tab changes", async () =>
        {
            await _viewer.LoadAsync(Entry(animated), true);
            await WaitUntil(() => GifSurface()?.Source is BitmapSource);
            var old = GifSurface()!;
            _viewer.SetActive(false);
            Require(AnimationBehavior.GetAnimator(old) is null && !Visible("AnimatedImageHost"), "Inactive animator retained");
            _viewer.SetActive(true);
            await WaitUntil(() => GifSurface()?.Source is BitmapSource);
            Require(!ReferenceEquals(old, GifSurface()), "Disposed animator reused");
        });
        await Check("GIF without loop extension stops on its last frame", async () =>
        {
            var once = MakeGif("once.gif", null,
                new(0, 0, 4, 2, 5, 1, [1, 1, 1, 1, 1, 1, 1, 1]),
                new(0, 0, 4, 2, 5, 1, [2, 2, 2, 2, 2, 2, 2, 2]));
            await _viewer.LoadAsync(Entry(once), true);
            await WaitUntil(() => GifSurface() is { } image && AnimationBehavior.GetAnimator(image)?.IsComplete == true);
            Require(Pixel(0, 0) == "00FF00FF", "Finite GIF did not retain its final frame");
        });
        await Check("GIF restore-background clears only the previous rectangle", async () =>
        {
            var background = MakeGif("background.gif", null,
                new(0, 0, 4, 2, 5, 1, [1, 1, 1, 1, 1, 1, 1, 1]),
                new(1, 0, 1, 1, 5, 2, [2]),
                new(3, 1, 1, 1, 5, 1, [3]));
            await _viewer.LoadAsync(Entry(background), true);
            await WaitUntil(() => Pixel(3, 1) == "0000FFFF");
            Require(Pixel(1, 0) == "00000000" && Pixel(0, 0) == "FF0000FF", "Background disposal cleared the wrong area");
        });
        await Check("GIF finite embedded loop count is honored", async () =>
        {
            var twice = MakeGif("twice.gif", 2,
                new(0, 0, 4, 2, 10, 1, [1, 1, 1, 1, 1, 1, 1, 1]),
                new(0, 0, 4, 2, 10, 1, [2, 2, 2, 2, 2, 2, 2, 2]));
            await _viewer.LoadAsync(Entry(twice), true);
            await WaitUntil(() => Pixel(0, 0) == "00FF00FF");
            await WaitUntil(() => Pixel(0, 0) == "FF0000FF");
            await WaitUntil(() => GifSurface() is { } image && AnimationBehavior.GetAnimator(image)?.IsComplete == true);
            Require(Pixel(0, 0) == "00FF00FF", "Finite repeats did not end on last frame");
        });
        await Check("GIF canvas dimensions differ from partial first frame", async () =>
        {
            var partial = MakeGif("partial.gif", 0,
                new(1, 0, 1, 1, 5, 1, [1]), new(2, 1, 1, 1, 5, 1, [2]));
            await _viewer.LoadAsync(Entry(partial), true);
            await WaitUntil(() => GifSurface()?.Source is BitmapSource);
            Require(_viewer.Metadata?.PixelWidth == 4 && _viewer.Metadata.PixelHeight == 2, "First frame rectangle used as canvas");
            var source = (BitmapSource)GifSurface()!.Source;
            Require(source.PixelWidth == 4 && source.PixelHeight == 2, "Wrong animation canvas");
        });
        await Check("rapid animated GIF selections leave no orphan surface", async () =>
        {
            var tasks = new List<Task>();
            for (int i = 0; i < 12; i++)
            {
                tasks.Add(_viewer.LoadAsync(Entry(animated), true));
                await Task.Delay(1);
                tasks.Add(_viewer.LoadAsync(Fixtures[".txt"], true));
            }
            await Task.WhenAll(tasks);
            await Task.Delay(100);
            Require(GifSurface() is null && Visible("TextPanel") && !_viewer.CanZoom, "Late animation replaced text");
        });
        await Check("corrupt animated GIF retains external-opening fallback", async () =>
        {
            var path = MakeGif("corrupt-animation.gif", 0,
                new(0, 0, 1, 1, 5, 1, [1]), new(1, 0, 1, 1, 5, 1, [2]));
            var bytes = await File.ReadAllBytesAsync(path);
            int descriptor = Array.IndexOf(bytes, (byte)0x2c);
            bytes[descriptor + 10] = 12; // Invalid GIF LZW minimum code size.
            await File.WriteAllBytesAsync(path, bytes);
            await _viewer.LoadAsync(Entry(path), true);
            await WaitUntil(() => Visible("ExternalFallbackPanel"));
            Require(GifSurface() is null && _viewer.ExternalOpenState.CanChooseApplication, "Corrupt animation retained its surface or lost fallback");
            await _viewer.LoadAsync(Fixtures[".png"], true);
            Require(_viewer.CanZoom && !Visible("ExternalFallbackPanel"), "Could not recover after corrupt animation");
        });
    }
    private static Image? GifSurface() => ((ContentControl)_viewer.FindName("AnimatedImageHost")).Content as Image;
    private static string Pixel(int x, int y)
    {
        if (GifSurface()?.Source is not BitmapSource source) return "";
        var pixel = new byte[4];
        source.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return $"{pixel[2]:X2}{pixel[1]:X2}{pixel[0]:X2}{pixel[3]:X2}";
    }
    private sealed record GifFrame(ushort X, ushort Y, ushort Width, ushort Height, ushort Delay, int Disposal, byte[] Pixels, bool Control = true);
    private static string MakeGif(string name, ushort? loops, params GifFrame[] frames)
    {
        var path = Path.Combine(Root, name);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("GIF89a")); writer.Write((ushort)4); writer.Write((ushort)2);
        writer.Write(new byte[] { 0x81, 0, 0, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255 });
        if (loops is { } count)
        {
            writer.Write(new byte[] { 0x21, 0xff, 11 }); writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            writer.Write(new byte[] { 3, 1 }); writer.Write(count); writer.Write((byte)0);
        }
        foreach (var frame in frames)
        {
            if (frame.Control)
            {
                writer.Write(new byte[] { 0x21, 0xf9, 4, (byte)(frame.Disposal << 2 | 1) });
                writer.Write(frame.Delay); writer.Write((ushort)0);
            }
            writer.Write((byte)0x2c); writer.Write(frame.X); writer.Write(frame.Y); writer.Write(frame.Width); writer.Write(frame.Height); writer.Write((byte)0);
            // Emit a clear code before each pixel so the tiny fixture needs no LZW dictionary encoder.
            var codes = frame.Pixels.SelectMany(pixel => new[] { 4, (int)pixel }).Append(5).ToArray();
            var data = new byte[(codes.Length * 3 + 7) / 8];
            for (int index = 0; index < codes.Length; index++)
                for (int bit = 0; bit < 3; bit++)
                    data[(index * 3 + bit) / 8] |= (byte)(((codes[index] >> bit) & 1) << ((index * 3 + bit) % 8));
            writer.Write((byte)2); writer.Write((byte)data.Length); writer.Write(data); writer.Write((byte)0);
        }
        writer.Write((byte)0x3b);
        return path;
    }
}
