using System.IO;
using System.Windows.Media.Imaging;
using MediFiler3.Application.Files;

namespace MediFiler3.Services;

public sealed class WicImageDecoder(IFileContentService fileContentService)
{
    public async Task<GifPresentation?> LoadAnimationAsync(string path, CancellationToken token)
    {
        var buffer = new MemoryStream();
        try
        {
            await using (var file = await fileContentService.OpenReadAsync(path, token))
                await file.CopyToAsync(buffer, token);
            var animation = await Task.Run(() =>
            {
                buffer.Position = 0;
                try
                {
                    var decoder = new GifBitmapDecoder(buffer, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    int count = decoder.Frames.Count;
                    long milliseconds = 0;
                    if (count > 1)
                        foreach (var frame in decoder.Frames)
                        {
                            token.ThrowIfCancellationRequested();
                            var metadata = frame.Metadata as BitmapMetadata;
                            var delay = metadata?.ContainsQuery("/grctlext/Delay") == true
                                ? metadata.GetQuery("/grctlext/Delay") as ushort? : null;
                            // WIC delay units are 10 ms. Match XamlAnimatedGif's 100 ms fallback for zero/missing delays.
                            milliseconds += delay is > 0 ? delay.Value * 10L : 100L;
                        }
                    return (FrameCount: count, Duration: TimeSpan.FromMilliseconds(milliseconds));
                }
                catch (ArgumentException exception) { throw new InvalidDataException("The GIF data is malformed or unsupported.", exception); }
            }, token);
            token.ThrowIfCancellationRequested();
            if (animation.FrameCount <= 1) { buffer.Dispose(); return null; }
            // GIF dimensions describe the logical canvas, not the first frame's potentially smaller rectangle.
            if (buffer.Length < 10) throw new InvalidDataException("Incomplete GIF header.");
            var header = buffer.GetBuffer();
            int width = header[6] | header[7] << 8;
            int height = header[8] | header[9] << 8;
            if (width == 0 || height == 0) throw new InvalidDataException("Invalid GIF canvas dimensions.");
            buffer.Position = 0;
            return new GifPresentation(buffer, width, height, animation.Duration);
        }
        catch { buffer.Dispose(); throw; }
    }

    public async Task<BitmapSource> LoadAsync(string filePath, int maxPixelWidth, int maxPixelHeight, CancellationToken cancellationToken)
        => (await LoadWithDimensionsAsync(filePath, maxPixelWidth, maxPixelHeight, cancellationToken)).Source;

    public async Task<(BitmapSource Source, int Width, int Height)> LoadWithDimensionsAsync(
        string filePath, int maxPixelWidth, int maxPixelHeight, CancellationToken cancellationToken)
    {
        maxPixelWidth = Math.Max(1, maxPixelWidth);
        maxPixelHeight = Math.Max(1, maxPixelHeight);

        int originalWidth;
        int originalHeight;
        await using (var metadataStream = await fileContentService.OpenReadAsync(filePath, cancellationToken))
        {
            var dimensions = await Task.Run(() => ReadDimensions(metadataStream), cancellationToken);
            originalWidth = dimensions.Width;
            originalHeight = dimensions.Height;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var scale = Math.Min(1d, Math.Min((double)maxPixelWidth / originalWidth, (double)maxPixelHeight / originalHeight));
        var decodeWidth = Math.Max(1, (int)Math.Floor(originalWidth * scale));

        await using var imageStream = await fileContentService.OpenReadAsync(filePath, cancellationToken);
        var bitmap = await Task.Run(() => Decode(imageStream, decodeWidth), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return (bitmap, originalWidth, originalHeight);
    }

    private static (int Width, int Height) ReadDimensions(Stream stream)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("The image contains no frames.");
        }

        var frame = decoder.Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private static BitmapSource Decode(Stream stream, int decodeWidth)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.DecodePixelWidth = decodeWidth;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
