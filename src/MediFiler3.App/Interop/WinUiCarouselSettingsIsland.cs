using Microsoft.UI;
using Microsoft.UI.Content;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.Graphics;

namespace MediFiler3.Interop;

public sealed class WinUiCarouselSettingsIsland : HwndHost
{
    private DesktopWindowXamlSource? _xamlSource;
    private Slider? _slider;
    private int _neighborCount = 5;

    public event Action<int>? NeighborCountChanged;
    public int NeighborCount => _neighborCount;

    public void SetNeighborCount(int neighborCount)
    {
        _neighborCount = Math.Clamp(neighborCount, 0, 12);
        var totalIconCount = (_neighborCount * 2) + 1;
        if (_slider is not null && Math.Abs(_slider.Value - totalIconCount) > 0.01)
        {
            _slider.Value = totalIconCount;
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _xamlSource = new DesktopWindowXamlSource();
        _xamlSource.Initialize(Win32Interop.GetWindowIdFromWindow(hwndParent.Handle));
        _xamlSource.ShouldConstrainPopupsToWorkArea = true;

        _slider = new Slider
        {
            Minimum = 1,
            Maximum = 25,
            StepFrequency = 2,
            TickFrequency = 2,
            TickPlacement = TickPlacement.BottomRight,
            SnapsTo = SliderSnapsTo.StepValues,
            Value = (_neighborCount * 2) + 1,
            IsThumbToolTipEnabled = false,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            RequestedTheme = ElementTheme.Dark
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_slider, "Total carousel icons");
        ToolTipService.SetToolTip(_slider, new ToolTip { Content = "Total carousel icons" });
        _slider.ValueChanged += (_, args) =>
        {
            var totalIconCount = Math.Clamp((int)Math.Round(args.NewValue), 1, 25);
            _neighborCount = (totalIconCount - 1) / 2;
            NeighborCountChanged?.Invoke(_neighborCount);
        };

        var content = new Grid
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorHelper.FromArgb(0, 0, 0, 0))
        };
        content.Children.Add(_slider);
        _xamlSource.Content = content;

        var islandHandle = Win32Interop.GetWindowFromWindowId(_xamlSource.SiteBridge.WindowId);
        return new HandleRef(this, islandHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (_xamlSource is not null)
        {
            _xamlSource.Content = null;
            _xamlSource.Dispose();
            _xamlSource = null;
        }

        _slider = null;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arrangedSize = base.ArrangeOverride(finalSize);
        if (_xamlSource is not null)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var width = Math.Max(0, (int)Math.Round(arrangedSize.Width * dpi.DpiScaleX));
            var height = Math.Max(0, (int)Math.Round(arrangedSize.Height * dpi.DpiScaleY));
            _xamlSource.SiteBridge.MoveAndResize(new RectInt32(0, 0, width, height));
        }

        return arrangedSize;
    }

    protected override bool TabIntoCore(TraversalRequest request)
    {
        if (_xamlSource is null)
        {
            return false;
        }

        var reason = request.FocusNavigationDirection == FocusNavigationDirection.Last
            ? XamlSourceFocusNavigationReason.Last
            : XamlSourceFocusNavigationReason.First;
        return _xamlSource.NavigateFocus(new XamlSourceFocusNavigationRequest(reason)).WasFocusMoved;
    }
}
