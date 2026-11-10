using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Persistence;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;
using Xaml = Microsoft.UI.Xaml;

namespace MediFiler3.Interop;

public enum HomeAction { Open, Favorite, Locate, Remove }
public sealed record HomeFolderAction(string Path, HomeAction Action, bool Favorite = false);

/// <summary>Native Home cards only; persistence, availability and pickers stay outside this island.</summary>
public sealed class WinUiHomeIsland : HwndHost
{
    private DesktopWindowXamlSource? _source;
    private Grid? _root;
    private Grid? _cards;
    private StackPanel? _page;
    private StackPanel? _header;
    private readonly List<(RootFolderHistory Root, TextBlock Label)> _visitViews = [];
    private readonly System.Windows.Threading.DispatcherTimer _relativeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private InfoBar? _notice;
    private StackPanel? _commands;
    private readonly Dictionary<string, StackPanel> _sections = [];
    private readonly Dictionary<string, int> _limits = new() { ["Recent"] = 25, ["Frequent"] = 25, ["Favorites"] = 25 };
    private readonly Dictionary<string, RootFolderStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Path, TextBlock Status, StackPanel Actions)> _statusViews = [];
    private RootFolderLists _lists = new([], [], []);
    private bool _busy;
    private string? _message;
    private bool _error;
    private string? _focusPath;
    private string? _focusSection;
    private ToggleButton? _pendingFocus;
    public event Action? AddFoldersRequested;
    public event Action? RefreshRequested;
    public event Action<string[]>? FoldersDropped;
    public event Action<HomeFolderAction>? FolderActionRequested;

    public WinUiHomeIsland() => _relativeTimer.Tick += (_, _) => UpdateRelativeTimes();

    private void UpdateRelativeTimes()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (root, label) in _visitViews)
        {
            var text = $"{root.OpenCount} visit{(root.OpenCount == 1 ? "" : "s")} \u00B7 {RootVisitTimeFormatter.Relative(root.LastOpenedUtc, now)}";
            if (label.Text != text) label.Text = text;
        }
    }

    public void SetLists(RootFolderLists lists)
    {
        _lists = lists;
        if (_focusPath is not null)
        {
            var candidates = new[] { ("Recent", lists.Recent), ("Frequent", lists.Frequent), ("Favorites", lists.Favorites) };
            if (!candidates.Any(c => c.Item1 == _focusSection && c.Item2.Any(r => SamePath(r.Path, _focusPath))))
                _focusSection = candidates.FirstOrDefault(c => c.Item2.Any(r => SamePath(r.Path, _focusPath))).Item1;
        }
        RenderLists();
    }
    public void SetAvailability(RootFolderStatus status)
    {
        _statuses[status.Root.Path] = status;
        foreach (var row in _statusViews.Where(r => string.Equals(r.Path, status.Root.Path, StringComparison.OrdinalIgnoreCase)))
            UpdateStatus(row.Status, row.Actions, status);
    }
    public void SetBusy(bool busy)
    {
        _busy = busy;
        if (_cards is not null) _cards.IsHitTestVisible = !busy;
        foreach (var section in _sections.Values) SetEnabled(section, !busy);
        if (_commands is not null) SetEnabled(_commands, !busy);
        RestoreFocus();
    }
    public void SetMessage(string? message, bool error = false)
    {
        _message = message;
        _error = error;
        if (_notice is null) return;
        _notice.Message = message ?? "";
        _notice.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        _notice.Visibility = string.IsNullOrWhiteSpace(message) ? Xaml.Visibility.Collapsed : Xaml.Visibility.Visible;
        _notice.IsOpen = !string.IsNullOrWhiteSpace(message);
    }

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        _source = new DesktopWindowXamlSource();
        _source.Initialize(Win32Interop.GetWindowIdFromWindow(parent.Handle));
        _source.ShouldConstrainPopupsToWorkArea = true;
        _root = new Grid { Background = Brush(255, 11, 12, 14), RequestedTheme = Xaml.ElementTheme.Dark };
        _root.AllowDrop = true;
        _root.DragOver += (_, args) =>
        {
            args.AcceptedOperation = args.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)
                ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
                : Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            args.Handled = true;
        };
        _root.Drop += async (sender, args) =>
        {
            args.Handled = true;
            var deferral = args.GetDeferral();
            var source = _source;
            try
            {
                if (!args.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;
                var items = await args.DataView.GetStorageItemsAsync();
                var paths = items.Where(item => item.IsOfType(Windows.Storage.StorageItemTypes.Folder))
                    .Select(item => item.Path).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
                if (paths.Length > 0 && ReferenceEquals(source, _source))
                    _ = Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(source, _source)) FoldersDropped?.Invoke(paths); }));
            }
            catch (Exception exception) { if (ReferenceEquals(source, _source)) SetMessage($"Could not read dropped folders: {exception.Message}", true); }
            finally { deferral.Complete(); }
        };
        _root.RowDefinitions.Add(new RowDefinition { Height = Xaml.GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new Xaml.GridLength(1, Xaml.GridUnitType.Star) });
        var page = _page = new StackPanel { Spacing = 24, Margin = new Xaml.Thickness(32, 24, 32, 32),
            HorizontalAlignment = Xaml.HorizontalAlignment.Left };
        var header = _header = new StackPanel { Spacing = 8, Margin = new Xaml.Thickness(32, 32, 32, 0),
            HorizontalAlignment = Xaml.HorizontalAlignment.Left };
        header.Children.Add(new TextBlock { Text = "Home", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Open a root folder or return to your library.", Foreground = Brush(255, 163, 166, 174) });
        _commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _commands.Children.Add(Command("Add folders", "AddHomeFolders", () => AddFoldersRequested?.Invoke()));
        _commands.Children.Add(Command("Check folders", "RefreshHomeFolders", () => RefreshRequested?.Invoke()));
        header.Children.Add(_commands);
        _root.Children.Add(header);
        _notice = new InfoBar { IsClosable = true, Visibility = Xaml.Visibility.Collapsed };
        _notice.Closed += (_, _) => { if (_notice is { IsOpen: false }) { _notice.Visibility = Xaml.Visibility.Collapsed; _message = null; } };
        header.Children.Add(_notice);
        _cards = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        foreach (var (key, title) in new[] { ("Recent", "Recently opened"), ("Frequent", "Frequently opened"), ("Favorites", "Favorites") })
        {
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            var rows = new StackPanel { Spacing = 4 };
            _sections[key] = rows;
            content.Children.Add(rows);
            _cards.Children.Add(new Border { Background = Brush(15, 255, 255, 255), BorderBrush = Brush(20, 255, 255, 255),
                BorderThickness = new Xaml.Thickness(1), CornerRadius = new Xaml.CornerRadius(8),
                Padding = new Xaml.Thickness(16), VerticalAlignment = Xaml.VerticalAlignment.Top, Child = content });
        }
        page.Children.Add(_cards);
        var scroll = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = Xaml.HorizontalAlignment.Stretch };
        Grid.SetRow(scroll, 1);
        _root.Children.Add(scroll);
        _source.Content = _root;
        RenderLists();
        SetMessage(_message, _error);
        SetBusy(_busy);
        ArrangeCards(ActualWidth);
        _relativeTimer.Start();
        return new HandleRef(this, Win32Interop.GetWindowFromWindowId(_source.SiteBridge.WindowId));
    }

    private void RenderLists()
    {
        if (_sections.Count == 0) return;
        _statusViews.Clear();
        _visitViews.Clear();
        _pendingFocus = null;
        foreach (var (key, roots) in new[] { ("Recent", _lists.Recent), ("Frequent", _lists.Frequent), ("Favorites", _lists.Favorites) })
        {
            var section = _sections[key];
            section.Children.Clear();
            if (roots.Count == 0)
                section.Children.Add(new TextBlock { Text = key == "Favorites" ? "Star a folder to keep it here." : "No folders yet. Use Add folders to begin.",
                    TextWrapping = Xaml.TextWrapping.Wrap, Foreground = Brush(255, 163, 166, 174), Margin = new Xaml.Thickness(0, 4, 0, 8) });
            for (var index = 0; index < Math.Min(roots.Count, _limits[key]); index++)
                section.Children.Add(CreateRow(key, index, roots[index]));
            if (roots.Count > _limits[key])
                section.Children.Add(Command($"Show more ({roots.Count - _limits[key]} remaining)", "More" + key, () =>
                { _limits[key] += 25; RenderLists(); }));
        }
        UpdateRelativeTimes();
        SetBusy(_busy);
    }

    private Grid CreateRow(string section, int index, RootFolderHistory root)
    {
        var name = RootFolderPath.Name(root.Path);
        var row = new Grid { ColumnSpacing = 8, RowSpacing = 4 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new Xaml.GridLength(1, Xaml.GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = Xaml.GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = Xaml.GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = Xaml.GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = Xaml.GridLength.Auto });
        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(new TextBlock { Text = name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = Xaml.TextTrimming.CharacterEllipsis });
        var visited = new TextBlock { Name = "Visit" + section + index, FontSize = 12,
            TextTrimming = Xaml.TextTrimming.CharacterEllipsis, Foreground = Brush(255, 163, 166, 174) };
        _visitViews.Add((root, visited));
        details.Children.Add(visited);
        var status = new TextBlock { FontSize = 12, TextWrapping = Xaml.TextWrapping.Wrap,
            Foreground = Brush(255, 239, 190, 103) };
        details.Children.Add(status);
        var open = Command("", "Open" + section + index, () => Request(root.Path, HomeAction.Open));
        open.Content = details;
        open.Background = Brush(0, 0, 0, 0);
        open.BorderThickness = new Xaml.Thickness(0);
        open.Padding = new Xaml.Thickness(8, 4, 8, 4);
        open.HorizontalAlignment = Xaml.HorizontalAlignment.Stretch;
        open.HorizontalContentAlignment = Xaml.HorizontalAlignment.Stretch;
        Xaml.Automation.AutomationProperties.SetName(open, $"Open {name}, {root.Path}");
        ToolTipService.SetToolTip(open, $"{root.Path}\nLast visited: {root.LastOpenedUtc?.ToLocalTime().ToString("F") ?? "Never"}");
        row.Children.Add(open);
        var star = new ToggleButton { Name = "Star" + section + index, IsChecked = root.Favorite,
            Width = 32, Height = 32, MinWidth = 0, Padding = new Xaml.Thickness(0), VerticalAlignment = Xaml.VerticalAlignment.Top,
            Content = new FontIcon { Glyph = root.Favorite ? "\uE735" : "\uE734", FontSize = 16,
                Foreground = root.Favorite ? Brush(255, 255, 206, 89) : Brush(255, 231, 232, 234) } };
        Xaml.Automation.AutomationProperties.SetName(star, $"{(root.Favorite ? "Remove" : "Add")} {name} {(root.Favorite ? "from" : "to")} favorites");
        ToolTipService.SetToolTip(star, root.Favorite ? "Remove from favorites" : "Add to favorites");
        void Toggle(bool favorite)
        {
            _focusPath = root.Path; _focusSection = section;
            Dispatcher.BeginInvoke(new Action(() => Request(root.Path, HomeAction.Favorite, favorite)));
        }
        star.Checked += (_, _) => Toggle(true);
        star.Unchecked += (_, _) => Toggle(false);
        Grid.SetColumn(star, 1);
        row.Children.Add(star);
        if (_focusPath is not null && SamePath(_focusPath, root.Path) && _focusSection == section)
        {
            _pendingFocus = star;
            star.Loaded += (_, _) => RestoreFocus();
        }
        var menuButton = Command("…", "Menu" + section + index, () => { });
        menuButton.Width = 32; menuButton.Height = 32; menuButton.MinWidth = 0;
        menuButton.Padding = new Xaml.Thickness(0); menuButton.VerticalAlignment = Xaml.VerticalAlignment.Top;
        Xaml.Automation.AutomationProperties.SetName(menuButton, $"Manage {name}");
        var menu = new MenuFlyout();
        AddMenu(menu, "Locate folder…", () => Request(root.Path, HomeAction.Locate));
        AddMenu(menu, "Remove entry", () => Request(root.Path, HomeAction.Remove));
        menuButton.Flyout = menu;
        Grid.SetColumn(menuButton, 2);
        row.Children.Add(menuButton);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Xaml.Thickness(8, 0, 0, 4) };
        foreach (var (label, action) in new[] { ("Locate", HomeAction.Locate), ("Remove", HomeAction.Remove) })
            actions.Children.Add(Command(label, label + section + index, () => Request(root.Path, action)));
        Grid.SetRow(actions, 1); Grid.SetColumnSpan(actions, 3);
        row.Children.Add(actions);
        _statusViews.Add((root.Path, status, actions));
        UpdateStatus(status, actions, _statuses.GetValueOrDefault(root.Path));
        return row;
    }

    private static void UpdateStatus(TextBlock label, StackPanel actions, RootFolderStatus? status)
    {
        label.Text = status?.Availability switch
        {
            RootFolderAvailability.Available => "",
            RootFolderAvailability.Missing => "Folder missing or renamed",
            RootFolderAvailability.AccessDenied => "Access denied",
            RootFolderAvailability.TemporarilyUnavailable => "Temporarily unavailable",
            _ => "Checking availability…"
        };
        label.Visibility = string.IsNullOrEmpty(label.Text) ? Xaml.Visibility.Collapsed : Xaml.Visibility.Visible;
        actions.Visibility = status is not null && status.Availability != RootFolderAvailability.Available
            ? Xaml.Visibility.Visible : Xaml.Visibility.Collapsed;
    }
    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private void RestoreFocus()
    {
        if (!_busy && _pendingFocus is { IsLoaded: true } button && button.Focus(Xaml.FocusState.Programmatic))
        { _pendingFocus = null; _focusPath = null; _focusSection = null; }
    }
    private void Request(string path, HomeAction action, bool favorite = false) => FolderActionRequested?.Invoke(new(path, action, favorite));
    private Button Command(string text, string name, Action action)
    {
        var button = new Button { Content = text, Name = name };
        // Native callbacks must finish before an action can navigate away and destroy the island.
        button.Click += (_, _) => Dispatcher.BeginInvoke(new Action(action));
        return button;
    }
    private void AddMenu(MenuFlyout menu, string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => Dispatcher.BeginInvoke(new Action(action));
        menu.Items.Add(item);
    }
    private static void SetEnabled(Xaml.UIElement element, bool enabled)
    {
        if (element is Control control) control.IsEnabled = enabled;
        if (element is Panel panel) foreach (var child in panel.Children) SetEnabled(child, enabled);
    }
    private void ArrangeCards(double width)
    {
        if (_cards is null) return;
        // Bound native content to the actual viewport. MaxWidth + Stretch can recenter
        // the ScrollViewer content incorrectly when growing beyond its maximum width.
        var contentWidth = Math.Max(0, Math.Min(1600, width - 64));
        _page!.Width = contentWidth;
        _header!.Width = contentWidth;
        _cards.Width = contentWidth;
        var columns = width >= 1100 ? 3 : width >= 760 ? 2 : 1;
        if (_cards.ColumnDefinitions.Count != columns)
        {
            _cards.ColumnDefinitions.Clear(); _cards.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++) _cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new Xaml.GridLength(1, Xaml.GridUnitType.Star) });
            for (var i = 0; i < (3 + columns - 1) / columns; i++) _cards.RowDefinitions.Add(new RowDefinition { Height = Xaml.GridLength.Auto });
            for (var i = 0; i < 3; i++) { Grid.SetColumn((Xaml.FrameworkElement)_cards.Children[i], i % columns); Grid.SetRow((Xaml.FrameworkElement)_cards.Children[i], i / columns); }
        }
    }
    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        ArrangeCards(size.Width);
        if (_source is not null)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _source.SiteBridge.MoveAndResize(new RectInt32(0, 0, Math.Max(0, (int)Math.Round(size.Width * dpi.DpiScaleX)),
                Math.Max(0, (int)Math.Round(size.Height * dpi.DpiScaleY))));
        }
        return size;
    }
    protected override bool TabIntoCore(System.Windows.Input.TraversalRequest request) => _source?.NavigateFocus(
        new XamlSourceFocusNavigationRequest(request.FocusNavigationDirection == System.Windows.Input.FocusNavigationDirection.Last
            ? XamlSourceFocusNavigationReason.Last : XamlSourceFocusNavigationReason.First)).WasFocusMoved == true;
    protected override void DestroyWindowCore(HandleRef handle)
    {
        _relativeTimer.Stop();
        _visitViews.Clear();
        if (_source is not null) { _source.Content = null; _source.Dispose(); _source = null; }
        _root = null; _cards = null; _page = null; _header = null; _notice = null; _commands = null;
        _sections.Clear(); _statusViews.Clear();
    }
    private static Xaml.Media.SolidColorBrush Brush(byte alpha, byte red, byte green, byte blue) =>
        new(ColorHelper.FromArgb(alpha, red, green, blue));
}
