using MediFiler3.Application.Files;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Windows.Graphics;

namespace MediFiler3.Interop;

/// <summary>Caption content and search. AppWindow owns dragging and the system caption buttons.</summary>
public sealed partial class WinUiTitleBarIsland : HwndHost
{
    private DesktopWindowXamlSource? _source;
    private Grid? _root;
    private TextBlock? _title;
    private TextBox? _fileSearch;
    private System.Windows.Rect _searchBounds;
    private bool _searchVisible;
    private bool _updatingSearch;
    private string _searchText = "";
    public event Action<string>? FileSearchChanged;
    public event Action? FileSearchSubmitted;
    public event Action? FileSearchDismissed;
    public Func<System.Windows.Input.Key, System.Windows.Input.ModifierKeys, bool, bool>? ViewShortcutHandler { get; set; }

    public void SetFileSearchState(bool visible, string text)
    {
        _searchVisible = visible;
        _searchText = text;
        if (_fileSearch is not null && _fileSearch.Text != text)
        {
            _updatingSearch = true;
            try { _fileSearch.Text = text; }
            finally { _updatingSearch = false; }
        }
        ApplySearchBounds();
    }

    public string FileSearchText => _fileSearch?.Text ?? _searchText;
    public bool IsFileSearchFocused => _searchVisible && _fileSearch is { FocusState: not FocusState.Unfocused } &&
        Handle != 0 && (GetFocus() == Handle || IsChild(Handle, GetFocus()));

    public bool FocusFileSearch() =>
        _searchVisible && IsVisible && WinUiViewKeyboard.FocusControl(Handle, _fileSearch);

    public bool HandleFileSearchKey(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers, bool repeat)
    {
        if (!IsFileSearchFocused || modifiers != System.Windows.Input.ModifierKeys.None) return false;
        if (key != System.Windows.Input.Key.Enter && key != System.Windows.Input.Key.Escape) return false;
        if (!repeat)
        {
            if (key == System.Windows.Input.Key.Enter) FileSearchSubmitted?.Invoke();
            else Dispatcher.BeginInvoke(new Action(() => FileSearchDismissed?.Invoke()));
        }
        return true;
    }

    public bool ContainsFileControlPoint(System.Windows.Point point) =>
        _searchVisible && (_searchBounds.Contains(point) || _filterBounds.Contains(point));

    public void SetSearchBounds(System.Windows.Rect bounds)
    {
        if (_searchBounds == bounds) return;
        _searchBounds = bounds;
        ApplySearchBounds();
    }

    private void ApplySearchBounds()
    {
        if (_fileSearch is null || _title is null) return;
        var visible = _searchVisible && !_searchBounds.IsEmpty && _searchBounds.Width > 0;
        _fileSearch.Visibility = visible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        if (visible)
        {
            _fileSearch.Width = _searchBounds.Width;
            _fileSearch.Height = _searchBounds.Height;
            _fileSearch.Margin = new Thickness(_searchBounds.X, _searchBounds.Y, 0, 0);
        }
        _title.MaxWidth = visible ? Math.Max(0, _searchBounds.X - 28) : double.PositiveInfinity;
    }

    private string _text = "MediFiler3";
    private Windows.UI.Color _background = Colors.Black, _foreground = Colors.White;

