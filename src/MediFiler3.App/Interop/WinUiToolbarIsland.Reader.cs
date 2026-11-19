using MediFiler3.Application.Navigation;
using Microsoft.UI.Xaml.Controls;
using Xml = System.Xml.Linq;
using Xaml = Microsoft.UI.Xaml;

#if EXPERIMENTAL_READER_PAGER
#pragma warning disable CS8305
#endif

namespace MediFiler3.Interop;

public sealed partial class WinUiToolbarIsland
{
    private Xaml.Controls.StackPanel? _readerControls, _readerWidthControls;
    private AppBarToggleButton? _readerWidthButton, _readerHeightButton;
    private Slider? _readerWidthSlider;
    private TextBlock? _readerWidthLabel;
    private ReaderOptions _readerToolbarOptions = new();
    private bool _readerControlsVisible, _settingReaderControls;
    public event Action<ReaderOptions>? ReaderOptionsChanged;
    public event Action? ReaderOpenBrowseRequested;
    private AppBarButton? _readerBrowse;
#if EXPERIMENTAL_READER_PAGER
    private PagerControl? _readerPager;
#else
    private NumberBox? _readerPageInput;
    private TextBlock? _readerPageTotal;
    private Button? _readerFirstPage, _readerPreviousPage, _readerNextPage, _readerLastPage;
#endif
    private AppBarElementContainer? _readerPagerContainer;
    private bool _settingReaderPager;
    public event Action<int>? ReaderPageRequested;
    private int _readerPageIndex = -1, _readerPageCount;
    private readonly List<AppBarSeparator> _readerFileSeparators = [];
    private AppBarSeparator? _readerNavigationDivider;
    public void SetReaderNavigation(int index, int count)
    {
        _readerPageIndex = index; _readerPageCount = count;
        if (_readerPagerContainer is not null) _readerPagerContainer.Visibility = _readerControlsVisible && count > 0
            ? Xaml.Visibility.Visible : Xaml.Visibility.Collapsed;
        if (_readerBrowse is not null) _readerBrowse.IsEnabled = index >= 0 && index < count;
        _settingReaderPager = true;
        try
        {
            var page = Math.Clamp(index, 0, Math.Max(0, count - 1));
#if EXPERIMENTAL_READER_PAGER
            if (_readerPager is not null)
            {
                _readerPager.IsEnabled = count > 0;
                _readerPager.NumberOfPages = Math.Max(1, count);
                _readerPager.SelectedPageIndex = page;
            }
#else
            if (_readerPageInput is not null)
            {
                _readerPageInput.IsEnabled = count > 0;
                _readerPageInput.Maximum = Math.Max(1, count);
                _readerPageInput.Value = page + 1;
                _readerPageTotal!.Text = $"of {count}";
                _readerFirstPage!.IsEnabled = _readerPreviousPage!.IsEnabled = page > 0;
                _readerNextPage!.IsEnabled = _readerLastPage!.IsEnabled = page + 1 < count;
            }
#endif
        }
        finally { _settingReaderPager = false; }
    }

