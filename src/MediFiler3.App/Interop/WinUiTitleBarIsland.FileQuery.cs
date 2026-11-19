using MediFiler3.Application.Files;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MediFiler3.Interop;

public sealed partial class WinUiTitleBarIsland
{
    private Grid? _fileSearchLayout;
    private Button? _fileFilterButton;
    private TextBlock? _filterCountLabel;
    private StackPanel? _recentFilterChips;
    private bool _fileSearchVisible;
    private Guid? _activeFilterId;
    private FileQuery _effectiveFileQuery = FileQuery.AllFiles;
    private IReadOnlyList<SavedFileFilter> _savedFileFilters = [];
    private IReadOnlyList<Guid> _recentFileFilters = [];
    private string _fileResultCount = "";
    private int _toolbarFilterTagCount = 3;
    private TextBlock? _toolbarTagMeasure;
    private double _filterAvailableWidth;
    private System.Windows.Rect _filterBounds = System.Windows.Rect.Empty;
    public bool HasOpenPopup => _root?.XamlRoot is { } root &&
        Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0;

    public double SetFilterBounds(double left, double top, double availableWidth, double height)
    {
        if (_fileSearchLayout is null) return 0;
        if (_filterAvailableWidth != availableWidth)
        {
            _filterAvailableWidth = availableWidth;
            UpdateRecentFilterChips();
        }
        _fileSearchLayout.Margin = new Thickness(left, top, 0, 0);
        _fileSearchLayout.Height = height;
        if (_fileFilterButton is not null) _fileFilterButton.Height = height;
        var width = _fileSearchVisible ? _fileSearchLayout.Width : 0;
        _filterBounds = double.IsFinite(width) && width > 0
            ? new System.Windows.Rect(left, top, width, height) : System.Windows.Rect.Empty;
        return double.IsFinite(width) ? width : 0;
    }

    public event Action<Guid?>? FileFilterSelected;
    public event Action<FileQuery>? FileFilterApplied;
    public event Action<bool, FileQuery>? FileFilterSaveRequested;
    public event Action? FileFilterRemoveRequested;

    public void SetFileFilterState(bool visible, FileQuery query, Guid? activeId,
        IReadOnlyList<SavedFileFilter> saved, IReadOnlyList<Guid> recent, int tagCount)
    {
        _fileSearchVisible = visible;
        _effectiveFileQuery = query;
        _activeFilterId = activeId;
        _savedFileFilters = saved;
        _recentFileFilters = recent;
        _toolbarFilterTagCount = tagCount;
        UpdateFileFilterControls();
    }

    public void SetFileResultCount(string count)
    {
        _fileResultCount = count;
        UpdateFileFilterLabel();
    }

