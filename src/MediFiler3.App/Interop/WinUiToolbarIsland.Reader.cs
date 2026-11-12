using MediFiler3.Application.Navigation;
using Microsoft.UI.Xaml.Controls;
using Xaml = Microsoft.UI.Xaml;

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
    public event Action<ReaderNavigation>? ReaderNavigateRequested;
    public event Action? ReaderJumpRequested, ReaderOpenBrowseRequested;
    private AppBarButton? _readerPrevious, _readerNext, _readerJump, _readerBrowse;
    private int _readerPageIndex = -1, _readerPageCount;
    private readonly List<AppBarSeparator> _readerFileSeparators = [];
    private AppBarSeparator? _readerNavigationDivider;
    public void SetReaderNavigation(int index, int count)
    {
        _readerPageIndex = index; _readerPageCount = count;
        if (_readerPrevious is not null) _readerPrevious.IsEnabled = index > 0 && count > 0;
        if (_readerNext is not null) _readerNext.IsEnabled = index >= 0 && index + 1 < count;
        if (_readerJump is not null) _readerJump.IsEnabled = count > 0;
        if (_readerBrowse is not null) _readerBrowse.IsEnabled = index >= 0 && index < count;
    }

    public void SetReaderControls(bool visible, ReaderOptions options)
    {
        options.Validate(); _readerControlsVisible = visible; _readerToolbarOptions = options;
        _settingReaderControls = true;
        try
        {
            if (_readerControls is not null) _readerControls.Visibility = visible ? Xaml.Visibility.Visible : Xaml.Visibility.Collapsed;
            foreach (var command in new AppBarButton?[] { _readerPrevious, _readerNext, _readerJump })
                if (command is not null) command.Visibility = visible ? Xaml.Visibility.Visible : Xaml.Visibility.Collapsed;
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
        _readerWidthButton = CreateReaderScaleButton("\uE8AB", "Resize to width", ReaderScalingMode.FitWidth); // Switch
        _readerHeightButton = CreateReaderScaleButton("\uE8CB", "Resize to height", ReaderScalingMode.FitHeight); // Sort
        buttons.PrimaryCommands.Add(_readerWidthButton); buttons.PrimaryCommands.Add(_readerHeightButton);
        _readerControls.Children.Add(buttons);
        SetReaderControls(_readerControlsVisible, _readerToolbarOptions);
        return _readerControls;
    }
    private void AddReaderNavigationControls(CommandBar middle)
    {
        _readerFileSeparators.Clear(); _readerFileSeparators.AddRange(middle.PrimaryCommands.OfType<AppBarSeparator>());
        _readerNavigationDivider = new AppBarSeparator(); middle.PrimaryCommands.Add(_readerNavigationDivider);
        _readerPrevious = CreateButton(Symbol.Back, "Previous Reader image (Left)", () => ReaderNavigateRequested?.Invoke(ReaderNavigation.PreviousImage));
        _readerNext = CreateButton(Symbol.Forward, "Next Reader image (Right)", () => ReaderNavigateRequested?.Invoke(ReaderNavigation.NextImage));
        _readerJump = CreateButton(Symbol.Find, "Go to Reader page (Ctrl+G)", () => ReaderJumpRequested?.Invoke());
        middle.PrimaryCommands.Add(_readerPrevious); middle.PrimaryCommands.Add(_readerNext);
        _readerBrowse = CreateButton(Symbol.OpenFile, "Open current Reader image in Browse (Enter)", () => ReaderOpenBrowseRequested?.Invoke());
        middle.PrimaryCommands.Add(_readerJump); middle.PrimaryCommands.Add(_readerBrowse);
        SetReaderNavigation(_readerPageIndex, _readerPageCount);
    }

    private AppBarToggleButton CreateReaderScaleButton(string glyph, string label, ReaderScalingMode mode)
    {
        var button = new AppBarToggleButton { Icon = new FontIcon { Glyph = glyph, FontSize = 16 },
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
