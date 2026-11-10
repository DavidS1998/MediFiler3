using System.Windows;
using System.Windows.Media;
using MediFiler3.Services;

namespace MediFiler3.Views;

public partial class ViewerHostView
{
    private GifPresentation? _gif;
    private ImageSource? DisplayedImageSource => _gif?.Surface.Source ?? ImageViewer.Source;

    private void ShowGif(GifPresentation gif)
    {
        _gif = gif;
        gif.Surface.RenderTransform = ImageViewer.RenderTransform;
        gif.Ready += OnGifReady;
        gif.Failed += OnGifFailed;
        AnimatedImageHost.Content = gif.Surface;
        AnimatedImageHost.Visibility = Visibility.Visible;
        ImageViewer.Source = null;
        ImageViewer.Visibility = TextPanel.Visibility = ThumbnailFallbackBadge.Visibility = ImageMessagePanel.Visibility = Visibility.Collapsed;
        gif.Start();
        PresentationChanged?.Invoke();
    }
    private void OnGifReady()
    {
        if (!_active || _gif is null) return;
        ClampPan();
        PresentationChanged?.Invoke();
    }
    private void OnGifFailed(string message)
    {
        if (!_active || _gif is null || _currentFile is not { } file) return;
        ReleaseGif();
        ShowImageMessage("This GIF could not be animated.", message);
        ShowExternalFallback(file, previewFailed: true);
    }
    private void ReleaseGif()
    {
        if (_gif is { } gif)
        {
            _gif = null;
            gif.Ready -= OnGifReady; gif.Failed -= OnGifFailed;
            gif.Dispose();
        }
        AnimatedImageHost.Content = null;
        AnimatedImageHost.Visibility = Visibility.Collapsed;
    }
}