    public void SetReaderControls(bool visible, ReaderOptions options)
    {
        options.Validate(); _readerControlsVisible = visible; _readerToolbarOptions = options;
        _settingReaderControls = true;
        try
        {
            if (_readerControls is not null) _readerControls.Visibility = visible ? Xaml.Visibility.Visible : Xaml.Visibility.Collapsed;
            if (_readerPagerContainer is not null) _readerPagerContainer.Visibility = visible && _readerPageCount > 0
                ? Xaml.Visibility.Visible : Xaml.Visibility.Collapsed;
            // Temporarily simplify Reader chrome; keep command implementations available.
            if (_readerBrowse is not null) _readerBrowse.Visibility = Xaml.Visibility.Collapsed;
            foreach (var command in new AppBarButton?[] { _renameFileButton, _deleteFileButton, _undoButton, _redoButton })
                if (command is not null) command.Visibility = visible ? Xaml.Visibility.Collapsed : Xaml.Visibility.Visible;
            foreach (var separator in _readerFileSeparators) separator.Visibility = visible ? Xaml.Visibility.Collapsed : Xaml.Visibility.Visible;
            UpdateExternalCommandVisibility();
            if (_readerNavigationDivider is not null) _readerNavigationDivider.Visibility = visible ? Xaml.Visibility.Visible : Xaml.Visibility.Collapsed;
            if (_readerWidthButton is not null) _readerWidthButton.IsChecked = options.Scaling == ReaderScalingMode.FitWidth;
            if (_readerHeightButton is not null) _readerHeightButton.IsChecked = options.Scaling == ReaderScalingMode.FitHeight;
            if (_readerWidthSlider is not null) _readerWidthSlider.Value = options.WidthPercent;
            if (_readerWidthLabel is not null) _readerWidthLabel.Text = $"{options.WidthPercent}%";
            if (_readerWidthControls is not null) _readerWidthControls.Visibility = options.Scaling == ReaderScalingMode.FitWidth ? Xaml.Visibility.Visible : Xaml.Visibility.Collapsed;
        }
        finally { _settingReaderControls = false; }
    }
    private void ChangeReaderToolbarOptions(ReaderOptions options)
    {
        if (_settingReaderControls || !_readerControlsVisible || options == _readerToolbarOptions) return;
        SetReaderControls(true, options);
        ReaderOptionsChanged?.Invoke(options);
    }
    private Xaml.FrameworkElement CreateReaderControls()
    {
        _readerControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            Margin = new Xaml.Thickness(8, 0, 8, 0), HorizontalAlignment = Xaml.HorizontalAlignment.Right,
            VerticalAlignment = Xaml.VerticalAlignment.Center };
        _readerWidthControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = Xaml.VerticalAlignment.Center };
        _readerWidthSlider = new Slider { Minimum = 10, Maximum = 100, StepFrequency = 1,
            SnapsTo = Microsoft.UI.Xaml.Controls.Primitives.SliderSnapsTo.StepValues,
            Width = 150, VerticalAlignment = Xaml.VerticalAlignment.Center, IsThumbToolTipEnabled = false };
        Xaml.Automation.AutomationProperties.SetName(_readerWidthSlider, "Reader image width percent");
        _readerWidthLabel = new TextBlock { Width = 40, VerticalAlignment = Xaml.VerticalAlignment.Center };
        _readerWidthSlider.ValueChanged += (_, args) => ChangeReaderToolbarOptions(_readerToolbarOptions with { WidthPercent = (int)Math.Round(args.NewValue) });
        _readerWidthControls.Children.Add(_readerWidthSlider); _readerWidthControls.Children.Add(_readerWidthLabel);
        _readerControls.Children.Add(_readerWidthControls);
        var buttons = new CommandBar { Background = new Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black),
            Height = 48, OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Collapsed,
            IsDynamicOverflowEnabled = false, DefaultLabelPosition = CommandBarDefaultLabelPosition.Collapsed };
        _readerWidthButton = CreateReaderScaleButton("ReaderFitWidth.svg", "Resize to width", ReaderScalingMode.FitWidth);
        _readerHeightButton = CreateReaderScaleButton("ReaderFitHeight.svg", "Resize to height", ReaderScalingMode.FitHeight);
        buttons.PrimaryCommands.Add(_readerWidthButton); buttons.PrimaryCommands.Add(_readerHeightButton);
        _readerControls.Children.Add(buttons);
        SetReaderControls(_readerControlsVisible, _readerToolbarOptions);
        return _readerControls;
    }
    private void AddReaderNavigationControls(CommandBar middle)
    {
        _readerFileSeparators.Clear(); _readerFileSeparators.AddRange(middle.PrimaryCommands.OfType<AppBarSeparator>());
        _readerNavigationDivider = new AppBarSeparator(); middle.PrimaryCommands.Add(_readerNavigationDivider);
        _readerPagerContainer = new AppBarElementContainer { Content = CreateReaderPager(),
            VerticalAlignment = Xaml.VerticalAlignment.Center };
        middle.PrimaryCommands.Add(_readerPagerContainer);
        _readerBrowse = CreateButton(Symbol.OpenFile, "Open current Reader image in Browse (Enter)", () => ReaderOpenBrowseRequested?.Invoke());
        middle.PrimaryCommands.Add(_readerBrowse);
        SetReaderNavigation(_readerPageIndex, _readerPageCount);
    }

    private Xaml.FrameworkElement CreateReaderPager()
    {
#if EXPERIMENTAL_READER_PAGER
        _readerPager = new PagerControl
        {
            DisplayMode = PagerControlDisplayMode.NumberBox,
            VerticalAlignment = Xaml.VerticalAlignment.Center,
            PrefixText = "Page", SuffixText = "of"
        };
        Xaml.Automation.AutomationProperties.SetName(_readerPager, "Reader page navigation");
        _readerPager.SelectedIndexChanged += (sender, _) => RequestReaderPage(sender.SelectedPageIndex);
        return _readerPager;
#else
        var pager = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4,
            VerticalAlignment = Xaml.VerticalAlignment.Center };
        Xaml.Automation.AutomationProperties.SetName(pager, "Reader page navigation");
        _readerFirstPage = CreateReaderPageButton("FirstPageButton", "First page", "\uE892", () => 0);
        _readerPreviousPage = CreateReaderPageButton("PreviousPageButton", "Previous page", "\uE76B", () => _readerPageIndex - 1);
        _readerNextPage = CreateReaderPageButton("NextPageButton", "Next page", "\uE76C", () => _readerPageIndex + 1);
        _readerLastPage = CreateReaderPageButton("LastPageButton", "Last page", "\uE893", () => _readerPageCount - 1);
        _readerPageInput = new NumberBox
        {
            Minimum = 1, Maximum = 1, Value = 1, MinWidth = 64,
            VerticalAlignment = Xaml.VerticalAlignment.Center
        };
        Xaml.Automation.AutomationProperties.SetName(_readerPageInput, "Reader page number");
        _readerPageInput.ValueChanged += (_, args) =>
        {
            if (_settingReaderPager) return;
            var value = args.NewValue;
            if (value >= 1 && value <= _readerPageCount && value == Math.Truncate(value))
                RequestReaderPage((int)value - 1);
            else
                SetReaderNavigation(_readerPageIndex, _readerPageCount);
        };
        _readerPageTotal = new TextBlock { VerticalAlignment = Xaml.VerticalAlignment.Center };
        pager.Children.Add(_readerFirstPage);
        pager.Children.Add(_readerPreviousPage);
        pager.Children.Add(new TextBlock { Text = "Page", VerticalAlignment = Xaml.VerticalAlignment.Center });
        pager.Children.Add(_readerPageInput);
        pager.Children.Add(_readerPageTotal);
        pager.Children.Add(_readerNextPage);
        pager.Children.Add(_readerLastPage);
        return pager;
