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
    public ObservableCollection<CarouselThumbnailItem> CarouselItems { get; } = [];

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
                UpdateZoomInteractionBadge();
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

        UpdateZoomInteractionBadge();
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
                for (var notch = 0; notch < Math.Abs(zoomNotches); notch++)
                {
                    ChangeZoom(factor);
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
        var item = CarouselItems.FirstOrDefault(candidate => ReferenceEquals(candidate.File, file));
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

    private void ChangeZoom(double factor)
    {
        _zoomLevel = Math.Clamp(_zoomLevel * factor, MinimumZoom, MaximumZoom);
        _zoomTransform.ScaleX = _zoomLevel;
        _zoomTransform.ScaleY = _zoomLevel;
        if (_zoomLevel <= 1d)
        {
            _panTransform.X = 0;
            _panTransform.Y = 0;
        }

        ClampPan();
        UpdateZoomInteractionBadge();
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
        UpdateZoomInteractionBadge();
    }

    private void StopPanning()
    {
        _isPanning = false;
        if (ImageViewport.IsMouseCaptured)
        {
            ImageViewport.ReleaseMouseCapture();
        }
    }

    private void UpdateZoomInteractionBadge()
    {
        ZoomInteractionBadge.Visibility = _zoomInteractionEnabled ? Visibility.Visible : Visibility.Collapsed;
        ZoomInteractionText.Text = $"Zoom / pan · {_zoomLevel:P0} · right-click to navigate";
    }

    private async Task RefreshCarouselAsync()
    {
        _carouselLoadCancellation?.Cancel();
        _carouselLoadCancellation?.Dispose();
        _carouselLoadCancellation = new CancellationTokenSource();
        var cancellationToken = _carouselLoadCancellation.Token;
        CarouselItems.Clear();

        var files = _navigationState.Files.ToArray();
        var currentFile = _navigationState.CurrentFile;
        var currentIndex = currentFile is null ? -1 : _navigationState.Files.IndexOf(currentFile);
        if (currentIndex < 0)
        {
            return;
        }

        const int neighborsOnEachSide = 5;
        const int thumbnailSize = 150;
        var start = Math.Max(0, currentIndex - neighborsOnEachSide);
        var end = Math.Min(files.Length - 1, currentIndex + neighborsOnEachSide);
        var visibleItems = new List<CarouselThumbnailItem>(end - start + 1);
        for (var index = start; index <= end; index++)
        {
            var item = new CarouselThumbnailItem(files[index], index == currentIndex);
            visibleItems.Add(item);
            CarouselItems.Add(item);
        }

        var currentItem = visibleItems[currentIndex - start];
        CarouselList.SelectedItem = currentItem;
        CarouselList.ScrollIntoView(currentItem);

        var loads = visibleItems.Select(item =>
        {
            item.ThumbnailTask = LoadCarouselThumbnailAsync(item, currentFile, thumbnailSize, cancellationToken);
            return item.ThumbnailTask;
        });

        await Task.WhenAll(loads);
    }

    private async Task<BitmapSource?> LoadCarouselThumbnailAsync(CarouselThumbnailItem item, FileEntry? currentFile,
        int thumbnailSize, CancellationToken cancellationToken)
    {
        try
        {
            var pngBytes = await _thumbnailService.GetThumbnailPngAsync(item.File, thumbnailSize, thumbnailSize, cancellationToken);
            if (pngBytes is null || cancellationToken.IsCancellationRequested || !ReferenceEquals(_navigationState.CurrentFile, currentFile))
            {
                return null;
            }

            var bitmap = await Task.Run(() => DecodePng(pngBytes), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(_navigationState.CurrentFile, currentFile))
            {
                item.Thumbnail = bitmap;
                return bitmap;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A new current file replaced this carousel window.
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
        var carouselItem = file is null ? null : CarouselItems.FirstOrDefault(item => ReferenceEquals(item.File, file));
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
}
