using Microsoft.UI.Content;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.Graphics;

namespace MediFiler3.Interop;

internal sealed class WinUiDialogIsland : HwndHost
{
    private DesktopWindowXamlSource? _xamlSource;

    public Grid? RootElement { get; private set; }
    public XamlRoot? XamlRoot => RootElement?.XamlRoot;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _xamlSource = new DesktopWindowXamlSource();
        _xamlSource.Initialize(Win32Interop.GetWindowIdFromWindow(hwndParent.Handle));
        _xamlSource.ShouldConstrainPopupsToWorkArea = true;
        RootElement = new Grid
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(255, 0, 0, 0)),
            RequestedTheme = ElementTheme.Dark
        };
        _xamlSource.Content = RootElement;
        return new HandleRef(this, Win32Interop.GetWindowFromWindowId(_xamlSource.SiteBridge.WindowId));
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (_xamlSource is not null)
        {
            _xamlSource.Content = null;
            _xamlSource.Dispose();
            _xamlSource = null;
        }

        RootElement = null;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        if (_xamlSource is not null)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _xamlSource.SiteBridge.MoveAndResize(new RectInt32(
                0,
                0,
                Math.Max(0, (int)Math.Round(arranged.Width * dpi.DpiScaleX)),
                Math.Max(0, (int)Math.Round(arranged.Height * dpi.DpiScaleY))));
        }

        return arranged;
    }
}
