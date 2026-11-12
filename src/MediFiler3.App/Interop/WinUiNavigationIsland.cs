using Microsoft.UI;
using Microsoft.UI.Content;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using Windows.Graphics;

namespace MediFiler3.Interop;

public sealed class WinUiNavigationIsland : HwndHost
{
    private DesktopWindowXamlSource? _xamlSource;
    public void RaiseSurface() => NativeSurfaceOrder.Raise(_xamlSource);
    private NavigationView? _navigationView;

    public event Action<string>? DestinationSelected;
    public event Action? RefreshRequested;

    public void SelectDestination(string destination)
    {
        if (_navigationView is null)
        {
            return;
        }

        _navigationView.SelectedItem = _navigationView.MenuItems
            .OfType<NavigationViewItem>()
            .Concat(_navigationView.FooterMenuItems.OfType<NavigationViewItem>())
            .FirstOrDefault(item => string.Equals(item.Tag as string, destination, StringComparison.Ordinal));
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _xamlSource = new DesktopWindowXamlSource();
        _xamlSource.Initialize(Win32Interop.GetWindowIdFromWindow(hwndParent.Handle));
        _xamlSource.ShouldConstrainPopupsToWorkArea = true;

        _navigationView = CreateNavigationView();
        _navigationView.AddHandler(Microsoft.UI.Xaml.UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.F5) return;
            args.Handled = true;
            Dispatcher.BeginInvoke(new Action(() => RefreshRequested?.Invoke()));
        }), true);
        _xamlSource.Content = _navigationView;

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

        _navigationView = null;
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
        var result = _xamlSource.NavigateFocus(new XamlSourceFocusNavigationRequest(reason));
        return result.WasFocusMoved;
    }

    private NavigationView CreateNavigationView()
    {
        var navigationView = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact,
            IsPaneOpen = false,
            OpenPaneLength = 232,
            CompactPaneLength = 56,
            IsPaneToggleButtonVisible = false,
            IsSettingsVisible = false,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            PaneTitle = "MediFiler3",
            RequestedTheme = ElementTheme.Dark,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorHelper.FromArgb(255, 0, 0, 0)),
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorHelper.FromArgb(255, 245, 245, 245))
        };

        navigationView.MenuItems.Add(new NavigationViewItem
        {
            Content = "Home",
            Icon = new SymbolIcon(Symbol.Home),
            Tag = "Home"
        });
        navigationView.MenuItems.Add(new NavigationViewItemSeparator());
        navigationView.MenuItems.Add(new NavigationViewItem
        {
            Content = "Browse",
            Icon = new SymbolIcon(Symbol.Folder),
            Tag = "Browse"
        });
        navigationView.MenuItems.Add(new NavigationViewItem
        {
            Content = "Gallery",
            Icon = new SymbolIcon(Symbol.Pictures),
            Tag = "Gallery"
        });
        navigationView.MenuItems.Add(new NavigationViewItem { Content = "Reader", Icon = new FontIcon { Glyph = "\uE736" }, Tag = "Reader" });
        navigationView.FooterMenuItems.Add(new NavigationViewItem
        {
            Content = "Settings",
            Icon = new SymbolIcon(Symbol.Setting),
            Tag = "Settings"
        });

        navigationView.SelectionChanged += OnSelectionChanged;
        navigationView.SelectedItem = navigationView.MenuItems[0];
        return navigationView;
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string destination })
        {
            DestinationSelected?.Invoke(destination);
        }
    }

}
