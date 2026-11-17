using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MediFiler3;

public partial class MainWindow
{
    public bool IsFullscreen { get; private set; }
    private bool _fullscreenSidebarVisible, _fullscreenToolbarVisible;
    private Rect _beforeFullscreen;
    private WindowState _beforeFullscreenState;
    private WindowStyle _beforeFullscreenStyle;
    private ResizeMode _beforeFullscreenResize;
    private double _beforeFullscreenMinWidth, _beforeFullscreenMinHeight;
    private readonly DispatcherTimer _fullscreenTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(40) };

    private void InitializeFullscreen()
    {
        _fullscreenTimer.Tick += OnFullscreenTimer;
        ComponentDispatcher.ThreadPreprocessMessage += OnFullscreenMessage;
        _windowLayout.TransientDisplayChanged += FitFullscreenMonitor;
        Closed += (_, _) =>
        {
            _fullscreenTimer.Stop();
            ComponentDispatcher.ThreadPreprocessMessage -= OnFullscreenMessage;
            _windowLayout.TransientDisplayChanged -= FitFullscreenMonitor;
        };
    }

    private void OnFullscreenMessage(ref MSG message, ref bool handled)
    {
        if (handled) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (message.hwnd != handle && GetAncestor(message.hwnd, 2) != handle) return;
        if (message.message == 0x020A)
        {
            var screen = new Point((short)(message.lParam.ToInt64() & 0xffff), (short)((message.lParam.ToInt64() >> 16) & 0xffff));
            handled = _browseView.ScrollGalleryDragTreeWheel((short)((message.wParam.ToInt64() >> 16) & 0xffff), screen);
            return;
        }
        if (message.message != 0x0100) return;
        var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(message.wParam.ToInt32());
        var repeat = (message.lParam.ToInt64() & (1L << 30)) != 0;
        handled = TryHandleViewShortcut(key, System.Windows.Input.Keyboard.Modifiers, repeat);
    }

    public void ToggleFullscreen()
    {
        if (!IsFullscreen)
        {
            _beforeFullscreen = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            _beforeFullscreenState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            _beforeFullscreenStyle = WindowStyle; _beforeFullscreenResize = ResizeMode;
            _beforeFullscreenMinWidth = MinWidth; _beforeFullscreenMinHeight = MinHeight;
            _windowLayout.SetTransientPresentation(true);
            IsFullscreen = true;
            WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            MinWidth = 0; MinHeight = 0;
            ToolbarRow.Height = new GridLength(0);
            System.Windows.Controls.Grid.SetRowSpan(ToolbarFrame, 2);
            ToolbarFrame.Height = 48; ToolbarFrame.VerticalAlignment = VerticalAlignment.Top;
            System.Windows.Controls.Grid.SetColumnSpan(Sidebar, 2);
            Sidebar.Width = CompactSidebarWidth; Sidebar.HorizontalAlignment = HorizontalAlignment.Left;
            SetFullscreenChrome(false, false);
            _browseView.SetFullscreenCarousel(true);
            FitFullscreenMonitor();
            _fullscreenTimer.Start();
        }
        else
        {
            _fullscreenTimer.Stop();
            IsFullscreen = false;
            WindowState = WindowState.Normal; WindowStyle = _beforeFullscreenStyle; ResizeMode = _beforeFullscreenResize;
            MinWidth = _beforeFullscreenMinWidth; MinHeight = _beforeFullscreenMinHeight;
            Left = _beforeFullscreen.Left; Top = _beforeFullscreen.Top; Width = _beforeFullscreen.Width; Height = _beforeFullscreen.Height;
            WindowState = _beforeFullscreenState;
            ToolbarRow.Height = new GridLength(48);
            System.Windows.Controls.Grid.SetRowSpan(ToolbarFrame, 1);
            ToolbarFrame.Height = double.NaN; ToolbarFrame.VerticalAlignment = VerticalAlignment.Stretch; ToolbarFrame.Visibility = Visibility.Visible;
            System.Windows.Controls.Grid.SetColumnSpan(Sidebar, 1);
            Sidebar.Width = double.NaN; Sidebar.HorizontalAlignment = HorizontalAlignment.Stretch;
            ApplySidebarLayout();
            _browseView.SetFullscreenCarousel(false);
            _browseView.SetShellVideoExclusions([]);
            _windowLayout.SetTransientPresentation(false);
        }
        ToolbarIsland.SetFullscreen(IsFullscreen);
        UpdateAppTitleBarLayout();
    }

    private void FitFullscreenMonitor()
    {
        if (!IsFullscreen) return;
        var handle = new WindowInteropHelper(this).Handle;
        var info = new FullscreenMonitorInfo { Size = Marshal.SizeOf<FullscreenMonitorInfo>() };
        if (GetMonitorInfo(MonitorFromWindow(handle, 2), ref info))
            SetWindowPos(handle, 0, info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top, 0x0014);
    }

    private void OnFullscreenTimer(object? sender, EventArgs args)
    {
        if (!IsFullscreen || !IsVisible || WindowState == WindowState.Minimized || !GetCursorPos(out var cursor)) return;
        _browseView.UpdateShellCarouselHover(_browseView.IsVisible ? _browseView.PointFromScreen(new Point(cursor.X, cursor.Y)) : null);
        var point = PointFromScreen(new Point(cursor.X, cursor.Y));
        // Native caption coordinates are above the WPF client origin. Keep the reveal
        // alive over that nonclient area, including the title and system buttons.
        var handle = new WindowInteropHelper(this).Handle;
        var inside = GetFullscreenWindowRect(handle, out var bounds) && cursor.X >= bounds.Left && cursor.X < bounds.Right && cursor.Y >= bounds.Top && cursor.Y < bounds.Bottom;
        var popup = ToolbarIsland.HasOpenPopup;
        var folderHover = _browseView.IsVisible
            ? _browseView.FolderRevealBounds.Contains(_browseView.PointFromScreen(new Point(cursor.X, cursor.Y)))
            : point.X <= (_fullscreenSidebarVisible ? CompactSidebarWidth + 12 : 8);
        SetFullscreenChrome(popup || inside && point.Y <= (_fullscreenToolbarVisible ? 60 : 8),
            inside && folderHover);
    }

    private void SetFullscreenChrome(bool toolbar, bool sidebar)
    {
        if (IsFullscreen && _fullscreenToolbarVisible != toolbar)
        {
            WindowStyle = toolbar ? WindowStyle.SingleBorderWindow : WindowStyle.None;
            FitFullscreenMonitor();
        }
        _fullscreenToolbarVisible = toolbar;
        UpdateAppTitleBarLayout();
        _fullscreenSidebarVisible = sidebar;
        _browseView.SetFullscreenFolderReveal(IsFullscreen && sidebar && _browseView.IsVisible, IsSidebarVisible ? CompactSidebarWidth : 0);
        ToolbarFrame.Visibility = toolbar ? Visibility.Visible : Visibility.Collapsed;
        ApplySidebarLayout();
        if (toolbar) ToolbarIsland.RaiseSurface();
        if (sidebar && IsSidebarVisible) NavigationIsland.RaiseSurface();
        if (!IsFullscreen || !_browseView.IsVisible) return;
        var exclusions = new List<Rect>();
        if (toolbar) exclusions.Add(new Rect(0, 0, Math.Max(0, _browseView.ActualWidth), 48));
        if (sidebar && IsSidebarVisible) exclusions.Add(new Rect(0, 0, CompactSidebarWidth, Math.Max(0, _browseView.ActualHeight)));
        _browseView.SetShellVideoExclusions(exclusions);
    }

    [StructLayout(LayoutKind.Sequential)] private struct FullscreenPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct FullscreenRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct FullscreenMonitorInfo { public int Size; public FullscreenRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")] private static extern bool GetFullscreenWindowRect(nint window, out FullscreenRect bounds);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out FullscreenPoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(nint monitor, ref FullscreenMonitorInfo info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
