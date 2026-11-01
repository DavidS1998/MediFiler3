using System.IO;
using System.Windows.Media.Imaging;
using MediFiler3.Application.Files;

namespace MediFiler3.Services;

public sealed class WicImageDecoder(IFileContentService fileContentService)
{
    public async Task<BitmapSource> LoadAsync(string filePath, int maxPixelWidth, int maxPixelHeight, CancellationToken cancellationToken)
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
        return bitmap;
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
