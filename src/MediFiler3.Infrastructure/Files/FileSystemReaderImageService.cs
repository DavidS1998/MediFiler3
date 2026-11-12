using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using MediFiler3.Application.Files;
using MediFiler3.Domain.Files;
using Windows.Graphics.Imaging;

namespace MediFiler3.Infrastructure.Files;

/// <summary>Source-image, first-frame decoding. Independent of the thumbnail disk cache.</summary>
public sealed class FileSystemReaderImageService(IFileContentService content) : IReaderImageService
{
    public const int MaximumPixels = 4 * 1024 * 1024;
    private readonly SemaphoreSlim _workers = new(2);
    public async Task<ReaderImage> DecodeAsync(FileEntry file, int displayPixelWidth, CancellationToken token)
    {
        await _workers.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            return await Task.Run(async () =>
            {
                await using var input = await content.OpenReadAsync(file.FullPath, token).ConfigureAwait(false);
                using var random = input.AsRandomAccessStream();
                var decoder = await BitmapDecoder.CreateAsync(random).AsTask(token).ConfigureAwait(false);
                var width = decoder.OrientedPixelWidth; var height = decoder.OrientedPixelHeight;
                if (width == 0 || height == 0) throw new InvalidDataException("Image has no dimensions.");
                var scale = Math.Min(1d, Math.Min((double)Math.Max(1, displayPixelWidth) / width, Math.Sqrt((double)MaximumPixels / ((double)width * height))));
                var transform = new BitmapTransform { InterpolationMode = BitmapInterpolationMode.Fant,
                    ScaledWidth = (uint)Math.Max(1, Math.Floor(decoder.PixelWidth * scale)),
                    ScaledHeight = (uint)Math.Max(1, Math.Floor(decoder.PixelHeight * scale)) };
                using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if ((long)bitmap.PixelWidth * bitmap.PixelHeight > MaximumPixels) throw new InvalidDataException("Decoded image exceeded the reader pixel limit.");
                var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)]; bitmap.CopyToBuffer(pixels.AsBuffer());
                return new ReaderImage(pixels, bitmap.PixelWidth, bitmap.PixelHeight, checked((int)width), checked((int)height));
            }, token).ConfigureAwait(false);
        }
        finally { _workers.Release(); }
    }
}
