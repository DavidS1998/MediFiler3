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
    private (int Width, int Height, int Left, int Right, bool Shown, int SearchLeft, int SearchWidth, int ControlsRight)? _titleBarGeometry;
    private bool _captionPresentationQueued;
    private (Windows.UI.Color Background, Windows.UI.Color Foreground)? _appliedCaptionPalette;

    private void InitializeAppTitleBar()
    {
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(new WindowInteropHelper(this).Handle));
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        _titleBarSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _titleBarSource?.AddHook(TitleBarWindowMessage);
        _appWindow.Changed += OnAppWindowChanged;
        SizeChanged += OnTitleBarSizeChanged;
        ToolbarIsland.PrimaryCommandCenterChanged += UpdateAppTitleBarLayout;
        StateChanged += OnTitleBarStateChanged;
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
            ToolbarIsland.PrimaryCommandCenterChanged -= UpdateAppTitleBarLayout;
            StateChanged -= OnTitleBarStateChanged;
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
        return TitleBarIsland.ContainsFileControlPoint(point) ? 1 : 2; // HTCLIENT for search and filters; HTCAPTION elsewhere.
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_titleBarClosed && IsVisible && (args.DidSizeChange || args.DidPresenterChange))
            Dispatcher.BeginInvoke(new Action(UpdateAppTitleBarLayout));
    }
    private void OnTitleBarSizeChanged(object sender, SizeChangedEventArgs args) => UpdateAppTitleBarLayout();
    private void OnTitleBarStateChanged(object? sender, EventArgs args)
    {
        if (WindowState == WindowState.Minimized) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            // A native state transition can reset caption regions without changing the measured row.
            _titleBarGeometry = null;
            UpdateAppTitleBarLayout();
        }));
    }

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
        var width = _appWindow.ClientSize.Width;
        var height = (int)Math.Round(TitleBarFrame.Height * dpi.DpiScaleY);
        var left = bar.LeftInset;
        var rightEdge = Math.Max(left, width - bar.RightInset);
        var searchWidth = shown && FileFiltersAvailable
            ? Math.Min((int)Math.Round(320 * dpi.DpiScaleX), Math.Max(0, rightEdge - left) / 2) : 0;
        var searchCenter = width / 2d;
        if (ToolbarIsland.PrimaryCommandCenter is { } commandCenter && ToolbarIsland.IsVisible)
            searchCenter = ToolbarIsland.TranslatePoint(new Point(commandCenter, 0), this).X * dpi.DpiScaleX;
        var searchLeft = Math.Clamp((int)Math.Round(searchCenter - searchWidth / 2d), left, rightEdge - searchWidth);
        var searchHeight = Math.Min(34, TitleBarFrame.Height - 4);
        TitleBarIsland.SetSearchBounds(searchWidth > 0
            ? new Rect((searchLeft - left) / dpi.DpiScaleX, (TitleBarFrame.Height - searchHeight) / 2,
                searchWidth / dpi.DpiScaleX, searchHeight)
            : Rect.Empty);

        var filterLeft = Math.Min(rightEdge, searchLeft + searchWidth + (int)Math.Round(8 * dpi.DpiScaleX));
        var filtersWidth = TitleBarIsland.SetFilterBounds((filterLeft - left) / dpi.DpiScaleX,
            Math.Max(0, (TitleBarFrame.Height - 34) / 2), (rightEdge - filterLeft) / dpi.DpiScaleX);
        var controlsRight = Math.Min(rightEdge, filterLeft + (int)Math.Round(filtersWidth * dpi.DpiScaleX));
        var geometry = (width, height, left, bar.RightInset, shown, searchLeft, searchWidth, controlsRight);
        if (_titleBarGeometry != geometry)
        {
            _titleBarGeometry = geometry;
            bar.SetDragRectangles(!shown ? [] : searchWidth > 0
                ? [new RectInt32(left, 0, searchLeft - left, height),
                   new RectInt32(controlsRight, 0, rightEdge - controlsRight, height)]
                : [new RectInt32(left, 0, rightEdge - left, height)]);
        }
        ApplyCaptionColors();
    }

    private void ApplyCaptionColors()
    {
        if (_titleBarClosed || _appWindow is null || _captionPresentationQueued) return;
        _captionPresentationQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            _captionPresentationQueued = false;
            FlushCaptionPresentation();
        }));
    }

    private void FlushCaptionPresentation()
    {
        if (_titleBarClosed || _appWindow is null || !AppWindowTitleBar.IsCustomizationSupported() ||
            TitleBarBackground is not SolidColorBrush background || TitleBarForeground is not SolidColorBrush foreground) return;
        var bg = ColorHelper.FromArgb(255, background.Color.R, background.Color.G, background.Color.B);
        var fg = ColorHelper.FromArgb(255, foreground.Color.R, foreground.Color.G, foreground.Color.B);
        var bar = _appWindow.TitleBar;
        var inactive = ColorHelper.FromArgb(255, (byte)((fg.R + bg.R) / 2),
            (byte)((fg.G + bg.G) / 2), (byte)((fg.B + bg.B) / 2));
        if (_appliedCaptionPalette != (bg, fg))
        {
            bar.BackgroundColor = bg; bar.ForegroundColor = fg;
            bar.InactiveBackgroundColor = bg; bar.InactiveForegroundColor = inactive;
            bar.ButtonBackgroundColor = bg; bar.ButtonForegroundColor = fg;
            bar.ButtonInactiveBackgroundColor = bg; bar.ButtonInactiveForegroundColor = inactive;
            bar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(255, (byte)((bg.R * 3 + fg.R) / 4),
                (byte)((bg.G * 3 + fg.G) / 4), (byte)((bg.B * 3 + fg.B) / 4));
            bar.ButtonHoverForegroundColor = fg;
            bar.ButtonPressedBackgroundColor = bar.ButtonHoverBackgroundColor;
            bar.ButtonPressedForegroundColor = fg;
            _appliedCaptionPalette = (bg, fg);
        }
        // Publish WPF, WinUI content and native button colors in the same render-priority update.
        if (TitleBarFrame.Background is not SolidColorBrush frame || frame.Color != background.Color)
            TitleBarFrame.Background = background;
        TitleBarIsland.SetPresentation(Title, bg, IsActive ? fg : inactive);
    }
}
