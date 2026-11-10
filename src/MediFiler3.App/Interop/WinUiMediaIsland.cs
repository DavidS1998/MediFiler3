using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.UI;
using Microsoft.UI.Content;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;
using MediaPlayer = Windows.Media.Playback.MediaPlayer;
using Xaml = Microsoft.UI.Xaml;

namespace MediFiler3.Interop;

public sealed class WinUiMediaIsland(MediaPlayer player) : HwndHost
{
    private DesktopWindowXamlSource? _source;
    private MediaPlayerElement? _element;
    private nint _handle;
    private Rect[] _exclusions = [];
    private string? _regionKey;
    public event Action<int>? WheelRequested;
    public event Action? ToggleRequested;
    public event Action<int, bool, bool>? KeyRequested;

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        _source = new DesktopWindowXamlSource();
        _source.Initialize(Win32Interop.GetWindowIdFromWindow(parent.Handle));
        _source.ShouldConstrainPopupsToWorkArea = true;
        _element = new MediaPlayerElement { AreTransportControlsEnabled = false, AutoPlay = false,
            Stretch = Xaml.Media.Stretch.Uniform };
        _element.SetMediaPlayer(player);
        Xaml.Automation.AutomationProperties.SetName(_element, "Media viewer");
        var root = new Grid { Background = new Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black), IsTabStop = true };
        root.Children.Add(_element);
        root.AddHandler(Xaml.UIElement.PointerWheelChangedEvent, new Xaml.Input.PointerEventHandler((_, args) =>
        {
            var properties = args.GetCurrentPoint(root).Properties;
            if (properties.IsHorizontalMouseWheel) return;
            args.Handled = true;
            var delta = properties.MouseWheelDelta;
            Forward(() => WheelRequested?.Invoke(delta));
        }), true);
        root.AddHandler(Xaml.UIElement.RightTappedEvent, new Xaml.Input.RightTappedEventHandler((_, args) =>
        { args.Handled = true; Forward(() => ToggleRequested?.Invoke()); }), true);
        root.PointerPressed += (_, _) => root.Focus(Xaml.FocusState.Pointer);
        root.AddHandler(Xaml.UIElement.KeyDownEvent, new Xaml.Input.KeyEventHandler((_, args) =>
        {
            var key = (int)args.Key;
            if (key is not (13 or 32 or 37 or 38 or 39 or 40)) return;
            var control = IsDown(Windows.System.VirtualKey.Control);
            var shift = IsDown(Windows.System.VirtualKey.Shift);
            args.Handled = true;
            Forward(() => KeyRequested?.Invoke(key, control, shift));
        }), true);
        _source.Content = root;
        _handle = Win32Interop.GetWindowFromWindowId(_source.SiteBridge.WindowId);
        return new HandleRef(this, _handle);
    }
    private static bool IsDown(Windows.System.VirtualKey key) =>
        (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    private void Forward(Action action) => Dispatcher.BeginInvoke(new Action(() => { if (_source is not null) action(); }));

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        if (_source is not null)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _source.SiteBridge.MoveAndResize(new RectInt32(0, 0, Math.Max(0, (int)Math.Round(result.Width * dpi.DpiScaleX)),
                Math.Max(0, (int)Math.Round(result.Height * dpi.DpiScaleY))));
            UpdateRegion();
        }
        return result;
    }
    public void SetExclusions(IEnumerable<Rect> exclusions) { _exclusions = exclusions.ToArray(); UpdateRegion(); }
    private void UpdateRegion()
    {
        if (_handle == 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(0, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(0, (int)Math.Round(ActualHeight * dpi.DpiScaleY));
        var rectangles = _exclusions.Select(rect => (L: (int)Math.Floor(rect.Left * dpi.DpiScaleX),
            T: (int)Math.Floor(rect.Top * dpi.DpiScaleY), R: (int)Math.Ceiling(rect.Right * dpi.DpiScaleX),
            B: (int)Math.Ceiling(rect.Bottom * dpi.DpiScaleY))).ToArray();
        var key = $"{width},{height};" + string.Join(";", rectangles);
        if (key == _regionKey) return;
        var region = CreateRectRgn(0, 0, width, height);
        if (region == 0) return;
        try
        {
            foreach (var rect in rectangles)
            {
                var cutout = CreateRectRgn(rect.L, rect.T, rect.R, rect.B);
                if (cutout == 0) continue;
                try { CombineRgn(region, region, cutout, 4); } // RGN_DIFF leaves WPF overlays/input strips visible.
                finally { DeleteObject(cutout); }
            }
            if (SetWindowRgn(_handle, region, true) != 0) { region = 0; _regionKey = key; } // Windows owns the region.
        }
        finally { if (region != 0) DeleteObject(region); }
    }
    public void DetachPlayer() => _element?.SetMediaPlayer(null);
    protected override void DestroyWindowCore(HandleRef handle)
    {
        DetachPlayer(); _element = null;
        if (_source is not null) { _source.Content = null; _source.Dispose(); _source = null; }
        _handle = 0; _regionKey = null;
    }
    protected override bool TabIntoCore(System.Windows.Input.TraversalRequest request) => _source?.NavigateFocus(
        new XamlSourceFocusNavigationRequest(XamlSourceFocusNavigationReason.First)).WasFocusMoved == true;
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint destination, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
}