    private Grid CreateFileFilterLayout()
    {
        _fileSearchLayout = new Grid
        {
            Width = 102, Height = 34, ColumnSpacing = 6,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top
        };
        _fileSearchLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _fileSearchLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var filterContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        filterContent.Children.Add(new FontIcon { Glyph = "\uE71C", FontSize = 14 });
        _filterCountLabel = new TextBlock { FontSize = 13, Text = "Filters" };
        filterContent.Children.Add(new Viewbox
        {
            Child = _filterCountLabel, MaxWidth = 55, StretchDirection = StretchDirection.DownOnly,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
        });
        _fileFilterButton = new Button { Content = filterContent, Width = 96, Height = 34, Padding = new Thickness(8, 1, 8, 1) };
        ApplyCaptionBackground(_fileFilterButton, "Button");
        var flyout = new Flyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft,
            ShouldConstrainToRootBounds = false
        };
        var presenterStyle = new Style(typeof(FlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        presenterStyle.Setters.Add(new Setter(ScrollViewer.HorizontalScrollModeProperty, ScrollMode.Disabled));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 470d));
        presenterStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        presenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12)));
        flyout.FlyoutPresenterStyle = presenterStyle;
        flyout.Opening += (_, _) => flyout.Content = CreateFileFilterEditor(flyout);
        _fileFilterButton.Flyout = flyout;
        Grid.SetColumn(_fileFilterButton, 0);
        _fileSearchLayout.Children.Add(_fileFilterButton);

        _recentFilterChips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        Grid.SetColumn(_recentFilterChips, 1);
        _fileSearchLayout.Children.Add(_recentFilterChips);
        UpdateFileFilterControls();
        return _fileSearchLayout;
    }

    private void UpdateFileFilterControls()
    {
        if (_fileSearchLayout is null) return;
        if (!_fileSearchVisible) _fileFilterButton?.Flyout?.Hide();
        _fileSearchLayout.Visibility = _fileSearchVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        UpdateFileFilterLabel();
        UpdateRecentFilterChips();
    }

    private SavedFileFilter? ActiveSavedFilter => _savedFileFilters.FirstOrDefault(filter => filter.Id == _activeFilterId);

    private string FileFilterName
    {
        get
        {
            var active = ActiveSavedFilter;
            if (_effectiveFileQuery == (active?.Query ?? FileQuery.AllFiles)) return active?.Name ?? "No filter";
            return active is null ? "Unsaved filter" : $"{active.Name} (unsaved)";
        }
    }

    private void UpdateFileFilterLabel()
    {
        if (_fileFilterButton is null || _filterCountLabel is null) return;
        _filterCountLabel.Text = _fileResultCount.Length > 0 ? _fileResultCount : "Filters";
        var description = string.IsNullOrEmpty(_fileResultCount) ? "Filters" : $"Filters, {_fileResultCount} files";
        ToolTipService.SetToolTip(_fileFilterButton, description);
        AutomationProperties.SetName(_fileFilterButton, description);
    }

    private void UpdateRecentFilterChips()
    {
        if (_recentFilterChips is null || _fileSearchLayout is null) return;
        const double filterButtonWidth = 102;
        const double minimumTagWidth = 34;
        var available = Math.Max(filterButtonWidth, _filterAvailableWidth);
        var capacity = Math.Max(0, (int)((available - filterButtonWidth) / (minimumTagWidth + 4)));
        var recent = RecentFilters().Take(Math.Min(capacity, _toolbarFilterTagCount)).ToArray();
        var existing = _recentFilterChips.Children.OfType<Button>().ToArray();
        var byId = recent.ToDictionary(filter => filter.Id);
        if (existing.Length == recent.Length && existing.All(button => byId.ContainsKey(((SavedFileFilter)button.Tag).Id)))
            recent = existing.Select(button => byId[((SavedFileFilter)button.Tag).Id]).ToArray();

        _toolbarTagMeasure ??= new TextBlock { FontSize = 14 };
        var preferredWidths = recent.Select(filter => MeasureFilterTag(filter.Name, 108, _toolbarTagMeasure)).ToArray();
        var preferredWidth = preferredWidths.Sum() + 4 * recent.Length;
        var width = Math.Min(available, filterButtonWidth + preferredWidth);
        _fileSearchLayout.Width = width;
        var budget = width - filterButtonWidth - 4 * recent.Length;
        var excess = Math.Max(0, preferredWidths.Sum() - budget);
        var shrinkable = preferredWidths.Sum(tagWidth => tagWidth - minimumTagWidth);

        if (!existing.Select(button => ((SavedFileFilter)button.Tag).Id).SequenceEqual(recent.Select(filter => filter.Id)))
            _recentFilterChips.Children.Clear();
        for (var index = 0; index < recent.Length; index++)
        {
            var filter = recent[index];
            var preferred = preferredWidths[index];
            var chipWidth = shrinkable > 0 ? preferred - excess * (preferred - minimumTagWidth) / shrinkable : preferred;
            if (index < _recentFilterChips.Children.Count)
            {
                var current = (Button)_recentFilterChips.Children[index];
                current.Tag = filter;
                current.Width = chipWidth;
                ((TextBlock)current.Content).Text = filter.Name;
                StyleFilterTag(current, filter, inCaption: true);
                continue;
            }
            var button = new Button { Content = new TextBlock { Text = filter.Name,
                TextTrimming = TextTrimming.CharacterEllipsis }, Tag = filter, Width = chipWidth, Padding = new Thickness(10, 4, 10, 4) };
            RoundFilterChip(button);
            ApplyCaptionBackground(button, "Button");
            StyleFilterTag(button, filter, inCaption: true);
            button.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => ToggleFileFilter(filter.Id)));
            _recentFilterChips.Children.Add(button);
        }
    }

    private IEnumerable<SavedFileFilter> RecentFilters() =>
        _recentFileFilters.SelectMany(id => _savedFileFilters.Where(filter => filter.Id == id));

    public bool IsFileFilterEditorOpen => _fileFilterButton?.Flyout?.IsOpen == true;

}