#endif
    }

    private void RequestReaderPage(int page)
    {
        if (!_settingReaderPager && _readerControlsVisible && page >= 0 && page < _readerPageCount)
            ReaderPageRequested?.Invoke(page);
    }

    private void ReleaseReaderPager()
    {
        _readerPagerContainer = null;
#if EXPERIMENTAL_READER_PAGER
        _readerPager = null;
#else
        _readerPageInput = null;
        _readerPageTotal = null;
        _readerFirstPage = _readerPreviousPage = _readerNextPage = _readerLastPage = null;
#endif
    }

#if !EXPERIMENTAL_READER_PAGER
    private Button CreateReaderPageButton(string name, string label, string glyph, Func<int> target)
    {
        var button = new Button { Name = name, Width = 32, Height = 32, Padding = new Xaml.Thickness(0),
            Content = new FontIcon { Glyph = glyph, FontSize = 16 },
            VerticalAlignment = Xaml.VerticalAlignment.Center };
        Xaml.Automation.AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        button.Click += (_, _) => RequestReaderPage(target());
        return button;
    }
#endif

    private AppBarToggleButton CreateReaderScaleButton(string iconResource, string label, ReaderScalingMode mode)
    {
        using var stream = typeof(WinUiToolbarIsland).Assembly.GetManifestResourceStream(iconResource)
            ?? throw new InvalidOperationException($"Missing Reader icon resource {iconResource}.");
        Xml.XNamespace svg = "http://www.w3.org/2000/svg";
        var path = Xml.XDocument.Load(stream).Root!.Element(svg + "path")!;
        var iconMarkup = new Xml.XElement(
            Xml.XName.Get("PathIcon", "http://schemas.microsoft.com/winfx/2006/xaml/presentation"),
            new Xml.XAttribute("Data", path.Attribute("d")!.Value));
        var icon = (PathIcon)Xaml.Markup.XamlReader.Load(iconMarkup.ToString());
        // Match the compact toolbar's 16-DIP icon slot while retaining the SVG's centered 24-unit viewport.
        const double iconSize = 16;
        const double scale = iconSize / 24;
        icon.Width = iconSize;
        icon.Height = iconSize;
        icon.Data.Transform = new Xaml.Media.ScaleTransform { ScaleX = scale, ScaleY = scale };
        var button = new AppBarToggleButton { Icon = icon,
            Width = 32, Label = label, LabelPosition = CommandBarLabelPosition.Collapsed };
        Xaml.Automation.AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        button.Checked += (_, _) => ChangeReaderToolbarOptions(_readerToolbarOptions with { Scaling = mode });
        button.Unchecked += (_, _) =>
        {
            // Scaling is a choice between two modes; pressing the selected button keeps it selected.
            if (!_settingReaderControls && _readerToolbarOptions.Scaling == mode) SetReaderControls(_readerControlsVisible, _readerToolbarOptions);
        };
        return button;
    }
}