    public void SetPresentation(string text, Windows.UI.Color background, Windows.UI.Color foreground)
    {
        _text = text; _background = background; _foreground = foreground;
        if (_root?.Background is SolidColorBrush fill && fill.Color != background) fill.Color = background;
        if (_title is null) return;
        if (_title.Text != text)
        {
            _title.Text = text;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_title, text);
        }
        if (_title.Foreground is SolidColorBrush ink && ink.Color != foreground) ink.Color = foreground;
    }

    private static readonly SolidColorBrush CaptionControlFill = new(ColorHelper.FromArgb(240, 30, 30, 30));
    private static readonly SolidColorBrush CaptionControlHover = new(ColorHelper.FromArgb(244, 43, 43, 43));
    private static readonly SolidColorBrush CaptionControlPressed = new(ColorHelper.FromArgb(248, 35, 35, 35));
    private static readonly SolidColorBrush CaptionControlDisabled = new(ColorHelper.FromArgb(234, 34, 34, 34));

    private static void ApplyCaptionBackground(Control control, string resourcePrefix, Windows.UI.Color? accent = null)
    {
        var fill = accent is { } color ? new SolidColorBrush(ColorHelper.FromArgb(240, color.R, color.G, color.B)) : CaptionControlFill;
        var hover = accent is { } hoverColor ? new SolidColorBrush(ColorHelper.FromArgb(244, hoverColor.R, hoverColor.G, hoverColor.B)) : CaptionControlHover;
        var pressed = accent is { } pressedColor ? new SolidColorBrush(ColorHelper.FromArgb(248, pressedColor.R, pressedColor.G, pressedColor.B)) : CaptionControlPressed;
        control.Background = fill;
        control.Resources[$"{resourcePrefix}Background"] = fill;
        control.Resources[$"{resourcePrefix}BackgroundPointerOver"] = hover;
        control.Resources[$"{resourcePrefix}BackgroundPressed"] = pressed;
        control.Resources[$"{resourcePrefix}BackgroundFocused"] = fill;
        control.Resources[$"{resourcePrefix}BackgroundDisabled"] = accent is null ? CaptionControlDisabled : fill;
    }

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        _source = new DesktopWindowXamlSource();
        _source.Initialize(Win32Interop.GetWindowIdFromWindow(parent.Handle));
        _source.ShouldConstrainPopupsToWorkArea = true;
        _root = new Grid { RequestedTheme = ElementTheme.Dark, Background = new SolidColorBrush(_background),
            BackgroundTransition = null };
        _title = new TextBlock { FontSize = 12, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left,
            Margin = new Thickness(12, 0, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = new SolidColorBrush(_foreground) };
        _root.Children.Add(_title);
        _fileSearch = new TextBox
        {
            Text = _searchText, PlaceholderText = "Search files", MaxLength = FileQuery.MaximumTextLength,
            MinHeight = 0, FontSize = 13, Padding = new Thickness(10, 5, 10, 5),
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left
        };
        ApplyCaptionBackground(_fileSearch, "TextControl");
        AutomationProperties.SetName(_fileSearch, "Search files (Ctrl+F)");
        _fileSearch.TextChanged += (_, _) =>
        {
            if (!_updatingSearch && _fileSearch is { } search) FileSearchChanged?.Invoke(search.Text);
        };
        _root.Children.Add(_fileSearch);
        _root.Children.Add(CreateFileFilterLayout());
        ApplySearchBounds();
        WinUiViewKeyboard.Attach(_root, (key, modifiers, repeat) =>
            ViewShortcutHandler?.Invoke(key, modifiers, repeat) ?? false);
        SetPresentation(_text, _background, _foreground);
        _source.Content = _root;
        return new HandleRef(this, Win32Interop.GetWindowFromWindowId(_source.SiteBridge.WindowId));
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        _source?.SiteBridge.MoveAndResize(new RectInt32(0, 0,
            Math.Max(0, (int)Math.Round(size.Width * dpi.DpiScaleX)),
            Math.Max(0, (int)Math.Round(size.Height * dpi.DpiScaleY))));
        return size;
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (_source is not null) { _source.Content = null; _source.Dispose(); _source = null; }
        _root = null; _title = null; _fileSearch = null;
        _fileSearchLayout = null; _fileFilterButton = null; _filterCountLabel = null; _recentFilterChips = null;
    }

    protected override nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0084)
        {
            var screen = new System.Windows.Point((short)(lParam.ToInt64() & 0xffff), (short)((lParam.ToInt64() >> 16) & 0xffff));
            handled = true;
            return PresentationSourceAvailable() && ContainsFileControlPoint(PointFromScreen(screen)) ? 1 : -1;
        }
        return base.WndProc(hwnd, message, wParam, lParam, ref handled);
    }

    private bool PresentationSourceAvailable() => System.Windows.PresentationSource.FromVisual(this) is not null;

    protected override bool TabIntoCore(System.Windows.Input.TraversalRequest request) => FocusFileSearch();

    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsChild(nint parent, nint child);
}
