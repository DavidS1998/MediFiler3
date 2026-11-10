using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MediFiler3.Application.Persistence;
using MediFiler3.Interop;

namespace MediFiler3.Services;

public sealed class WindowLayoutController : IDisposable
{
    private readonly Window _window;
    private readonly WindowLayoutService _service;
    private readonly IWindowPlacementPlatform _platform;
    private readonly double _minimumWidth, _minimumHeight;
    private readonly DispatcherTimer _captureTimer;
    private readonly WindowPlacement _startup;
    private HwndSource? _source;
    private WindowLayout? _lastCapture;
    private bool _ready, _applying, _maximized, _disposed, _reconcileQueued;
    private bool _inMoveSize, _pendingDisplayChange;
    private bool _transientPresentation;
    public event Action? TransientDisplayChanged;
    public void SetTransientPresentation(bool transient)
    {
        if (_transientPresentation == transient) return;
        if (transient) { _captureTimer.Stop(); CaptureChange(); }
        _transientPresentation = transient;
        if (!transient) ReconcileDisplays();
    }
    public event Action<Exception>? PlacementFailed;

    public WindowLayoutController(Window window, WindowLayoutService service, IWindowPlacementPlatform platform)
    {
        _window = window;
        _service = service;
        _platform = platform;
        _minimumWidth = Math.Max(1, window.MinWidth);
        _minimumHeight = Math.Max(1, window.MinHeight);
        _startup = WindowPlacementPolicy.Resolve(service.Current, platform.GetMonitors(), _minimumWidth, _minimumHeight);
        _maximized = _startup.Maximized;
        SetMinimums(_startup);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        var layout = _startup.ToLayout();
        window.Left = layout.Left;
        window.Top = layout.Top;
        window.Width = layout.Width;
        window.Height = layout.Height;
        // WPF rejects showing a maximized window with ShowActivated=false. Background
        // windows can maximize after Loaded; ordinary startup maximizes before Show.
        window.WindowState = layout.Maximized && window.ShowActivated ? WindowState.Maximized : WindowState.Normal;
        _captureTimer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        _captureTimer.Tick += OnCaptureTick;
        window.SourceInitialized += OnSourceInitialized;
        window.Loaded += OnLoaded;
        window.LocationChanged += OnGeometryChanged;
        window.SizeChanged += OnSizeChanged;
        window.StateChanged += OnStateChanged;
        window.Closing += OnClosing;
        window.Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
        _source?.AddHook(WindowMessage);
        TryPlace(() => _platform.Apply(_window, _startup, beforeFirstShow: true));
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_ready) return;
        if (_startup.Maximized && _window.WindowState != WindowState.Maximized) _window.WindowState = WindowState.Maximized;
        _ready = true;
        TryPlace(() =>
        {
            _lastCapture = Capture();
            if (!_service.LoadFailed) _service.Update(_lastCapture);
        });
    }

    private void OnGeometryChanged(object? sender, EventArgs args) => ScheduleCapture();
    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => ScheduleCapture();
    private void OnStateChanged(object? sender, EventArgs args)
    {
        if (_ready && !_applying && !_transientPresentation && _window.WindowState != WindowState.Minimized)
            _maximized = _window.WindowState == WindowState.Maximized;
        ScheduleCapture();
    }
    private void ScheduleCapture()
    {
        if (!_ready || _applying || _disposed || _transientPresentation) return;
        _captureTimer.Stop();
        _captureTimer.Start();
    }
    private void OnCaptureTick(object? sender, EventArgs args) { _captureTimer.Stop(); CaptureChange(); }
    private WindowLayout Capture() => _platform.Capture(_window, _maximized, _service.Current?.FolderTreeWidth ?? 300);
    private void CaptureChange()
    {
        if (!_ready || _disposed || _transientPresentation) return;
        TryPlace(() =>
        {
            var value = Capture();
            if (value == _lastCapture) return;
            _service.Update(value);
            _lastCapture = value;
        });
    }
    private void OnClosing(object? sender, CancelEventArgs args) { _captureTimer.Stop(); CaptureChange(); }
    private void OnClosed(object? sender, EventArgs args) => Dispose();

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0231) _inMoveSize = true; // WM_ENTERSIZEMOVE
        else if (message == 0x0232)
        {
            _inMoveSize = false;
            if (_pendingDisplayChange) { _pendingDisplayChange = false; QueueReconcile(); }
            else ScheduleCapture();
        }
        else if (message == 0x02E0)
        {
            // WPF handles the suggested DPI rectangle. Crossing monitors during a
            // drag must not force the window back into one work area mid-gesture.
            if (_inMoveSize) ScheduleCapture(); else QueueReconcile();
        }
        else if (message == 0x007E || message == 0x001A && wParam == 0x002F)
        {
            if (_inMoveSize) _pendingDisplayChange = true; else QueueReconcile();
        }
        return 0;
    }
    private void QueueReconcile()
    {
        if (_reconcileQueued || !_ready || _disposed) return;
        _reconcileQueued = true;
        _window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _reconcileQueued = false;
            if (_inMoveSize) _pendingDisplayChange = true;
            else if (!_disposed) ReconcileDisplays();
        }));
    }

    public void ReconcileDisplays()
    {
        if (!_ready || _disposed) return;
        if (_transientPresentation) { TransientDisplayChanged?.Invoke(); return; }
        _captureTimer.Stop();
        TryPlace(() =>
        {
            var current = Capture();
            var next = WindowPlacementPolicy.Resolve(current, _platform.GetMonitors(), _minimumWidth, _minimumHeight);
            _applying = true;
            try
            {
                SetMinimums(next);
                if (next.ToLayout() != current) _platform.Apply(_window, next, beforeFirstShow: false);
            }
            finally { _applying = false; }
            CaptureChange();
        });
    }

    private void SetMinimums(WindowPlacement value)
    {
        _window.MinWidth = Math.Min(_minimumWidth, value.Monitor.LogicalWidth);
        _window.MinHeight = Math.Min(_minimumHeight, value.Monitor.LogicalHeight);
    }
    private void TryPlace(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is Win32Exception or ArgumentException or InvalidOperationException)
        { PlacementFailed?.Invoke(exception); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _captureTimer.Stop();
        _captureTimer.Tick -= OnCaptureTick;
        if (_source is { IsDisposed: false }) _source.RemoveHook(WindowMessage);
        _window.SourceInitialized -= OnSourceInitialized;
        _window.Loaded -= OnLoaded;
        _window.LocationChanged -= OnGeometryChanged;
        _window.SizeChanged -= OnSizeChanged;
        _window.StateChanged -= OnStateChanged;
        _window.Closing -= OnClosing;
        _window.Closed -= OnClosed;
    }
}
