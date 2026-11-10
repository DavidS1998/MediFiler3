using System.IO;
using System.Windows.Media.Imaging;
using MediFiler3.Application.Files;
using MediFiler3.Application.Viewing;
using MediFiler3.Domain.Files;

namespace MediFiler3.Services;

public sealed class ImageViewerHandler(WicImageDecoder decoder) : IFileViewerHandler<ViewerPresentation>
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };
    public bool CanHandle(FileEntry file) => Extensions.Contains(file.Extension);
    public ViewerCapabilities Capabilities => ViewerCapabilities.Zoom | ViewerCapabilities.Pan;
    public async Task<ViewerContent<ViewerPresentation>> LoadAsync(FileEntry file, int width, int height, CancellationToken cancellationToken)
    {
        var image = await decoder.LoadWithDimensionsAsync(file.FullPath, width, height, cancellationToken);
        return new(new BitmapPresentation(image.Source), new ViewerMetadata(file.Size, image.Width, image.Height));
    }
}

public sealed class ThumbnailViewerHandler(IThumbnailService thumbnails) : IFileViewerHandler<ViewerPresentation>
{
    public bool CanHandle(FileEntry file) => true;
    public ViewerCapabilities Capabilities => ViewerCapabilities.None;
    public async Task<ViewerContent<ViewerPresentation>> LoadAsync(FileEntry file, int width, int height, CancellationToken cancellationToken)
    {
        var bytes = await thumbnails.GetThumbnailPngAsync(file, width, height, cancellationToken);
        var source = bytes is null ? null : await Task.Run(() => DecodePng(bytes), cancellationToken);
        return new(source is null ? null : new BitmapPresentation(source), new ViewerMetadata(file.Size), IsThumbnailFallback: true);
    }

    public static BitmapSource DecodePng(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
