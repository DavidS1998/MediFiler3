using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Windows.Graphics;

namespace MediFiler3.Interop;

/// <summary>Passive title content; AppWindow owns dragging and the system caption buttons.</summary>
public sealed class WinUiTitleBarIsland : HwndHost
{
    private DesktopWindowXamlSource? _source;
    private Grid? _root;
    private TextBlock? _title;
    private string _text = "MediFiler3";
    private Windows.UI.Color _background = Colors.Black, _foreground = Colors.White;

    public void SetPresentation(string text, Windows.UI.Color background, Windows.UI.Color foreground)
    {
        _text = text; _background = background; _foreground = foreground;
        if (_root is not null) _root.Background = new SolidColorBrush(background);
        if (_title is null) return;
        _title.Text = text;
        _title.Foreground = new SolidColorBrush(foreground);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_title, text);
    }

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        _source = new DesktopWindowXamlSource();
        _source.Initialize(Win32Interop.GetWindowIdFromWindow(parent.Handle));
        _root = new Grid { RequestedTheme = ElementTheme.Dark };
        _title = new TextBlock { FontSize = 12, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        _root.Children.Add(_title);
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
        _root = null; _title = null;
    }

    protected override nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0084) { handled = true; return -1; } // HTTRANSPARENT: passive text uses parent caption input.
        return base.WndProc(hwnd, message, wParam, lParam, ref handled);
    }
}
