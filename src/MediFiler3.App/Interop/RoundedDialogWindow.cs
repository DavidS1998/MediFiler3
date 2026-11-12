using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MediFiler3.Interop;

/// <summary>Clip the outer HWND, including native child surfaces, to the dialog's rounded silhouette.</summary>
internal sealed class RoundedDialogWindow : IDisposable
{
    private readonly Window _window;
    private HwndSource? _source;
    private double _radius;
    private string? _key;
    public RoundedDialogWindow(Window window)
    {
        _window = window;
        window.SourceInitialized += OnInitialized; window.SizeChanged += OnSize; window.Closed += OnClosed;
    }
    public void SetRadius(double radius) { _radius = double.IsFinite(radius) ? Math.Max(0, radius) : 0; Update(); }
    private void OnInitialized(object? sender, EventArgs args)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
        _source?.AddHook(OnMessage); Update();
    }
    private void OnSize(object sender, SizeChangedEventArgs args) => Update();
    private nint OnMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x02E0) _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Update));
        return 0;
    }
    private void Update()
    {
        var window = new WindowInteropHelper(_window).Handle;
        if (window == 0 || !GetWindowRect(window, out var rect)) return;
        var dpi = VisualTreeHelper.GetDpi(_window);
        var width = rect.Right - rect.Left; var height = rect.Bottom - rect.Top;
        var x = Math.Min(Math.Min(width, height), (int)Math.Ceiling(_radius * dpi.DpiScaleX * 2));
        var y = Math.Min(Math.Min(width, height), (int)Math.Ceiling(_radius * dpi.DpiScaleY * 2));
        var key = $"{width},{height},{x},{y}"; if (_key == key) return;
        _key = key;
        if (x <= 0 || y <= 0) { SetWindowRgn(window, 0, true); return; }
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, x, y);
        if (region == 0) { _key = null; return; }
        if (SetWindowRgn(window, region, true) == 0) { DeleteObject(region); _key = null; }
    }
    private void OnClosed(object? sender, EventArgs args) => Dispose();
    public void Dispose()
    {
        _window.SourceInitialized -= OnInitialized; _window.SizeChanged -= OnSize; _window.Closed -= OnClosed;
        if (_source is { IsDisposed: false }) _source.RemoveHook(OnMessage);
        _source = null;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
}
