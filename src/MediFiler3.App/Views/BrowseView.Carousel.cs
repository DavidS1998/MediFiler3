using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MediFiler3.Views;

public partial class BrowseView
{
    public Func<bool>? SidebarHoverProbe { get; set; }
    private bool _sidebarFolderReveal;

    public void UpdateShellFolderHover(bool sidebar, Point point)
    {
        if (_fullscreenCarousel) return; // Fullscreen retains its coordinated shell/tree reveal.
        _sidebarFolderReveal = sidebar;
        var inside = point.X >= 0 && point.Y >= 0 && point.Y < ActualHeight;
        SetFolderOverlayVisible(sidebar || _folderOverlayPinned || inside &&
            _folderOverlayVisible && point.X <= FolderOverlay.Width);
    }
    private void OpenCarouselFileInGallery(MediFiler3.Domain.Files.FileEntry file)
    {
        if (!_viewerActive || IsGallery || !_navigationState.ContainsFile(file)) return;
        _navigationState.SetCurrentFile(file);
        GalleryRequested?.Invoke();
    }

    private void HandleCarouselTileClick(MediFiler3.Domain.Files.FileEntry file, int clickCount)
    {
        if (clickCount == 2) OpenCarouselFileInGallery(file);
        else if (!ReferenceEquals(file, _navigationState.CurrentFile)) SelectFile(file);
    }
    private bool _carouselEnabled = true;
    private bool _fullscreenCarousel;
    private bool _fullscreenFolderReveal;
    public double FullscreenFolderRevealWidth => _folderOverlayVisible ? FolderOverlay.Width : 0;
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

    public void SetCarouselEnabled(bool enabled)
    {
        _carouselEnabled = enabled;
        _carouselRefreshVersion++;
        _carouselLoadCancellation?.Cancel();
        CarouselOverlay.Visibility = enabled && !IsGallery ? Visibility.Visible : Visibility.Collapsed;
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
        var picture = new Border { Width = 150, Height = 150, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromRgb(11, 12, 14)),
            Child = item.Thumbnail is null ? new TextBlock { Text = item.PlaceholderText, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }
                : new Image { Source = item.Thumbnail, Stretch = Stretch.UniformToFill } };
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
