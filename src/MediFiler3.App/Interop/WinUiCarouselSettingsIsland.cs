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

public sealed partial class WinUiCarouselSettingsIsland : HwndHost
{
    public WinUiCarouselSettingsIsland()
    {
        Unloaded += (_, _) => DetachFolderRuleSource();
        Loaded += (_, _) => ResumeFolderRuleSource();
    }
    private DesktopWindowXamlSource? _xamlSource;
    private Slider? _slider;
    private int _neighborCount = 5;
    private ToggleSwitch? _carouselToggle;
    private ToggleSwitch? _folderDepthToggle;
    private bool _folderDepthShadingEnabled = true;
    public event Action<bool>? FolderDepthShadingChanged;
    public void SetFolderDepthShading(bool enabled)
    {
        _folderDepthShadingEnabled = enabled;
        if (_folderDepthToggle is not null) _folderDepthToggle.IsOn = enabled;
    }
    private Button? _clearBackups;
    private Button? _clearCache;
    private TextBlock? _backupStatus;
    private TextBlock? _cacheStatus;
    private bool _carouselEnabled = true;
    private bool _backupsBusy, _cacheBusy;
    private string _backupText = "", _cacheText = "";
    public event Action<bool>? CarouselEnabledChanged;
    public event Action? ClearBackupsRequested;
    public event Action? ClearCacheRequested;
    public void SetCarouselEnabled(bool enabled)
    {
        _carouselEnabled = enabled;
        if (_carouselToggle is not null) _carouselToggle.IsOn = enabled;
        if (_slider is not null) _slider.IsEnabled = enabled;
    }
    public void SetBackupState(bool busy, string? text = null)
    {
        _backupsBusy = busy;
        if (text is not null) _backupText = text;
        if (_clearBackups is not null) _clearBackups.IsEnabled = !busy;
        if (_backupStatus is not null) _backupStatus.Text = _backupText;
    }
    public void SetCacheState(bool busy, string? text = null)
    {
        _cacheBusy = busy;
        if (text is not null) _cacheText = text;
        if (_clearCache is not null) _clearCache.IsEnabled = !busy;
        if (_cacheStatus is not null) _cacheStatus.Text = _cacheText;
    }

    public event Action<int>? NeighborCountChanged;
    public int NeighborCount => _neighborCount;

