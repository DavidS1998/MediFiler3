using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MediFiler3.Application.Folders;

namespace MediFiler3.Views;

public partial class BrowseView
{
    private DispatcherTimer? _galleryTreeDragTimer;
    private bool _galleryTreeDragInside;
    private Point _galleryTreeDragPoint;
    private TreeViewItem? _galleryTreeHoverItem;
    private long _galleryTreeHoverSince;
    private static readonly TimeSpan GalleryFolderExpandDelay = TimeSpan.FromMilliseconds(600);

    private bool IsInsideGalleryDragTree(Point point) => FolderTree.IsVisible && point.X >= 0 && point.Y >= 0 &&
        point.X < FolderTree.ActualWidth && point.Y < FolderTree.ActualHeight;

    private void UpdateGalleryTreeDragNavigation(bool authorized, Point point)
    {
        if (!authorized || !IsInsideGalleryDragTree(point)) { StopGalleryTreeDragNavigation(); return; }
        _galleryTreeDragInside = true; _galleryTreeDragPoint = point;
        UpdateGalleryTreeHoverItem();
        if (_galleryTreeDragTimer is null)
        {
            _galleryTreeDragTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
            _galleryTreeDragTimer.Tick += OnGalleryTreeDragNavigationTick;
        }
        _galleryTreeDragTimer.Start();
    }

    private void StopGalleryTreeDragNavigation()
    {
        _galleryTreeDragTimer?.Stop();
        _galleryTreeDragInside = false; _galleryTreeHoverItem = null;
    }

    private TreeViewItem? GalleryTreeItemAtDragPoint() =>
        FindAncestor<TreeViewItem>(FolderTree.InputHitTest(_galleryTreeDragPoint) as DependencyObject);

    private void UpdateGalleryTreeHoverItem()
    {
        var item = GalleryTreeItemAtDragPoint();
        if (ReferenceEquals(item, _galleryTreeHoverItem)) return;
        _galleryTreeHoverItem = item;
        _galleryTreeHoverSince = Stopwatch.GetTimestamp();
    }

    private void OnGalleryTreeDragNavigationTick(object? sender, EventArgs args)
    {
        if (!_galleryTreeDragInside || !GalleryDragIsCurrent() || !IsInsideGalleryDragTree(_galleryTreeDragPoint))
        { StopGalleryTreeDragNavigation(); return; }
        var scroll = FindVisualChild<ScrollViewer>(FolderTree);
        var viewport = scroll is null ? null : FindVisualChild<ScrollContentPresenter>(scroll);
        if (scroll is not null && viewport is not null)
        {
            var point = FolderTree.TranslatePoint(_galleryTreeDragPoint, viewport);
            var band = Math.Min(32, viewport.ActualHeight / 3);
            var delta = 0d;
            if (point.X >= 0 && point.X < viewport.ActualWidth && point.Y >= 0 && point.Y < viewport.ActualHeight && band > 0)
            {
                if (point.Y < band) delta = -(8 + 12 * (1 - point.Y / band));
                else if (point.Y > viewport.ActualHeight - band) delta = 8 + 12 * (1 - (viewport.ActualHeight - point.Y) / band);
            }
            var offset = Math.Clamp(scroll.VerticalOffset + delta, 0, scroll.ScrollableHeight);
            if (delta != 0 && Math.Abs(offset - scroll.VerticalOffset) > 0.1)
            {
                scroll.ScrollToVerticalOffset(offset);
                // Scrolling changes the row under a stationary pointer. Do not expand the old row.
                _galleryTreeHoverItem = null;
                HighlightGalleryDropTarget(null);
                return;
            }
        }
        UpdateGalleryTreeHoverItem();
        if (_galleryTreeHoverItem is { IsLoaded: true, IsExpanded: false, HasItems: true, DataContext: FolderTreeEntry folder } item &&
            _navigationState.GetRootForFolder(folder) is not null && Stopwatch.GetElapsedTime(_galleryTreeHoverSince) >= GalleryFolderExpandDelay)
        {
            // Expand without selecting/navigating: the dragged source context must stay intact.
            folder.IsExpanded = true;
            item.SetCurrentValue(TreeViewItem.IsExpandedProperty, true);
            _galleryTreeHoverItem = null;
        }
        var target = GalleryTreeItemAtDragPoint();
        var valid = target?.DataContext is FolderTreeEntry destination && _navigationState.GetRootForFolder(destination) is not null &&
            _galleryMoveDrag is { } drag && !PathsEqual(destination.Path, drag.Folder.Path);
        HighlightGalleryDropTarget(valid ? target : null);
    }

    internal bool ScrollGalleryDragTreeWheel(int delta, Point screenPoint)
    {
        if (!_galleryTreeDragInside || !GalleryDragIsCurrent()) return false;
        var point = FolderTree.PointFromScreen(screenPoint);
        if (!IsInsideGalleryDragTree(point)) return false;
        if (FindVisualChild<ScrollViewer>(FolderTree) is not { } scroll) return false;
        _galleryTreeDragPoint = point; _galleryTreeHoverItem = null;
        var lines = SystemParameters.WheelScrollLines;
        var amount = lines < 0 ? scroll.ViewportHeight : lines * 28d;
        scroll.ScrollToVerticalOffset(Math.Clamp(scroll.VerticalOffset - delta / 120d * amount, 0, scroll.ScrollableHeight));
        HighlightGalleryDropTarget(null);
        return true;
    }

    private void OnGalleryTreeDragMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (ScrollGalleryDragTreeWheel(args.Delta, FolderTree.PointToScreen(args.GetPosition(FolderTree)))) args.Handled = true;
    }
}
