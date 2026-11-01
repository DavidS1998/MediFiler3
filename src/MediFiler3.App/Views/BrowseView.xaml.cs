using System.Windows.Controls;
using System.Windows;
using System.IO;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using MediFiler3.Application.Files;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;
using MediFiler3.Services;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Media.Animation;

namespace MediFiler3.Views;

public partial class BrowseView : UserControl
{
    private readonly IFolderTreeService _folderTreeService;
    private readonly IFileListingService _fileListingService;
    private readonly NavigationSessionState _navigationState;
    private readonly WicImageDecoder _imageDecoder;
    private readonly IThumbnailService _thumbnailService;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _fileLoadCancellation;
    private CancellationTokenSource? _imageLoadCancellation;
    private CancellationTokenSource? _carouselLoadCancellation;
    private int _overscrollDirection;
    private int _overscrollCount;
    private int _wheelDeltaRemainder;
    private int _zoomWheelDeltaRemainder;
    private bool _changingFolderFromOverscroll;
    private bool _zoomInteractionEnabled;
    private bool _isPanning;
    private System.Windows.Point _lastPanPoint;
    private double _zoomLevel = 1d;
    private bool _folderOverlayVisible;
    private bool _carouselOverlayVisible;
    private bool _carouselPanelAnimating;
    private CarouselSpacerItem? _carouselLeadingSpacer;
    private CarouselSpacerItem? _carouselTrailingSpacer;
    private readonly DispatcherTimer _carouselScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTime _carouselScrollStartedAt;
    private double _carouselScrollStartOffset;
    private double _carouselScrollTargetOffset;
    private double _carouselOffsetBeforeRefresh;
    private ScrollViewer? _carouselScrollViewer;
    private int _carouselStartIndex;
    private int _carouselEndIndex;
    private int _carouselFileCount;
    private int _carouselCurrentIndex;
    private int _carouselRefreshVersion;
    private int _carouselWheelDeltaRemainder;
    private bool _hasCenteredCarouselItem;
    private int _carouselNeighborsOnEachSide = 5;
    private readonly ScaleTransform _zoomTransform = new(1d, 1d);
    private readonly TranslateTransform _panTransform = new();
    private readonly DispatcherTimer _resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private const int OverscrollAttemptsToChangeFolder = 5;
    private const double MinimumZoom = 0.2d;
    private const double MaximumZoom = 5d;
    private const double ZoomStepFactor = 1.25d;
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"
    };
    public event Action<string>? StatusChanged;
    public ObservableCollection<object> CarouselItems { get; } = [];
    public double CurrentZoomLevel => _zoomLevel;
    public int CarouselNeighborCount => _carouselNeighborsOnEachSide;
    public event Action<double>? ZoomLevelChanged;

    public BrowseView(IFolderTreeService folderTreeService, IFileListingService fileListingService, NavigationSessionState navigationState,
        WicImageDecoder imageDecoder, IThumbnailService thumbnailService)
    {
        _folderTreeService = folderTreeService;
        _fileListingService = fileListingService;
        _navigationState = navigationState;
        _imageDecoder = imageDecoder;
        _thumbnailService = thumbnailService;
        InitializeComponent();
        ImageViewer.RenderTransform = new TransformGroup
        {
            Children = { _zoomTransform, _panTransform }
        };
        DataContext = navigationState;
        CarouselList.ItemsSource = CarouselItems;
        _resizeTimer.Tick += OnImageResizeTimerTick;
        _carouselScrollTimer.Tick += OnCarouselScrollTimerTick;
    }

    public async Task LoadRootsAsync(IEnumerable<string> paths)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;
        _fileLoadCancellation?.Cancel();
        ClearActivePresentation("Loading folder hierarchy...", string.Empty);
        _navigationState.ReplaceRoots([]);
        var requestedRoots = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requestedRoots.Length == 0)
        {
            StatusChanged?.Invoke("No folders selected.");
            return;
        }

        StatusChanged?.Invoke("Loading folder hierarchy...");
        try
        {
            var result = await _folderTreeService.LoadRootsAsync(requestedRoots, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _navigationState.ReplaceRoots(result.Roots);

            if (result.Errors.Count > 0)
            {
                StatusChanged?.Invoke($"Loaded {result.Roots.Count} root(s); {result.Errors.Count} folder(s) could not be accessed.");
                MessageBox.Show(string.Join(Environment.NewLine, result.Errors), "Some folders could not be loaded", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                StatusChanged?.Invoke($"Loaded {result.Roots.Count} folder root(s).");
            }

            if (result.Roots.FirstOrDefault() is { } firstRoot)
            {
                await ActivateFolderAsync(firstRoot);
            }
            else
            {
                ShowImageMessage("No accessible folders were loaded.", string.Empty);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer context replaced this load.
        }
    }

    private async void OnFolderSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderTreeEntry folder)
        {
            await ActivateFolderAsync(folder);
        }
    }

    private async Task ActivateFolderAsync(FolderTreeEntry folder)
    {
        if (ReferenceEquals(_navigationState.ActiveFolder, folder))
        {
            return;
        }

        _fileLoadCancellation?.Cancel();
        _fileLoadCancellation?.Dispose();
        _fileLoadCancellation = new CancellationTokenSource();
        var cancellationToken = _fileLoadCancellation.Token;
        _navigationState.SetActiveFolder(folder);
        ClearActivePresentation($"Loading files in {folder.Name}...", folder.Path);
        StatusChanged?.Invoke($"Loading files in {folder.Name}...");

        try
        {
            var result = await _fileListingService.GetFilesAsync(folder.Path, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_navigationState.ActiveFolder, folder))
            {
                return;
            }

            _navigationState.ReplaceFiles(result.Files);
            EmptyFilesNotice.Visibility = result.Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _ = RefreshCarouselAsync();
            _ = LoadCurrentImageAsync(clearCurrentImage: true);
            StatusChanged?.Invoke(result.Errors.Count == 0
                ? $"{result.Files.Count} file(s) in {folder.Name}."
                : $"Loaded {result.Files.Count} file(s); {result.Errors.Count} item(s) could not be read.");
            if (result.Errors.Count > 0)
            {
                var errorSample = result.Errors.Take(5).ToList();
                if (result.Errors.Count > errorSample.Count)
                {
                    errorSample.Add($"...and {result.Errors.Count - errorSample.Count} more.");
                }

                MessageBox.Show(string.Join(Environment.NewLine, errorSample), "Some files could not be read", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer folder selection replaced this listing.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (ReferenceEquals(_navigationState.ActiveFolder, folder))
            {
                _navigationState.ReplaceFiles([]);
                EmptyFilesNotice.Visibility = Visibility.Visible;
                StatusChanged?.Invoke($"Could not read files in {folder.Name}.");
                MessageBox.Show(exception.Message, "Folder could not be read", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OnCarouselSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_carouselOverlayVisible)
        {
            RestoreCarouselSelection();
            return;
        }

        if (CarouselList.SelectedItem is not CarouselThumbnailItem selectedItem || ReferenceEquals(selectedItem.File, _navigationState.CurrentFile))
        {
            return;
        }

        var immediatePreview = selectedItem.Thumbnail;
        _navigationState.SetCurrentFile(selectedItem.File);
        ResetBoundaryAttempts();
        _ = RefreshCarouselAsync();
        _ = LoadCurrentImageAsync(clearCurrentImage: true, immediatePreview: immediatePreview);
    }

    private void OnCarouselPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_carouselOverlayVisible || _carouselPanelAnimating)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        while (source is not null && !ReferenceEquals(source, CarouselList))
        {
            if (source is ListBoxItem { DataContext: CarouselThumbnailItem item })
            {
                e.Handled = true;
                if (!ReferenceEquals(item.File, _navigationState.CurrentFile))
                {
                    SelectFile(item.File);
                }

                return;
            }

            source = VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source);
        }
    }

    private void RestoreCarouselSelection()
    {
        var currentItem = CarouselItems.OfType<CarouselThumbnailItem>()
            .FirstOrDefault(item => ReferenceEquals(item.File, _navigationState.CurrentFile));
        if (currentItem is not null)
        {
            CarouselList.SelectedItem = currentItem;
        }
    }

    private void OnCarouselPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Delta == 0)
        {
            return;
        }

        _carouselWheelDeltaRemainder += e.Delta;
        var notches = _carouselWheelDeltaRemainder / 120;
        _carouselWheelDeltaRemainder %= 120;
        if (notches == 0)
        {
            return;
        }

        e.Handled = true;
        var direction = notches < 0 ? 1 : -1;
        for (var notch = 0; notch < Math.Abs(notches); notch++)
        {
            NavigateByOneFile(direction);
        }
    }

    private void OnBrowsePreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!ImageViewport.IsKeyboardFocusWithin && !CarouselList.IsKeyboardFocusWithin)
        {
            return;
        }

        if (IsSupportedCurrentImage && e.Key is System.Windows.Input.Key.Add or System.Windows.Input.Key.Subtract)
        {
            if (!_zoomInteractionEnabled)
            {
                _zoomInteractionEnabled = true;
            }

            e.Handled = true;
            ChangeZoom(e.Key == System.Windows.Input.Key.Add ? ZoomStepFactor : 1d / ZoomStepFactor);
            return;
        }

        var direction = e.Key switch
        {
            System.Windows.Input.Key.Left or System.Windows.Input.Key.Up => -1,
            System.Windows.Input.Key.Right or System.Windows.Input.Key.Down => 1,
            _ => 0
        };

        if (direction == 0)
        {
            return;
        }

        e.Handled = true;
        NavigateByOneFile(direction);
    }

    private void OnImageViewportMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ImageViewport.Focus();
        System.Windows.Input.Keyboard.Focus(ImageViewport);

        if (_zoomInteractionEnabled && IsSupportedCurrentImage && e.ChangedButton == System.Windows.Input.MouseButton.Left && _zoomLevel > 1d)
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(ImageViewport);
            ImageViewport.CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnBrowseSurfaceMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var point = e.GetPosition(BrowseSurface);
        var showFolder = point.X <= 26 || (_folderOverlayVisible && point.X <= FolderOverlay.Width);
        var showCarousel = point.Y >= BrowseSurface.ActualHeight - 26 ||
                           (_carouselOverlayVisible && point.Y >= BrowseSurface.ActualHeight - CarouselOverlay.Height);
        SetFolderOverlayVisible(showFolder);
        SetCarouselOverlayVisible(showCarousel);
    }

    private void OnBrowseSurfaceMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        SetFolderOverlayVisible(false);
        SetCarouselOverlayVisible(false);
    }

    private void OnBrowseSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCarouselSpacerWidths();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(CenterCurrentCarouselItem));
    }

    private void SetFolderOverlayVisible(bool isVisible)
    {
        if (_folderOverlayVisible == isVisible)
        {
            return;
        }

        _folderOverlayVisible = isVisible;
        FolderOverlay.IsHitTestVisible = isVisible;
        FolderOverlayTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            To = isVisible ? 0 : -FolderOverlay.Width,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void SetCarouselOverlayVisible(bool isVisible)
    {
        if (_carouselOverlayVisible == isVisible)
        {
            return;
        }

        _carouselOverlayVisible = isVisible;
        _carouselPanelAnimating = true;
        CarouselOverlay.IsHitTestVisible = false;
        CarouselList.IsHitTestVisible = false;
        var animation = new DoubleAnimation
        {
            To = isVisible ? 0 : CarouselOverlay.Height * 0.75,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) =>
        {
            if (_carouselOverlayVisible == isVisible)
            {
                _carouselPanelAnimating = false;
                CarouselOverlay.IsHitTestVisible = isVisible;
                CarouselList.IsHitTestVisible = isVisible;
            }
        };

        CarouselOverlayTransform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private void OnImageViewportMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isPanning || e.ChangedButton != System.Windows.Input.MouseButton.Left)
        {
            return;
        }

        _isPanning = false;
        ImageViewport.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnImageViewportMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        var point = e.GetPosition(ImageViewport);
        _panTransform.X += point.X - _lastPanPoint.X;
        _panTransform.Y += point.Y - _lastPanPoint.Y;
        _lastPanPoint = point;
        ClampPan();
        e.Handled = true;
    }

    private void OnImageViewportRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!IsSupportedCurrentImage || ImageViewer.Source is null)
        {
            return;
        }

        e.Handled = true;
        _zoomInteractionEnabled = !_zoomInteractionEnabled;
        if (!_zoomInteractionEnabled)
        {
            ResetZoomState();
            _ = LoadCurrentImageAsync(clearCurrentImage: false);
        }

    }

    private void OnImageViewportMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Delta == 0)
        {
            return;
        }

        if (_zoomInteractionEnabled && IsSupportedCurrentImage && ImageViewer.Source is not null)
        {
            _zoomWheelDeltaRemainder += e.Delta;
            var zoomNotches = _zoomWheelDeltaRemainder / 120;
            _zoomWheelDeltaRemainder %= 120;
            if (zoomNotches != 0)
            {
                e.Handled = true;
                var factor = zoomNotches > 0 ? ZoomStepFactor : 1d / ZoomStepFactor;
                var zoomOrigin = e.GetPosition(ImageViewport);
                for (var notch = 0; notch < Math.Abs(zoomNotches); notch++)
                {
                    ChangeZoom(factor, zoomOrigin);
                }
            }

            return;
        }

        _wheelDeltaRemainder += e.Delta;
        var notches = _wheelDeltaRemainder / 120;
        _wheelDeltaRemainder %= 120;
        if (notches == 0)
        {
            return;
        }

        e.Handled = true;
        var direction = notches < 0 ? 1 : -1;
        for (var notch = 0; notch < Math.Abs(notches); notch++)
        {
            NavigateByOneFile(direction);
        }
    }

    private void NavigateByOneFile(int direction)
    {
        if (_navigationState.Files.Count == 0)
        {
            RegisterBoundaryAttempt(direction);
            return;
        }

        var currentIndex = _navigationState.CurrentFile is null
            ? -1
            : _navigationState.Files.IndexOf(_navigationState.CurrentFile);
        var nextIndex = currentIndex + direction;
        if (nextIndex >= 0 && nextIndex < _navigationState.Files.Count)
        {
            ResetBoundaryAttempts();
            SelectFile(_navigationState.Files[nextIndex]);
            return;
        }

        RegisterBoundaryAttempt(direction);
    }

    private void RegisterBoundaryAttempt(int direction)
    {
        if (_overscrollDirection != direction)
        {
            _overscrollDirection = direction;
            _overscrollCount = 0;
        }

        _overscrollCount++;
        if (_overscrollCount < OverscrollAttemptsToChangeFolder || _changingFolderFromOverscroll)
        {
            return;
        }

        _overscrollCount = 0;
        _changingFolderFromOverscroll = true;
        _ = ActivateAdjacentFolderWithFilesAsync(direction);
    }

    private async Task ActivateAdjacentFolderWithFilesAsync(int direction)
    {
        try
        {
            var originalFolder = _navigationState.ActiveFolder;
            var folders = EnumerateFoldersInTreeOrder(_navigationState.Roots).ToArray();
            var currentIndex = Array.FindIndex(folders, folder => ReferenceEquals(folder, _navigationState.ActiveFolder));
            for (var index = currentIndex + direction; index >= 0 && index < folders.Length; index += direction)
            {
                var folder = folders[index];
                await ActivateFolderAsync(folder);
                folder.IsSelected = true;
                if (_navigationState.Files.Count > 0)
                {
                    ResetBoundaryAttempts();
                    return;
                }
            }

            if (originalFolder is not null)
            {
                await ActivateFolderAsync(originalFolder);
                originalFolder.IsSelected = true;
            }

            StatusChanged?.Invoke(direction > 0 ? "No files in later folders." : "No files in earlier folders.");
        }
        finally
        {
            _changingFolderFromOverscroll = false;
        }
    }

    private static IEnumerable<FolderTreeEntry> EnumerateFoldersInTreeOrder(IEnumerable<FolderTreeEntry> folders)
    {
        foreach (var folder in folders)
        {
            yield return folder;
            foreach (var child in EnumerateFoldersInTreeOrder(folder.Children))
            {
                yield return child;
            }
        }
    }

    private void SelectFile(FileEntry file)
    {
        var item = CarouselItems.OfType<CarouselThumbnailItem>().FirstOrDefault(candidate => ReferenceEquals(candidate.File, file));
        var immediatePreview = item?.Thumbnail;
        _navigationState.SetCurrentFile(file);
        ResetZoomState();
        ResetBoundaryAttempts();
        _ = RefreshCarouselAsync();
        _ = LoadCurrentImageAsync(clearCurrentImage: true, immediatePreview: immediatePreview);
    }

    private void ResetBoundaryAttempts()
    {
        _overscrollDirection = 0;
        _overscrollCount = 0;
    }

    private bool IsSupportedCurrentImage => _navigationState.CurrentFile is { } file && SupportedImageExtensions.Contains(file.Extension);

    private void ChangeZoom(double factor, Point? zoomOrigin = null)
    {
        var previousZoom = _zoomLevel;
        var nextZoom = Math.Clamp(previousZoom * factor, MinimumZoom, MaximumZoom);
        if (Math.Abs(nextZoom - previousZoom) < double.Epsilon)
        {
            return;
        }

        var origin = zoomOrigin ?? new Point(ImageViewport.ActualWidth / 2d, ImageViewport.ActualHeight / 2d);
        var center = new Point(ImageViewport.ActualWidth / 2d, ImageViewport.ActualHeight / 2d);
        var zoomRatio = nextZoom / previousZoom;
        _panTransform.X = origin.X - center.X - zoomRatio * (origin.X - center.X - _panTransform.X);
        _panTransform.Y = origin.Y - center.Y - zoomRatio * (origin.Y - center.Y - _panTransform.Y);
        _zoomLevel = nextZoom;
        _zoomTransform.ScaleX = _zoomLevel;
        _zoomTransform.ScaleY = _zoomLevel;
        if (_zoomLevel <= 1d)
        {
            _panTransform.X = 0;
            _panTransform.Y = 0;
        }

        ClampPan();
        ZoomLevelChanged?.Invoke(_zoomLevel);
        if (IsSupportedCurrentImage && ImageViewer.Source is not null)
        {
            _ = LoadCurrentImageAsync(clearCurrentImage: false);
        }
    }

    private void ClampPan()
    {
        if (ImageViewer.Source is not BitmapSource source || source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            _panTransform.X = 0;
            _panTransform.Y = 0;
            return;
        }

        var fitScale = Math.Min(ImageViewport.ActualWidth / source.PixelWidth, ImageViewport.ActualHeight / source.PixelHeight);
        var maxX = Math.Max(0, source.PixelWidth * fitScale * (_zoomLevel - 1d) / 2d);
        var maxY = Math.Max(0, source.PixelHeight * fitScale * (_zoomLevel - 1d) / 2d);
        _panTransform.X = Math.Clamp(_panTransform.X, -maxX, maxX);
        _panTransform.Y = Math.Clamp(_panTransform.Y, -maxY, maxY);
    }

    private void ResetZoomState()
    {
        _zoomInteractionEnabled = false;
        _zoomLevel = 1d;
        _wheelDeltaRemainder = 0;
        _zoomWheelDeltaRemainder = 0;
        _zoomTransform.ScaleX = 1d;
        _zoomTransform.ScaleY = 1d;
        _panTransform.X = 0;
        _panTransform.Y = 0;
        StopPanning();
        ZoomLevelChanged?.Invoke(_zoomLevel);
    }

    public void ResetZoomToFit()
    {
        if (!IsSupportedCurrentImage || ImageViewer.Source is null)
        {
            return;
        }

        _zoomLevel = 1d;
        _zoomTransform.ScaleX = 1d;
        _zoomTransform.ScaleY = 1d;
        _panTransform.X = 0;
        _panTransform.Y = 0;
        StopPanning();
        ZoomLevelChanged?.Invoke(_zoomLevel);
        _ = LoadCurrentImageAsync(clearCurrentImage: false);
    }

    public void ZoomInFromToolbar() => AdjustZoomFromToolbar(ZoomStepFactor);

    public void ZoomOutFromToolbar() => AdjustZoomFromToolbar(1d / ZoomStepFactor);

    public void SetCarouselNeighborCount(int count)
    {
        var boundedCount = Math.Clamp(count, 0, 12);
        if (_carouselNeighborsOnEachSide == boundedCount)
        {
            return;
        }

        _carouselNeighborsOnEachSide = boundedCount;
        _hasCenteredCarouselItem = false;
        _ = RefreshCarouselAsync();
    }

    private void AdjustZoomFromToolbar(double factor)
    {
        if (!IsSupportedCurrentImage || ImageViewer.Source is null)
        {
            return;
        }

        _zoomInteractionEnabled = true;
        ChangeZoom(factor);
    }

    private void StopPanning()
    {
        _isPanning = false;
        if (ImageViewport.IsMouseCaptured)
        {
            ImageViewport.ReleaseMouseCapture();
        }
    }

    private async Task RefreshCarouselAsync()
    {
        var refreshVersion = ++_carouselRefreshVersion;
        if (_carouselLoadCancellation is null || _carouselLoadCancellation.IsCancellationRequested)
        {
            _carouselLoadCancellation?.Dispose();
            _carouselLoadCancellation = new CancellationTokenSource();
        }

        var cancellationToken = _carouselLoadCancellation.Token;
        _carouselOffsetBeforeRefresh = FindVisualChild<ScrollViewer>(CarouselList)?.HorizontalOffset ?? 0;

        var files = _navigationState.Files.ToArray();
        var currentFile = _navigationState.CurrentFile;
        var currentIndex = currentFile is null ? -1 : _navigationState.Files.IndexOf(currentFile);
        if (currentIndex < 0)
        {
            _carouselLeadingSpacer = null;
            _carouselTrailingSpacer = null;
            return;
        }

        _carouselLeadingSpacer ??= new CarouselSpacerItem(0);
        _carouselTrailingSpacer ??= new CarouselSpacerItem(0);
        if (CarouselItems.Count == 0 || !ReferenceEquals(CarouselItems[0], _carouselLeadingSpacer))
        {
            CarouselItems.Insert(0, _carouselLeadingSpacer);
        }

        if (!CarouselItems.Contains(_carouselTrailingSpacer))
        {
            CarouselItems.Add(_carouselTrailingSpacer);
        }

        const int thumbnailSize = 150;
        var start = Math.Max(0, currentIndex - _carouselNeighborsOnEachSide);
        var end = Math.Min(files.Length - 1, currentIndex + _carouselNeighborsOnEachSide);
        _carouselStartIndex = start;
        _carouselEndIndex = end;
        _carouselFileCount = files.Length;
        _carouselCurrentIndex = currentIndex;
        var visibleItems = new List<CarouselThumbnailItem>(end - start + 1);
        var existingItems = new Dictionary<FileEntry, CarouselThumbnailItem>(ReferenceEqualityComparer.Instance);
        foreach (var existingItem in CarouselItems.OfType<CarouselThumbnailItem>())
        {
            existingItems[existingItem.File] = existingItem;
        }

        var desiredFiles = new HashSet<FileEntry>(ReferenceEqualityComparer.Instance);
        for (var index = start; index <= end; index++)
        {
            desiredFiles.Add(files[index]);
        }
        for (var index = CarouselItems.Count - 2; index >= 1; index--)
        {
            if (CarouselItems[index] is CarouselThumbnailItem item && !desiredFiles.Contains(item.File))
            {
                CarouselItems.RemoveAt(index);
            }
        }

        for (var index = start; index <= end; index++)
        {
            var file = files[index];
            var item = existingItems.TryGetValue(file, out var existingItem)
                ? existingItem
                : new CarouselThumbnailItem(file, index == currentIndex);
            item.SetCurrent(index == currentIndex);
            var destinationIndex = index - start + 1;
            var currentItemIndex = CarouselItems.IndexOf(item);
            if (currentItemIndex < 0)
            {
                CarouselItems.Insert(destinationIndex, item);
            }
            else if (currentItemIndex != destinationIndex)
            {
                CarouselItems.RemoveAt(currentItemIndex);
                CarouselItems.Insert(destinationIndex, item);
            }

            visibleItems.Add(item);
        }

        var trailingIndex = CarouselItems.IndexOf(_carouselTrailingSpacer);
        if (trailingIndex != CarouselItems.Count - 1)
        {
            CarouselItems.RemoveAt(trailingIndex);
            CarouselItems.Add(_carouselTrailingSpacer);
        }

        UpdateCarouselSpacerWidths();

        var currentItem = visibleItems[currentIndex - start];
        CarouselList.SelectedItem = currentItem;
        if (refreshVersion != _carouselRefreshVersion)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            if (refreshVersion != _carouselRefreshVersion)
            {
                return;
            }

            _carouselScrollViewer = FindVisualChild<ScrollViewer>(CarouselList);
            if (_carouselScrollViewer is not null)
            {
                _carouselScrollViewer.ScrollToHorizontalOffset(
                    Math.Clamp(_carouselOffsetBeforeRefresh, 0, _carouselScrollViewer.ScrollableWidth));
            }

            CenterCurrentCarouselItem();
        }, DispatcherPriority.Loaded);

        if (refreshVersion != _carouselRefreshVersion)
        {
            return;
        }

        var loads = visibleItems
            .Where(item => item.Thumbnail is null && (item.ThumbnailTask is null || item.ThumbnailTask.IsCompleted))
            .Select(item => item.ThumbnailTask = LoadCarouselThumbnailAsync(item, thumbnailSize, cancellationToken));

        await Task.WhenAll(loads);
    }

    private void UpdateCarouselSpacerWidths()
    {
        const double carouselItemWidth = 156;
        var sideSpace = Math.Max(0, (CarouselList.ActualWidth - carouselItemWidth) / 2);
        if (_carouselLeadingSpacer is not null)
        {
            _carouselLeadingSpacer.Width = sideSpace + _carouselStartIndex * carouselItemWidth;
        }

        if (_carouselTrailingSpacer is not null)
        {
            _carouselTrailingSpacer.Width = sideSpace + Math.Max(0, _carouselFileCount - 1 - _carouselEndIndex) * carouselItemWidth;
        }
    }

    private void CenterCurrentCarouselItem()
    {
        if (CarouselList.SelectedItem is not CarouselThumbnailItem selectedItem ||
            CarouselList.ItemContainerGenerator.ContainerFromItem(selectedItem) is not ListBoxItem container ||
            (_carouselScrollViewer ??= FindVisualChild<ScrollViewer>(CarouselList)) is not { } scrollViewer)
        {
            return;
        }

        const double carouselItemWidth = 156;
        var targetOffset = _carouselCurrentIndex * carouselItemWidth;
        targetOffset = Math.Clamp(targetOffset, 0, scrollViewer.ScrollableWidth);
        if (!_hasCenteredCarouselItem || Math.Abs(targetOffset - scrollViewer.HorizontalOffset) < 1)
        {
            _carouselScrollTimer.Stop();
            scrollViewer.ScrollToHorizontalOffset(targetOffset);
            _hasCenteredCarouselItem = true;
            return;
        }

        _carouselScrollTimer.Stop();
        _carouselScrollStartOffset = scrollViewer.HorizontalOffset;
        _carouselScrollTargetOffset = targetOffset;
        _carouselScrollStartedAt = DateTime.UtcNow;
        _carouselScrollTimer.Start();
    }

    private void OnCarouselScrollTimerTick(object? sender, EventArgs e)
    {
        if (_carouselScrollViewer is null)
        {
            _carouselScrollTimer.Stop();
            return;
        }

        var progress = Math.Clamp((DateTime.UtcNow - _carouselScrollStartedAt).TotalMilliseconds / 180d, 0d, 1d);
        var easedProgress = 1d - Math.Pow(1d - progress, 3d);
        var offset = _carouselScrollStartOffset + (_carouselScrollTargetOffset - _carouselScrollStartOffset) * easedProgress;
        _carouselScrollViewer.ScrollToHorizontalOffset(offset);
        if (progress >= 1d)
        {
            _carouselScrollTimer.Stop();
            _carouselScrollViewer.ScrollToHorizontalOffset(_carouselScrollTargetOffset);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private async Task<BitmapSource?> LoadCarouselThumbnailAsync(CarouselThumbnailItem item, int thumbnailSize,
        CancellationToken cancellationToken)
    {
        try
        {
            var pngBytes = await _thumbnailService.GetThumbnailPngAsync(item.File, thumbnailSize, thumbnailSize, cancellationToken);
            if (pngBytes is null || cancellationToken.IsCancellationRequested || !CarouselItems.Contains(item))
            {
                return null;
            }

            var bitmap = await Task.Run(() => DecodeCenterCroppedThumbnail(pngBytes), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!cancellationToken.IsCancellationRequested && CarouselItems.Contains(item))
            {
                item.Thumbnail = bitmap;
                return bitmap;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The active folder or carousel view was replaced.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException or
                                           InvalidDataException or FormatException or NotSupportedException)
        {
            // Keep the file-type placeholder when no thumbnail is available.
        }

        return null;
    }

    private void OnImageViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_navigationState.CurrentFile is not null && ImageViewer.Source is not null)
        {
            _resizeTimer.Stop();
            _resizeTimer.Start();
        }
    }

    private async void OnImageResizeTimerTick(object? sender, EventArgs e)
    {
        _resizeTimer.Stop();
        await LoadCurrentImageAsync(clearCurrentImage: false);
    }

    private async Task LoadCurrentImageAsync(bool clearCurrentImage, BitmapSource? immediatePreview = null)
    {
        _imageLoadCancellation?.Cancel();
        _imageLoadCancellation?.Dispose();
        _imageLoadCancellation = null;
        var file = _navigationState.CurrentFile;
        var carouselItem = file is null ? null : CarouselItems.OfType<CarouselThumbnailItem>().FirstOrDefault(item => ReferenceEquals(item.File, file));
        var previewTask = carouselItem?.ThumbnailTask;
        var fullImageLoaded = false;

        if (clearCurrentImage)
        {
            var loadedPreview = immediatePreview ?? carouselItem?.Thumbnail;
            if (file is not null && loadedPreview is not null)
            {
                ShowImagePreview(loadedPreview, file);
            }
            else
            {
                ImageViewer.Source = null;
                ImageViewer.Visibility = Visibility.Collapsed;
                ThumbnailFallbackBadge.Visibility = Visibility.Collapsed;
            }
        }

        if (file is null)
        {
            ShowImageMessage("No file selected.", string.Empty);
            return;
        }

        var cancellationSource = new CancellationTokenSource();
        _imageLoadCancellation = cancellationSource;
        var cancellationToken = cancellationSource.Token;
        if (ImageViewer.Source is null)
        {
            ShowImageMessage("Loading image...", file.Name);
        }

        if (clearCurrentImage && immediatePreview is null && carouselItem?.Thumbnail is null && previewTask is not null)
        {
            _ = ShowCarouselPreviewWhenReadyAsync(previewTask, file, cancellationToken, () => fullImageLoaded);
        }

        try
        {
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            cancellationToken.ThrowIfCancellationRequested();
            var dpi = VisualTreeHelper.GetDpi(ImageViewport);
            var decodeScale = Math.Max(1d, _zoomLevel);
            var maxPixelWidth = Math.Max(1, (int)Math.Ceiling(ImageViewport.ActualWidth * dpi.DpiScaleX * decodeScale));
            var maxPixelHeight = Math.Max(1, (int)Math.Ceiling(ImageViewport.ActualHeight * dpi.DpiScaleY * decodeScale));
            BitmapSource image;
            var isThumbnailFallback = !SupportedImageExtensions.Contains(file.Extension);
            if (isThumbnailFallback)
            {
                var thumbnail = await _thumbnailService.GetThumbnailPngAsync(file, maxPixelWidth, maxPixelHeight, cancellationToken);
                if (thumbnail is null)
                {
                    ShowImageMessage("No integrated viewer or thumbnail is available.", file.Name);
                    return;
                }

                image = await Task.Run(() => DecodePng(thumbnail), cancellationToken);
            }
            else
            {
                image = await _imageDecoder.LoadAsync(file.FullPath, maxPixelWidth, maxPixelHeight, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_navigationState.CurrentFile, file))
            {
                return;
            }

            fullImageLoaded = true;
            ImageViewer.Source = image;
            ClampPan();
            ImageViewer.Visibility = Visibility.Visible;
            ImageMessagePanel.Visibility = Visibility.Collapsed;
            ThumbnailFallbackBadge.Visibility = isThumbnailFallback ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer file selection or viewer size replaced this image load.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           System.Security.SecurityException or InvalidDataException or FormatException or
                                           NotSupportedException or COMException)
        {
            if (!ReferenceEquals(_navigationState.CurrentFile, file))
            {
                return;
            }

            ImageViewer.Source = null;
            ImageViewer.Visibility = Visibility.Collapsed;
            ThumbnailFallbackBadge.Visibility = Visibility.Collapsed;
            ShowImageMessage("This image could not be displayed.", exception.Message);
        }
    }

    private void ShowImageMessage(string message, string detail)
    {
        ImageViewer.Source = null;
        ImageViewer.Visibility = Visibility.Collapsed;
        ThumbnailFallbackBadge.Visibility = Visibility.Collapsed;
        ImageMessage.Text = message;
        ImageMessageDetail.Text = detail;
        ImageMessagePanel.Visibility = Visibility.Visible;
    }

    private async Task ShowCarouselPreviewWhenReadyAsync(Task<BitmapSource?> previewTask, FileEntry file,
        CancellationToken cancellationToken, Func<bool> isFullImageLoaded)
    {
        try
        {
            var preview = await previewTask;
            if (preview is null || cancellationToken.IsCancellationRequested || isFullImageLoaded() ||
                !ReferenceEquals(_navigationState.CurrentFile, file))
            {
                return;
            }

            ShowImagePreview(preview, file);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer file selection replaced this preview.
        }
    }

    private void ShowImagePreview(BitmapSource preview, FileEntry file)
    {
        ImageViewer.Source = preview;
        ClampPan();
        ImageViewer.Visibility = Visibility.Visible;
        ImageMessagePanel.Visibility = Visibility.Collapsed;
        ThumbnailFallbackBadge.Visibility = SupportedImageExtensions.Contains(file.Extension) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearActivePresentation(string message, string detail)
    {
        ResetZoomState();
        _carouselRefreshVersion++;
        _carouselScrollTimer.Stop();
        _hasCenteredCarouselItem = false;
        _carouselOffsetBeforeRefresh = 0;
        _carouselScrollViewer = null;
        _carouselStartIndex = 0;
        _carouselEndIndex = -1;
        _carouselFileCount = 0;
        _carouselCurrentIndex = 0;
        _imageLoadCancellation?.Cancel();
        _imageLoadCancellation?.Dispose();
        _imageLoadCancellation = null;
        _carouselLoadCancellation?.Cancel();
        _carouselLoadCancellation?.Dispose();
        _carouselLoadCancellation = null;
        _resizeTimer.Stop();
        ImageViewer.Source = null;
        ImageViewer.Visibility = Visibility.Collapsed;
        ThumbnailFallbackBadge.Visibility = Visibility.Collapsed;
        CarouselItems.Clear();
        _carouselLeadingSpacer = null;
        _carouselTrailingSpacer = null;
        CarouselList.SelectedItem = null;
        EmptyFilesNotice.Visibility = Visibility.Visible;
        ShowImageMessage(message, detail);
    }

    private static BitmapSource DecodePng(byte[] pngBytes)
    {
        using var stream = new MemoryStream(pngBytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static BitmapSource DecodeCenterCroppedThumbnail(byte[] pngBytes)
    {
        var source = DecodePng(pngBytes);
        var cropSize = Math.Min(source.PixelWidth, source.PixelHeight);
        var crop = new Int32Rect(
            (source.PixelWidth - cropSize) / 2,
            (source.PixelHeight - cropSize) / 2,
            cropSize,
            cropSize);
        var cropped = new CroppedBitmap(source, crop);
        cropped.Freeze();
        return cropped;
    }
}
