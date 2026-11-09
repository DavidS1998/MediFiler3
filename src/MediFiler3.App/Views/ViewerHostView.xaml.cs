using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediFiler3.Application.Files;
using MediFiler3.Application.Viewing;
using MediFiler3.Domain.Files;
using MediFiler3.Services;

namespace MediFiler3.Views;

public partial class ViewerHostView : UserControl
{
    private ViewerCoordinator<BitmapSource>? _coordinator;
    private FileEntry? _currentFile;
    private ViewerMetadata? _metadata;
    private long _requestVersion;
    private bool _active = true;
    private bool _zoomInteractionEnabled;
    private bool _isPanning;
    private Point _lastPanPoint;
    private double _zoomLevel = 1d;
    private int _wheelDeltaRemainder;
    private int _zoomWheelDeltaRemainder;
    private readonly ScaleTransform _zoomTransform = new(1d, 1d);
    private readonly TranslateTransform _panTransform = new();
    private readonly DispatcherTimer _resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private const double MinimumZoom = 0.2d;
    private const double MaximumZoom = 5d;
    private const double ZoomStepFactor = 1.25d;
    private bool IsSupportedCurrentImage => Capabilities.HasFlag(ViewerCapabilities.Zoom);
    public double CurrentZoomLevel => _zoomLevel;
    public bool CanZoom => Capabilities.HasFlag(ViewerCapabilities.Zoom) && ImageViewer.Source is not null;
    public ViewerCapabilities Capabilities => _active ? _coordinator?.GetCapabilities(_currentFile) ?? ViewerCapabilities.None
        : ViewerCapabilities.None;
    public ViewerInteraction Interaction => _zoomInteractionEnabled ? ViewerInteraction.Content : ViewerInteraction.Navigation;
    public ViewerMetadata? Metadata => _metadata ?? (_currentFile is { } file ? new(file.Size) : null);
    public ViewerMetadata GetMetadataFor(FileEntry file) => ReferenceEquals(file, _currentFile)
        ? Metadata ?? new(file.Size) : new(file.Size);
    public event Action<int>? NavigationRequested;
    public event Action<double>? ZoomLevelChanged;
    public event Action? PresentationChanged;

    public ViewerHostView()
    {
        InitializeComponent();
        ImageViewer.RenderTransform = new TransformGroup { Children = { _zoomTransform, _panTransform } };
        _resizeTimer.Tick += OnImageResizeTimerTick;
    }

    public void Initialize(WicImageDecoder decoder, IThumbnailService thumbnails)
    {
        _coordinator = new ViewerCoordinator<BitmapSource>([new ImageViewerHandler(decoder), new ThumbnailViewerHandler(thumbnails)]);
    }

