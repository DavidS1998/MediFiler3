using Microsoft.UI;
using Microsoft.UI.Windowing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.Graphics;

namespace MediFiler3;

public partial class MainWindow
{
    private AppWindow? _appWindow;
    private bool _titleBarClosed;
    private HwndSource? _titleBarSource;
    private (int Width, int Height, int Left, int Right, bool Shown)? _titleBarGeometry;

    private void InitializeAppTitleBar()
    {
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(new WindowInteropHelper(this).Handle));
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        _titleBarSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _titleBarSource?.AddHook(TitleBarWindowMessage);
        _appWindow.Changed += OnAppWindowChanged;
        SizeChanged += OnTitleBarSizeChanged;
        DpiChanged += OnTitleBarDpiChanged;
        Activated += OnTitleBarActivationChanged;
        Deactivated += OnTitleBarActivationChanged;
        Closed += (_, _) =>
        {
            _titleBarClosed = true;
            _titleBarSource?.RemoveHook(TitleBarWindowMessage);
            _titleBarSource = null;
            if (_appWindow is not null) _appWindow.Changed -= OnAppWindowChanged;
            _appWindow = null;
            SizeChanged -= OnTitleBarSizeChanged;
            DpiChanged -= OnTitleBarDpiChanged;
            Activated -= OnTitleBarActivationChanged;
            Deactivated -= OnTitleBarActivationChanged;
            TitleBarIsland.Dispose();
        };
        UpdateAppTitleBarLayout();
    }

    private nint TitleBarWindowMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        // WPF's own nonclient handler otherwise reports HTCLIENT over the extended area.
        // Keep all non-title content and the OS caption-button insets on their native route.
        if (message != 0x0084 || _titleBarClosed || TitleBarFrame.Visibility != Visibility.Visible) return 0;
        var screen = new Point((short)(lParam.ToInt64() & 0xffff), (short)((lParam.ToInt64() >> 16) & 0xffff));
        var point = TitleBarIsland.PointFromScreen(screen);
        if (point.X < 0 || point.Y < 0 || point.X >= TitleBarIsland.ActualWidth || point.Y >= TitleBarIsland.ActualHeight) return 0;
        handled = true;
        return 2; // HTCAPTION: native drag, double-click maximize and system context menu.
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_titleBarClosed && IsVisible && (args.DidSizeChange || args.DidPresenterChange))
            Dispatcher.BeginInvoke(new Action(UpdateAppTitleBarLayout));
    }
    private void OnTitleBarSizeChanged(object sender, SizeChangedEventArgs args) => UpdateAppTitleBarLayout();
    private void OnTitleBarDpiChanged(object sender, DpiChangedEventArgs args) => UpdateAppTitleBarLayout();
    private void OnTitleBarActivationChanged(object? sender, EventArgs args) => ApplyCaptionColors();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == TitleProperty && _sourceInitialized) ApplyCaptionColors();
    }

    private void UpdateAppTitleBarLayout()
    {
        if (_titleBarClosed || _appWindow is null || !AppWindowTitleBar.IsCustomizationSupported()) return;
        var bar = _appWindow.TitleBar;
        if (!bar.ExtendsContentIntoTitleBar) bar.ExtendsContentIntoTitleBar = true;
        var shown = !IsFullscreen || _fullscreenToolbarVisible;
        TitleBarFrame.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        var dpi = VisualTreeHelper.GetDpi(this);
        TitleBarFrame.Height = Math.Max(24, bar.Height / dpi.DpiScaleY);
        TitleBarIsland.Margin = new Thickness(bar.LeftInset / dpi.DpiScaleX, 0, bar.RightInset / dpi.DpiScaleX, 0);
        // Coordinates are physical client pixels; leave Windows' caption button insets untouched.
        var geometry = (_appWindow.ClientSize.Width, (int)Math.Round(TitleBarFrame.Height * dpi.DpiScaleY),
            bar.LeftInset, bar.RightInset, shown);
        if (_titleBarGeometry != geometry)
        {
            _titleBarGeometry = geometry;
            bar.SetDragRectangles(shown
                ? [new RectInt32(bar.LeftInset, 0, Math.Max(0, geometry.Item1 - bar.LeftInset - bar.RightInset), geometry.Item2)]
                : []);
        }
        ApplyCaptionColors();
    }

    private void ApplyCaptionColors()
    {
        if (_titleBarClosed || _appWindow is null || !AppWindowTitleBar.IsCustomizationSupported() ||
            TitleBarBackground is not SolidColorBrush background || TitleBarForeground is not SolidColorBrush foreground) return;
        var bg = ColorHelper.FromArgb(255, background.Color.R, background.Color.G, background.Color.B);
        var fg = ColorHelper.FromArgb(255, foreground.Color.R, foreground.Color.G, foreground.Color.B);
        var bar = _appWindow.TitleBar;
        bar.BackgroundColor = bg; bar.ForegroundColor = fg;
        bar.InactiveBackgroundColor = bg;
        var inactive = ColorHelper.FromArgb(255, (byte)((fg.R + bg.R) / 2),
            (byte)((fg.G + bg.G) / 2), (byte)((fg.B + bg.B) / 2));
        bar.InactiveForegroundColor = inactive;
        bar.ButtonBackgroundColor = bg; bar.ButtonForegroundColor = fg;
        bar.ButtonInactiveBackgroundColor = bg; bar.ButtonInactiveForegroundColor = inactive;
        bar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(255, (byte)((bg.R * 3 + fg.R) / 4),
            (byte)((bg.G * 3 + fg.G) / 4), (byte)((bg.B * 3 + fg.B) / 4));
        bar.ButtonHoverForegroundColor = fg;
        bar.ButtonPressedBackgroundColor = bar.ButtonHoverBackgroundColor;
        bar.ButtonPressedForegroundColor = fg;
        TitleBarIsland.SetPresentation(Title, bg, IsActive ? fg : inactive);
    }
}