    public void SetNeighborCount(int neighborCount)
    {
        _neighborCount = Math.Clamp(neighborCount, 0, 12);
        var sliderPosition = _neighborCount * 2;
        if (_slider is not null && Math.Abs(_slider.Value - sliderPosition) > 0.01)
        {
            _slider.Value = sliderPosition;
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _xamlSource = new DesktopWindowXamlSource();
        _xamlSource.Initialize(Win32Interop.GetWindowIdFromWindow(hwndParent.Handle));
        _xamlSource.ShouldConstrainPopupsToWorkArea = true;

        _slider = new Slider
        {
            // Use a zero origin so native ticks and step positions share the same lattice.
            Minimum = 0,
            Maximum = 24,
            StepFrequency = 2,
            TickFrequency = 2,
            TickPlacement = TickPlacement.BottomRight,
            SnapsTo = SliderSnapsTo.Ticks,
            Value = _neighborCount * 2,
            IsThumbToolTipEnabled = false,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            RequestedTheme = ElementTheme.Dark
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_slider, "Total carousel icons");
        ToolTipService.SetToolTip(_slider, new ToolTip { Content = "Total carousel icons" });
        _slider.ValueChanged += (_, args) =>
        {
            _neighborCount = Math.Clamp((int)Math.Round(args.NewValue / 2), 0, 12);
            NeighborCountChanged?.Invoke(_neighborCount);
        };

        var page = new StackPanel { Spacing = 16, MaxWidth = 960, HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            Margin = new Microsoft.UI.Xaml.Thickness(32) };
        page.Children.Add(new TextBlock { Text = "Settings", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var folderPage = SettingsCard(page, "Folder tree");
        _folderDepthToggle = new ToggleSwitch { Header = "Dim folder text by depth", IsOn = _folderDepthShadingEnabled };
        _folderDepthToggle.Toggled += (_, _) =>
        {
            if (_folderDepthShadingEnabled == _folderDepthToggle.IsOn) return;
            _folderDepthShadingEnabled = _folderDepthToggle.IsOn;
            FolderDepthShadingChanged?.Invoke(_folderDepthShadingEnabled);
        };
        folderPage.Children.Add(_folderDepthToggle);
        folderPage.Children.Add(Description("Roots and their immediate subfolders stay white. Deeper folders gradually become grey, stopping at depth 5."));
        AddFolderRuleSettings(folderPage);
        var carouselPage = SettingsCard(page, "Carousel");
        _carouselToggle = new ToggleSwitch { Header = "Show carousel", IsOn = _carouselEnabled };
        _carouselToggle.Toggled += (_, _) =>
        {
            _carouselEnabled = _carouselToggle.IsOn;
            _slider.IsEnabled = _carouselEnabled;
            CarouselEnabledChanged?.Invoke(_carouselEnabled);
        };
        carouselPage.Children.Add(_carouselToggle);
        carouselPage.Children.Add(Description("Nearby thumbnails shown on each side of the current file."));
        var sliderRow = new Grid { ColumnSpacing = 16 };
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star) });
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });
        _slider.IsEnabled = _carouselEnabled;
        sliderRow.Children.Add(_slider);
        var count = new TextBlock { Text = $"{_neighborCount * 2 + 1} icons", VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center };
        _slider.ValueChanged += (_, _) => count.Text = $"{_neighborCount * 2 + 1} icons";
        Grid.SetColumn(count, 1); sliderRow.Children.Add(count); carouselPage.Children.Add(sliderRow);
        AddReaderSettings(SettingsCard(page, "Continuous reader"));
        var systemPage = SettingsCard(page, "System");
        systemPage.Children.Add(Heading("Overwrite backups"));
        systemPage.Children.Add(Description("Replaced files are kept for Undo. Clearing backups permanently deletes those saved versions and removes Undo and Redo for the affected operations."));
        _clearBackups = new Button { Content = "Clear overwrite backups" };
        _clearBackups.Click += (_, _) =>
        {
            if (_backupsBusy) return;
            SetBackupState(true);
            Dispatcher.BeginInvoke(new Action(() => ClearBackupsRequested?.Invoke()));
        };
        systemPage.Children.Add(_clearBackups);
        _backupStatus = Description(_backupText); systemPage.Children.Add(_backupStatus);
        systemPage.Children.Add(Heading("Thumbnail cache"));
        systemPage.Children.Add(Description("Thumbnails are regenerated as needed. Idle cleanup targets 1 GiB and removes entries unused for 90 days."));
        _clearCache = new Button { Content = "Clear thumbnail cache" };
        _clearCache.Click += (_, _) =>
        {
            if (_cacheBusy) return;
            SetCacheState(true);
            Dispatcher.BeginInvoke(new Action(() => ClearCacheRequested?.Invoke()));
        };
        systemPage.Children.Add(_clearCache);
        _cacheStatus = Description(_cacheText); systemPage.Children.Add(_cacheStatus);
        SetBackupState(_backupsBusy); SetCacheState(_cacheBusy);
        var content = new Grid { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorHelper.FromArgb(255, 11, 12, 14)), RequestedTheme = ElementTheme.Dark };
        content.Children.Add(new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch });
        _xamlSource.Content = content;

        var islandHandle = Win32Interop.GetWindowFromWindowId(_xamlSource.SiteBridge.WindowId);
        return new HandleRef(this, islandHandle);
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 18,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Microsoft.UI.Xaml.Thickness(0, 12, 0, 0) };
    private static TextBlock Description(string text) => new() { Text = text, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorHelper.FromArgb(255, 163, 166, 174)) };

    private static StackPanel SettingsCard(StackPanel page, string title)
    {
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        page.Children.Add(new Border
        {
            Tag = title, Child = body, Padding = new Microsoft.UI.Xaml.Thickness(20), CornerRadius = new Microsoft.UI.Xaml.CornerRadius(10),
            BorderThickness = new Microsoft.UI.Xaml.Thickness(1),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorHelper.FromArgb(255, 48, 50, 56)),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorHelper.FromArgb(255, 22, 24, 28))
        });
        return body;
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        ReleaseFolderRuleControls();
        if (_xamlSource is not null)
        {
            _xamlSource.Content = null;
            _xamlSource.Dispose();
            _xamlSource = null;
        }

        _readerSpacing = null; _readerPreload = null; _readerIndicator = null;
        _folderDepthToggle = null;
        _slider = null; _carouselToggle = null; _clearBackups = null; _clearCache = null; _backupStatus = null; _cacheStatus = null;
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