    public bool HandleKey(KeyEventArgs e)
    {
        if (!IsSupportedCurrentImage || e.Key is not (Key.Add or Key.Subtract)) return false;
        _zoomInteractionEnabled = true;
        ChangeZoom(e.Key == Key.Add ? ZoomStepFactor : 1d / ZoomStepFactor);
        e.Handled = true;
        return true;
    }

    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        if (!active)
        {
            _requestVersion++;
            _coordinator?.Cancel();
            _resizeTimer.Stop();
            StopPanning();
            ImageViewer.Source = null;
        }
        else _ = LoadCurrentImageAsync(true);
        PresentationChanged?.Invoke();
    }

    public void Clear()
    {
        _metadata = null;
        _requestVersion++;
        _coordinator?.Cancel();
        _resizeTimer.Stop();
        ResetZoomState();
        ImageViewer.Source = null;
        ImageViewer.Visibility = Visibility.Collapsed;
        ThumbnailFallbackBadge.Visibility = Visibility.Collapsed;
    }

    public Task LoadAsync(FileEntry? file, bool clearCurrentImage, BitmapSource? immediatePreview = null)
    {
        if (!ReferenceEquals(file, _currentFile))
        {
            _metadata = null;
            ResetZoomState();
        }
        _currentFile = file;
        if (clearCurrentImage)
        {
            _coordinator?.Cancel();
            ImageViewer.Source = null;
        }
        PresentationChanged?.Invoke();
        return LoadCurrentImageAsync(clearCurrentImage, immediatePreview);
    }

    private async Task LoadCurrentImageAsync(bool clearCurrentImage, BitmapSource? immediatePreview = null)
    {
        var request = ++_requestVersion;
        var file = _currentFile;
        if (!_active) return;
        _coordinator?.Cancel();
        if (clearCurrentImage)
        {
            _coordinator?.Cancel();
            ImageViewer.Source = null;
            if (file is not null && immediatePreview is not null) ShowImagePreview(immediatePreview, file);
        }
        if (file is null) { ShowImageMessage("No file selected.", ""); return; }
        if (ImageViewer.Source is null) ShowImageMessage("Loading file...", file.Name);
        try
        {
            // Wait for the host's layout so the first decode uses the actual viewport bounds.
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            if (request != _requestVersion || !_active) return;
            var dpi = VisualTreeHelper.GetDpi(ImageViewport);
            var scale = Math.Max(1d, _zoomLevel);
            var width = Math.Max(1, (int)Math.Ceiling(ImageViewport.ActualWidth * dpi.DpiScaleX * scale));
            var height = Math.Max(1, (int)Math.Ceiling(ImageViewport.ActualHeight * dpi.DpiScaleY * scale));
            var result = await _coordinator!.LoadAsync(file, width, height);
            if (result is null || request != _requestVersion || !_active || !ReferenceEquals(file, _currentFile)) return;
            _metadata = result.Metadata;
            if (result.Presentation is null) { ShowImageMessage("No integrated viewer or thumbnail is available.", file.Name); return; }
            ImageViewer.Source = result.Presentation;
            ClampPan();
            ImageViewer.Visibility = Visibility.Visible;
            ImageMessagePanel.Visibility = Visibility.Collapsed;
            ThumbnailFallbackBadge.Visibility = result.IsThumbnailFallback ? Visibility.Visible : Visibility.Collapsed;
            PresentationChanged?.Invoke();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or InvalidDataException or FormatException or NotSupportedException or COMException)
        {
            if (ReferenceEquals(file, _currentFile) && _active)
                ShowImageMessage("This file could not be displayed.", exception.Message);
        }
    }

    public void ZoomInFromToolbar() => AdjustZoomFromToolbar(ZoomStepFactor);
    public void ZoomOutFromToolbar() => AdjustZoomFromToolbar(1d / ZoomStepFactor);

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

    }

    private void OnImageViewportMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Delta == 0)
        {
            return;
        }

        if (ViewerInputPolicy.Wheel(Capabilities, Interaction) == ViewerInputAction.Zoom && ImageViewer.Source is not null)
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
            NavigationRequested?.Invoke(direction);
        }
    }

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

    public void ResetZoomState()
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

    private void OnImageViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_currentFile is not null && ImageViewer.Source is not null)
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

    public void ShowImageMessage(string message, string detail)
    {
        ImageViewer.Source = null;
        ImageViewer.Visibility = Visibility.Collapsed;
        ThumbnailFallbackBadge.Visibility = Visibility.Collapsed;
        ImageMessage.Text = message;
        ImageMessageDetail.Text = detail;
        ImageMessagePanel.Visibility = Visibility.Visible;
        PresentationChanged?.Invoke();
    }

    private void ShowImagePreview(BitmapSource preview, FileEntry file)
    {
        ImageViewer.Source = preview;
        ClampPan();
        ImageViewer.Visibility = Visibility.Visible;
        ImageMessagePanel.Visibility = Visibility.Collapsed;
        ThumbnailFallbackBadge.Visibility = IsSupportedCurrentImage ? Visibility.Collapsed : Visibility.Visible;
    }
}
