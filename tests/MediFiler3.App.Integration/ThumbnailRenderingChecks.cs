using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediFiler3.Services;

internal static partial class Program
{
    private static async Task RunThumbnailRenderingChecksAsync()
    {
        static byte[] Png(byte[] pixels, int width, int height)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4)));
            using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
        }
        static byte[] Pixels(BitmapSource bitmap)
        {
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            converted.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
        }
        await Check("Thumbnail display decoding preserves original cached PNG pixels and dimensions without blur", async () =>
        {
            const int width = 17, height = 9;
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var index = (y * width + x) * 4;
                    pixels[index] = pixels[index + 1] = pixels[index + 2] = x < 8 ? (byte)0 : (byte)255;
                    pixels[index + 3] = 255;
                }
            var original = Png(pixels, width, height); var before = original.ToArray();
            var bitmap = await Task.Run(() => ThumbnailViewerHandler.DecodePng(original));
            Require(bitmap.PixelWidth == width && bitmap.PixelHeight == height && bitmap.IsFrozen, "Raw decode changed geometry or thread safety");
            Require(Pixels(bitmap).SequenceEqual(pixels), "Display decoding altered or blurred the cached pixels");
            var repeated = await Task.Run(() => ThumbnailViewerHandler.DecodePng(original));
            Require(original.SequenceEqual(before) && Pixels(repeated).SequenceEqual(pixels), "Repeated decode changed cached PNGs or pixels");
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
            { writer.WriteBytes(original); await writer.StoreAsync(); await writer.FlushAsync(); }
            stream.Seek(0);
            var native = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            await native.SetSourceAsync(stream);
            Require(native.PixelWidth == width && native.PixelHeight == height, "Native raw PNG decode changed dimensions");
        });
        await Check("Unfiltered thumbnail decoding preserves tiny sources alpha and canceled background work", async () =>
        {
            var original = Png([0, 0, 128, 128], 1, 1);
            var bitmap = await Task.Run(() => ThumbnailViewerHandler.DecodePng(original));
            var pixels = Pixels(bitmap);
            Require(bitmap.PixelWidth == 1 && bitmap.PixelHeight == 1 && pixels[0] == 0 && pixels[1] == 0 && pixels[2] == pixels[3] && pixels[3] == 128,
                "Tiny transparent image gained a dark/color halo or lost opacity");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var started = false;
            try { await Task.Run(() => { started = true; return ThumbnailViewerHandler.DecodePng(original); }, cancellation.Token); throw new InvalidOperationException("Canceled decode completed"); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            Require(!started, "Canceled background decode started anyway");
        });
    }
}
