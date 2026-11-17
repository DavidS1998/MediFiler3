using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MediFiler3.Views;

public partial class BrowseView
{
    public Func<Point?>? FolderHoverPositionProbe { get; set; }
    private bool _folderHoverReveal;
    private double _folderRevealSidebarWidth;
    public Rect FolderRevealBounds => new(_fullscreenCarousel ? 0 : -_folderRevealSidebarWidth, 0,
        _folderRevealSidebarWidth + (IsGallery && !_folderOverlayVisible && !_fullscreenFolderReveal && !_folderOverlayPinned ? 0 : FolderOverlay.Width), Math.Max(0, ActualHeight));
    public Rect CarouselRevealBounds => new(0, Math.Max(0, ActualHeight - CarouselOverlay.Height),
        Math.Max(0, ActualWidth), Math.Min(Math.Max(0, ActualHeight), CarouselOverlay.Height));
    public void UpdateShellCarouselHover(Point? point) => SetCarouselOverlayVisible(
        _viewerActive && point is { } position && CarouselRevealBounds.Contains(position));
    public void SetFolderRevealSidebarWidth(double width) => _folderRevealSidebarWidth = width;

    public void UpdateShellFolderHover(Point? point)
    {
        if (_fullscreenCarousel) return; // Fullscreen retains its coordinated shell/tree reveal.
        _folderHoverReveal = point is { } position && FolderRevealBounds.Contains(position);
        SetFolderOverlayVisible(_folderOverlayPinned || _folderHoverReveal);
    }
    private void OpenCarouselFileInGallery(MediFiler3.Domain.Files.FileEntry file)
    {
        if (!_viewerActive || !IsBrowse || !_navigationState.ContainsFile(file)) return;
        _navigationState.SetCurrentFile(file);
        GalleryRequested?.Invoke();
    }

    private void HandleCarouselTileClick(MediFiler3.Domain.Files.FileEntry file, int clickCount)
    {
        if (!IsBrowse) return;
        if (clickCount == 2) OpenCarouselFileInGallery(file);
        else if (!ReferenceEquals(file, _navigationState.CurrentFile)) SelectFile(file);
    }
    private bool _carouselEnabled = true;
    private bool _fullscreenCarousel;
    private bool _fullscreenFolderReveal;
    public void SetFullscreenFolderReveal(bool reveal, double sidebarWidth)
    {
        _fullscreenFolderReveal = reveal;
        var margin = FolderOverlay.Margin;
        FolderOverlay.Margin = new Thickness(reveal ? sidebarWidth : 0, margin.Top, margin.Right, margin.Bottom);
        UpdateGalleryTreeInset();
        SetFolderOverlayVisible(reveal || _folderOverlayPinned);
    }
    private bool _carouselRefreshQueued;
    private IReadOnlyList<Rect> _shellVideoExclusions = [];
    public bool CarouselEnabled => _carouselEnabled;

    private void UpdateTreeCarouselClearance() => TreeScrollClearance = _carouselEnabled && IsBrowse ? CarouselOverlay.Height : 48;

    public void SetCarouselEnabled(bool enabled)
    {
        _carouselEnabled = enabled;
        UpdateTreeCarouselClearance();
        _carouselRefreshVersion++;
        _carouselLoadCancellation?.Cancel();
        CarouselOverlay.Visibility = enabled && IsBrowse ? Visibility.Visible : Visibility.Collapsed;
        CarouselGhosts.Children.Clear();
        if (!enabled) { _carouselScrollTimer.Stop(); CancelCarouselItemLoads(); CarouselItems.Clear(); }
        SetFullscreenCarousel(_fullscreenCarousel);
        if (enabled) { _hasCenteredCarouselItem = false; _ = RefreshCarouselAsync(); }
    }

    private void CancelCarouselItemLoads()
    {
        foreach (var item in CarouselItems.OfType<CarouselThumbnailItem>())
        {
            item.ThumbnailCancellation?.Cancel();
            item.ThumbnailCancellation?.Dispose();
            item.ThumbnailCancellation = null;
            item.ThumbnailTask = null;
        }
    }

    public void SetFullscreenCarousel(bool fullscreen)
    {
        _fullscreenCarousel = fullscreen;
        if (!fullscreen) SetFullscreenFolderReveal(false, 0);
        _carouselPanelAnimating = false;
        _carouselOverlayVisible = false;
        CarouselOverlay.IsHitTestVisible = false;
        CarouselList.IsHitTestVisible = false;
        CarouselOverlayTransform.BeginAnimation(TranslateTransform.YProperty, null);
        CarouselOverlay.BeginAnimation(OpacityProperty, null);
        CarouselOverlayTransform.Y = CarouselOverlay.Height * (fullscreen ? 1 : 0.875);
        CarouselOverlay.Opacity = fullscreen ? 0 : 0.65;
    }

    public void SetShellVideoExclusions(IReadOnlyList<Rect> exclusions) => _shellVideoExclusions = exclusions;

    private void QueueCarouselRefresh()
    {
        if (_carouselRefreshQueued || !_carouselEnabled) return;
        _carouselRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(async () =>
        {
            _carouselRefreshQueued = false;
            if (_viewerActive) await RefreshCarouselAsync();
        }));
    }

    private void OnCarouselRequestBringIntoView(object sender, RequestBringIntoViewEventArgs args)
    {
        // Selection must not race explicit index-based centering with ListBox auto-scrolling.
        args.Handled = true;
    }

    private void OnCarouselItemLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not ListBoxItem { DataContext: CarouselThumbnailItem } container || !SystemParameters.ClientAreaAnimation) return;
        container.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110)));
    }

    private void FadeOutCarouselItem(CarouselThumbnailItem item)
    {
        if (!SystemParameters.ClientAreaAnimation || CarouselOverlay.Visibility != Visibility.Visible || CarouselOverlay.Opacity <= 0 ||
            CarouselList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container) return;
        var point = container.TranslatePoint(new Point(), CarouselGhosts);
        if (point.X + 156 < 0 || point.X > CarouselList.ActualWidth) return;
        // A noninteractive visual keeps the old edge tile alive while the real list removes it immediately.
        var tile = new StackPanel { Width = 156, Height = 170, IsHitTestVisible = false };
        var picture = new Border { Width = 150, Height = 150, Background = new SolidColorBrush(Color.FromRgb(11, 12, 14)),
            Child = item.Thumbnail is null ? new TextBlock { Text = item.PlaceholderText, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }
                : new Image { Source = item.Thumbnail, Stretch = Stretch.UniformToFill } };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        tile.Children.Add(picture);
        tile.Children.Add(new TextBlock { Text = item.Name, Foreground = Brushes.LightGray, FontSize = 11, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        Canvas.SetLeft(tile, point.X); Canvas.SetTop(tile, point.Y);
        while (CarouselGhosts.Children.Count >= 24) CarouselGhosts.Children.RemoveAt(0);
        CarouselGhosts.Children.Add(tile);
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110));
        fade.Completed += (_, _) => CarouselGhosts.Children.Remove(tile);
        tile.BeginAnimation(OpacityProperty, fade);
    }
}
