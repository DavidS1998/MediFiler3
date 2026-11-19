using MediFiler3.Application.Folders;
using System.Text.RegularExpressions;
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
    private string[] _quickFolders = [];
    private string? _activeQuickFolder;

    public void SetActiveQuickFolder(string? rootPath)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(_activeQuickFolder, rootPath)) return;
        _activeQuickFolder = rootPath;
        if (_quickFolderButtons is null) return;
        foreach (var button in _quickFolderButtons.Children.OfType<Button>())
            UpdateQuickFolderAccent(button);
    }

    private void UpdateQuickFolderAccent(Button button) =>
        WinUiAccentButton.SetSelected(button, StringComparer.OrdinalIgnoreCase.Equals(button.Tag as string, _activeQuickFolder));
    private Grid? _sidebarRoot;
    private ScrollViewer? _quickFolderScroll;
    private StackPanel? _quickFolderButtons;
    private NavigationViewItem? _readerItem;
    private NavigationViewItem? _settingsItem;
    private Microsoft.UI.Xaml.Controls.Primitives.ScrollBar? _quickFolderScrollBar;
    public event Action<string>? QuickFolderOpenRequested;
    public event Action<string>? QuickFolderMoveRequested;

    public void SetQuickFolders(IEnumerable<string> paths)
    {
        var next = paths.ToArray();
        if (_quickFolders.SequenceEqual(next, StringComparer.Ordinal)) return;
        _quickFolders = next;
        UpdateQuickFolders();
    }

    private void UpdateQuickFolders()
    {
        if (_quickFolderButtons is not { } buttons || _quickFolderScroll is not { } scroll) return;
        buttons.Children.Clear();
        foreach (var path in _quickFolders)
        {
            var name = RootFolderPath.Name(path);
            var initials = GetQuickFolderInitials(name);
            var button = new Button
            {
                Content = initials, Tag = path, FontSize = 11, Width = 40, Height = 32,
                Padding = new Microsoft.UI.Xaml.Thickness(2),
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
            };
            ToolTipService.SetToolTip(button, $"{name}\n{path}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{name}, {path}");
            button.Click += (_, _) => RequestQuickFolder(path, move: false);
            button.RightTapped += (_, args) =>
            {
                args.Handled = true;
                RequestQuickFolder(path, move: true);
            };
            UpdateQuickFolderAccent(button);
            buttons.Children.Add(button);
        }
        scroll.Visibility = _quickFolders.Length == 0 ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    }

    private static string GetQuickFolderInitials(string name)
    {
        var cleanedName = Regex.Replace(name, @"[^a-zA-Z0-9]+", "");
        var initials = string.Concat(Regex.Matches(cleanedName, @"[A-Z0-9]").Select(match => match.Value));
        if (initials.Length <= 1)
            initials = cleanedName[..Math.Min(cleanedName.Length, 3)];

        if (initials.Length == 2)
        {
            var lastInitialIndex = cleanedName.LastIndexOf(initials[1]);
            if (lastInitialIndex < cleanedName.Length - 1)
                initials += cleanedName[lastInitialIndex + 1];
        }

        return initials[..Math.Min(initials.Length, 3)];
    }

    private void RequestQuickFolder(string path, bool move) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_quickFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            if (move) QuickFolderMoveRequested?.Invoke(path);
            else QuickFolderOpenRequested?.Invoke(path);
        }
    }));

    private void PositionQuickFolderScroll(object? sender, object args)
    {
        if (_sidebarRoot is not { } root || _quickFolderScroll is not { } scroll ||
            _readerItem is not { IsLoaded: true, ActualHeight: > 0 } reader ||
            _settingsItem is not { IsLoaded: true, ActualHeight: > 0 } settings) return;
        var top = reader.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, reader.ActualHeight)).Y + 4;
        var bottom = settings.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()).Y - 4;
        var margin = new Microsoft.UI.Xaml.Thickness(0, Math.Max(0, top), 0, Math.Max(0, root.ActualHeight - bottom));
        if (scroll.Margin != margin) scroll.Margin = margin;
        if (_quickFolderScrollBar is null && FindVerticalScrollBar(scroll) is { } bar)
        {
            _quickFolderScrollBar = bar;
            bar.Width = 6;
            bar.MinWidth = 0;
            bar.Opacity = .55;
        }
    }

    private static Microsoft.UI.Xaml.Controls.Primitives.ScrollBar? FindVerticalScrollBar(Microsoft.UI.Xaml.DependencyObject node)
    {
        if (node is Microsoft.UI.Xaml.Controls.Primitives.ScrollBar { Orientation: Orientation.Vertical } bar) return bar;
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); index++)
            if (FindVerticalScrollBar(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, index)) is { } child) return child;
        return null;
    }

    public event Action<string>? DestinationSelected;
    public event Action? RefreshRequested;
    public Func<Key, ModifierKeys, bool, bool>? ViewShortcutHandler { get; set; }

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
        _sidebarRoot = new Grid { RequestedTheme = ElementTheme.Dark };
        _sidebarRoot.Children.Add(_navigationView);
        _quickFolderButtons = new StackPanel { Spacing = 3, Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 5, 0) };
        _quickFolderScroll = new ScrollViewer
        {
            Content = _quickFolderButtons, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_quickFolderScroll, "Quick folders");
        _sidebarRoot.Children.Add(_quickFolderScroll);
        _sidebarRoot.LayoutUpdated += PositionQuickFolderScroll;
        UpdateQuickFolders();
        WinUiViewKeyboard.Attach(_sidebarRoot, (key, modifiers, repeat) => ViewShortcutHandler?.Invoke(key, modifiers, repeat) ?? false);
        _sidebarRoot.AddHandler(Microsoft.UI.Xaml.UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.F5) return;
            args.Handled = true;
            Dispatcher.BeginInvoke(new Action(() => RefreshRequested?.Invoke()));
        }), true);
        _xamlSource.Content = _sidebarRoot;

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

        if (_sidebarRoot is not null) _sidebarRoot.LayoutUpdated -= PositionQuickFolderScroll;
        _navigationView = null;
        _sidebarRoot = null;
        _quickFolderScroll = null;
        _quickFolderButtons = null;
        _readerItem = null;
        _settingsItem = null;
        _quickFolderScrollBar = null;
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
        _readerItem = new NavigationViewItem { Content = "Reader", Icon = new FontIcon { Glyph = "\uE736" }, Tag = "Reader" };
        navigationView.MenuItems.Add(_readerItem);
        _settingsItem = new NavigationViewItem { Content = "Settings", Icon = new SymbolIcon(Symbol.Setting), Tag = "Settings" };
        navigationView.FooterMenuItems.Add(_settingsItem);

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
