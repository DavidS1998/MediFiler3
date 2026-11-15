using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using MediFiler3.Application.Folders;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace MediFiler3.Interop;

public sealed partial class WinUiCarouselSettingsIsland
{
    [Microsoft.UI.Xaml.Data.Bindable]
    public sealed class FolderRuleEditorItem : INotifyPropertyChanged
    {
        private readonly Action<FolderRuleEditorItem>? _enabledChanged;
        public FolderRuleEditorItem(FolderNameRule rule, Action<FolderRuleEditorItem>? enabledChanged = null, bool draft = false)
        {
            Rule = rule; SavedRule = draft ? null : rule; _enabledChanged = enabledChanged;
        }
        public FolderNameRule Rule { get; private set; }
        public FolderNameRule? SavedRule { get; private set; }
        public bool IsDraft => SavedRule is null;
        public bool IsDirty => Rule != SavedRule;
        private bool _selected;
        public bool Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(BorderBrush))); } }
        public Microsoft.UI.Xaml.Media.Brush BorderBrush => new Microsoft.UI.Xaml.Media.SolidColorBrush(Selected
            ? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent)
            : ColorHelper.FromArgb(255, 52, 54, 59));
        public void Preview(FolderNameRule rule)
        {
            Rule = rule;
            foreach (var property in new[] { nameof(Name), nameof(Summary), nameof(PreviewBrush), nameof(ExpansionGlyph), nameof(ExpansionVisibility), nameof(ToggleName) })
                PropertyChanged?.Invoke(this, new(property));
        }
        public string Name => Rule.Kind == FolderRuleKind.AllFolders ? "All folders" : Rule.Kind == FolderRuleKind.Name ? (string.IsNullOrWhiteSpace(Rule.Contains) ? "New name rule" : Rule.Contains) : Rule.MinimumFiles == Rule.MaximumFiles
            ? $"{Rule.MinimumFiles} files" : Rule.MaximumFiles is null ? $"{Rule.MinimumFiles ?? 0}+ files" : $"{Rule.MinimumFiles ?? 0}–{Rule.MaximumFiles} files";
        public string ToggleName => "Enable rule: " + Name;
        public Microsoft.UI.Xaml.Media.Brush PreviewBrush
        {
            get { var color = Rule.TextColor ?? new FolderRuleColor(255, 255, 255);
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorHelper.FromArgb(255, color.Red, color.Green, color.Blue)); }
        }
        public string ExpansionGlyph => Rule.ExpandedByDefault == true ? "\uE70D" : "\uE76C";
        public Visibility ExpansionVisibility => Rule.ExpandedByDefault.HasValue ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public bool Enabled
        {
            get => Rule.Enabled;
            set
            {
                if (value == Rule.Enabled) return;
                Rule = Rule with { Enabled = value };
                if (SavedRule is not null) SavedRule = SavedRule with { Enabled = value };
                PropertyChanged?.Invoke(this, new(nameof(Enabled))); PropertyChanged?.Invoke(this, new(nameof(Summary)));
                _enabledChanged?.Invoke(this);
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Summary => (IsDirty ? "Unsaved \u00B7 " : "") + (Rule.Enabled ? "" : "Disabled \u00B7 ") + string.Join(" \u00B7 ", new[]
        {
            Rule.TextColor is { } color ? $"RGB {color.Red}, {color.Green}, {color.Blue}" : null,
            Rule.HideFolder ? "Hidden" : Rule.ExpandedByDefault is { } expanded ? expanded ? "Expanded by default" : "Collapsed by default" : null,
            (Rule.HideFolder || Rule.ExpandedByDefault.HasValue) && Rule.ApplyToRootFolders ? "Including roots" : null
        }.Where(part => part is not null));
        public override string ToString() => Name + ": " + Summary;
    }

    private readonly ObservableCollection<FolderRuleEditorItem> _folderRuleItems = [];
    private FolderNameRule[] _folderRules = [];
    private ListView? _folderRulesList;
    private TextBox? _folderRuleName;
    private TextBox? _folderRuleMinimum, _folderRuleMaximum;
    private RadioButtons? _folderRuleKind;
    private StackPanel? _folderRuleRange;
    private CheckBox? _folderRuleUseColor;
    private ColorPicker? _folderRuleColor;
    private ComboBox? _folderRuleExpanded;
    private CheckBox? _folderRuleIncludeRoots;
    private TextBlock? _folderRuleError;
    private Button? _folderRuleSave, _folderRuleAdd, _folderRuleRemove, _folderRuleUp, _folderRuleDown;
    private Guid? _editingFolderRule;
    private bool _folderRuleSync, _folderRuleLoading;
    private long _folderRuleUiVersion;
    public event Action<IReadOnlyList<FolderNameRule>>? FolderNameRulesChanged;

    public void SetFolderNameRules(IReadOnlyList<FolderNameRule> rules)
    {
        FolderNameRule.ValidateAll(rules);
        if (_folderRules.SequenceEqual(rules)) return;
        _folderRules = rules.ToArray();
        _folderRuleSync = true;
        try
        {
            _folderRuleItems.Clear();
            foreach (var rule in rules) _folderRuleItems.Add(CreateRuleItem(rule));
            if (_folderRulesList is not null) _folderRulesList.SelectedIndex = rules.Count > 0 ? 0 : -1;
        }
        finally { _folderRuleSync = false; }
        LoadFolderRule(_folderRuleItems.FirstOrDefault()?.Rule);
    }

    private void AddFolderRuleSettings(StackPanel page)
    {
        page.Children.Add(Heading("Folder rules"));
        page.Children.Add(Description("Match a folder name or its direct file count. Drag grips or use Move up/down to set priority. For each action, the first enabled match that sets it wins. Unchanged actions allow lower rules to apply; otherwise depth shading applies."));
        _folderRulesList = new ListView
        {
            ItemsSource = _folderRuleItems, SelectionMode = ListViewSelectionMode.Single,
            CanDragItems = true, CanReorderItems = true, AllowDrop = true, MaxHeight = 480,
            ItemContainerStyle = (Style)XamlReader.Load("""
                <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
                    <Setter Property="Padding" Value="0"/><Setter Property="MinHeight" Value="0"/>
                    <Setter Property="Margin" Value="0,0,16,4"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/>
                </Style>
                """),
            HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            ItemTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <Border Background="#1C1E22" BorderBrush="{Binding BorderBrush}" BorderThickness="2" CornerRadius="6" Padding="8,4">
                    <Grid ColumnSpacing="12">
                        <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                        <FontIcon Glyph="&#xE700;" FontSize="16" AutomationProperties.AccessibilityView="Raw"/>
                        <StackPanel Grid.Column="1" Spacing="0">
                            <Grid ColumnSpacing="8">
                                <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                                <FontIcon Glyph="{Binding ExpansionGlyph}" Visibility="{Binding ExpansionVisibility}" FontSize="12" IsHitTestVisible="False" AutomationProperties.AccessibilityView="Raw"/>
                                <TextBlock Grid.Column="1" Text="{Binding Name}" Foreground="{Binding PreviewBrush}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                            </Grid>
                            <TextBlock Text="{Binding Summary}" TextTrimming="CharacterEllipsis" FontSize="12"/>
                        </StackPanel>
                        <ToggleSwitch Grid.Column="2" IsOn="{Binding Enabled, Mode=TwoWay}" OnContent="On" OffContent="Off" MinWidth="70" AutomationProperties.Name="{Binding ToggleName}"/>
                    </Grid>
                    </Border>
                </DataTemplate>
                """)
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_folderRulesList, "Folder rules, highest priority first");
        page.Children.Add(_folderRulesList);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _folderRuleAdd = RuleButton("Add rule", () =>
        {
            if (_folderRuleItems.Count >= 256) return;
            var draft = CreateRuleItem(new FolderNameRule("", new(255, 113, 206)), true);
            _folderRuleSync = true;
            try { _folderRuleItems.Add(draft); _folderRulesList.SelectedItem = draft; }
            finally { _folderRuleSync = false; }
            LoadFolderRule(draft.Rule); _folderRulesList.ScrollIntoView(draft); _folderRuleName?.Focus(FocusState.Programmatic);
        });
        _folderRuleRemove = RuleButton("Remove rule", RemoveFolderRule);
        _folderRuleUp = RuleButton("Move up", () => MoveFolderRule(-1));
        _folderRuleDown = RuleButton("Move down", () => MoveFolderRule(1));
        commands.Children.Add(_folderRuleAdd); commands.Children.Add(_folderRuleRemove);
        commands.Children.Add(_folderRuleUp); commands.Children.Add(_folderRuleDown); page.Children.Add(commands);

        _folderRuleKind = new RadioButtons { Header = "Rule condition", MaxColumns = 3, HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
        _folderRuleKind.Items.Add("Folder name"); _folderRuleKind.Items.Add("Number of files"); _folderRuleKind.Items.Add("All folders");
        _folderRuleName = new TextBox { Header = "Folder name contains", PlaceholderText = "Text to find in the folder name", MaxLength = 256 };
        _folderRuleMinimum = new TextBox { Header = "Minimum files (inclusive)", PlaceholderText = "0", MaxLength = 19 };
        _folderRuleMaximum = new TextBox { Header = "Maximum files (inclusive)", PlaceholderText = "No upper limit", MaxLength = 19 };
        _folderRuleRange = new StackPanel { Spacing = 8 };
        _folderRuleRange.Children.Add(RuleColumns(_folderRuleMinimum, _folderRuleMaximum));
        _folderRuleRange.Children.Add(Description("Use 0 to 0 for empty folders. For more than 100 files, set minimum 101 and leave maximum blank. Unknown counts do not match."));
        _folderRuleKind.SelectionChanged += (_, _) => UpdateRuleConditionVisibility();
        _folderRuleUseColor = new CheckBox { Content = "Text color" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_folderRuleUseColor, "Set folder rule text color");
        _folderRuleColor = new ColorPicker { Color = ColorHelper.FromArgb(255, 255, 113, 206), IsAlphaEnabled = false, MaxWidth = 320 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_folderRuleColor, "Folder rule text color");
        _folderRuleExpanded = new ComboBox { Height = 64, HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
        foreach (var option in new[] { "Leave expansion unchanged", "Expanded by default", "Collapsed by default", "Hide" }) _folderRuleExpanded.Items.Add(option);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_folderRuleExpanded, "Folder expansion action");
        page.Children.Add(_folderRuleKind); page.Children.Add(_folderRuleName); page.Children.Add(_folderRuleRange);
        var colorEditor = new Expander { Header = _folderRuleUseColor, Content = _folderRuleColor,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top };
        colorEditor.SizeChanged += (_, _) =>
        {
            // Keep the combo aligned with the collapsed color header, even when its picker is open.
            if (!colorEditor.IsExpanded && colorEditor.ActualHeight > 0) _folderRuleExpanded.Height = colorEditor.ActualHeight;
        };
        _folderRuleExpanded.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top;
        _folderRuleIncludeRoots = new CheckBox { Content = "Apply to root folders too" };
        var expansionEditor = new StackPanel { Spacing = 8 };
        expansionEditor.Children.Add(_folderRuleExpanded); expansionEditor.Children.Add(_folderRuleIncludeRoots);
        page.Children.Add(RuleColumns(colorEditor, expansionEditor));
        _folderRuleError = Description("");
        _folderRuleError.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        _folderRuleError.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) =>
            _folderRuleError.Visibility = string.IsNullOrEmpty(_folderRuleError.Text) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(_folderRuleError, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        page.Children.Add(_folderRuleError);
        _folderRuleSave = RuleButton("Save rule", SaveFolderRule); page.Children.Add(_folderRuleSave);
        _folderRuleUseColor.Checked += (_, _) => _folderRuleColor.IsEnabled = true;
        _folderRuleUseColor.Unchecked += (_, _) => _folderRuleColor.IsEnabled = false;
        _folderRulesList.SelectionChanged += (_, _) =>
        {
            if (_folderRuleSync) return;
            var rule = (_folderRulesList.SelectedItem as FolderRuleEditorItem)?.Rule;
            if (rule?.Id != _editingFolderRule) LoadFolderRule(rule);
            UpdateFolderRuleButtons();
        };
        _folderRuleName.TextChanged += (_, _) => UpdateFolderRuleDraft();
        _folderRuleMinimum.TextChanged += (_, _) => UpdateFolderRuleDraft();
        _folderRuleMaximum.TextChanged += (_, _) => UpdateFolderRuleDraft();
        _folderRuleKind.SelectionChanged += (_, _) => UpdateFolderRuleDraft();
        _folderRuleColor.ColorChanged += (_, _) => UpdateFolderRuleDraft();
        _folderRuleUseColor.Checked += (_, _) => UpdateFolderRuleDraft();
        _folderRuleUseColor.Unchecked += (_, _) => UpdateFolderRuleDraft();
        _folderRuleExpanded.SelectionChanged += (_, _) => { UpdateFolderRuleDraft(); UpdateRootActionToggle(); };
        _folderRuleIncludeRoots.Checked += (_, _) => UpdateFolderRuleDraft();
        _folderRuleIncludeRoots.Unchecked += (_, _) => UpdateFolderRuleDraft();
        _folderRulesList.DragItemsCompleted += (_, _) => CommitFolderRuleDrag();
        _folderRulesList.SelectedIndex = _folderRuleItems.Count > 0 ? 0 : -1;
        LoadFolderRule(_folderRuleItems.FirstOrDefault()?.Rule);
    }

    private static Grid RuleColumns(FrameworkElement left, FrameworkElement right)
    {
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        left.HorizontalAlignment = right.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
        row.Children.Add(left); Grid.SetColumn(right, 1); row.Children.Add(right);
        return row;
    }

    private Button RuleButton(string text, Action action)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => QueueFolderRuleAction(action);
        return button;
    }
    private void QueueFolderRuleAction(Action action)
    {
        if (!IsLoaded || _folderRuleSync || _folderRulesList is null) return;
        var list = _folderRulesList;
        var version = _folderRuleUiVersion;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            // Never mutate a native source inside its input callback, or a disposed island.
            if (list is not null && ReferenceEquals(list, _folderRulesList) && _xamlSource is not null && IsLoaded && version == _folderRuleUiVersion) action();
        }));
    }
    private void CommitFolderRuleDrag()
    {
        var order = _folderRuleItems.Where(item => !item.IsDraft).Select(item => item.SavedRule!).ToArray();
        QueueFolderRuleAction(() => { PublishFolderRules(order); UpdateFolderRuleButtons(); });
    }
    private void LoadFolderRule(FolderNameRule? rule)
    {
        if (_folderRuleName is null || _folderRuleColor is null || _folderRuleExpanded is null || _folderRuleUseColor is null || _folderRuleKind is null) return;
        _folderRuleLoading = true;
        try
        {
            _editingFolderRule = rule?.Id;
            _folderRuleName.Text = rule?.Contains ?? "";
            _folderRuleKind.SelectedIndex = rule is null ? 0 : (int)rule.Kind;
            if (_folderRuleMinimum is not null) _folderRuleMinimum.Text = rule?.MinimumFiles?.ToString(CultureInfo.InvariantCulture) ?? "0";
            if (_folderRuleMaximum is not null) _folderRuleMaximum.Text = rule?.MaximumFiles?.ToString(CultureInfo.InvariantCulture) ?? "";
            UpdateRuleConditionVisibility();
            _folderRuleUseColor.IsChecked = rule is null || rule.TextColor is not null;
            var color = rule?.TextColor ?? new FolderRuleColor(255, 113, 206);
            _folderRuleColor.Color = ColorHelper.FromArgb(255, color.Red, color.Green, color.Blue);
            _folderRuleColor.IsEnabled = _folderRuleUseColor.IsChecked == true;
            _folderRuleExpanded.SelectedIndex = rule?.HideFolder == true ? 3 : rule?.ExpandedByDefault is { } expanded ? expanded ? 1 : 2 : 0;
            if (_folderRuleIncludeRoots is not null) _folderRuleIncludeRoots.IsChecked = rule?.ApplyToRootFolders == true;
            UpdateRootActionToggle();
            if (_folderRuleError is not null) _folderRuleError.Text = "";
            UpdateFolderRuleButtons();
        }
        finally { _folderRuleLoading = false; }
    }
    private void UpdateFolderRuleDraft()
    {
        if (_folderRuleLoading || _folderRuleSync || _folderRulesList?.SelectedItem is not FolderRuleEditorItem item || _folderRuleColor is null) return;
        TryFileCount(_folderRuleMinimum?.Text, 0, out var minimum);
        TryFileCount(_folderRuleMaximum?.Text, null, out var maximum);
        var color = _folderRuleColor.Color;
        item.Preview(item.Rule with { Contains = _folderRuleKind?.SelectedIndex == 0 ? _folderRuleName?.Text ?? "" : "",
            Kind = (FolderRuleKind)Math.Max(0, _folderRuleKind?.SelectedIndex ?? 0),
            MinimumFiles = _folderRuleKind?.SelectedIndex == 1 ? minimum : null, MaximumFiles = _folderRuleKind?.SelectedIndex == 1 ? maximum : null,
            HideFolder = _folderRuleExpanded?.SelectedIndex == 3, ApplyToRootFolders = _folderRuleIncludeRoots?.IsChecked == true,
            TextColor = _folderRuleUseColor?.IsChecked == true ? new FolderRuleColor(color.R, color.G, color.B) : null,
            ExpandedByDefault = _folderRuleExpanded?.SelectedIndex switch { 1 => true, 2 => false, _ => null } });
    }
    private void SaveFolderRule()
    {
        if (_folderRuleName is null || _folderRuleColor is null || _folderRulesList is null) return;
        var kind = (FolderRuleKind)Math.Max(0, _folderRuleKind?.SelectedIndex ?? 0);
        long? minimum = null, maximum = null;
        if (kind == FolderRuleKind.FileCount)
        {
            if (!TryFileCount(_folderRuleMinimum?.Text, 0, out minimum) || !TryFileCount(_folderRuleMaximum?.Text, null, out maximum))
            { if (_folderRuleError is not null) _folderRuleError.Text = "Enter nonnegative whole file counts."; return; }
        }
        var color = _folderRuleColor.Color;
        var enabled = _folderRuleItems.FirstOrDefault(item => item.Rule.Id == _editingFolderRule)?.Rule.Enabled ?? true;
        var rule = new FolderNameRule(kind == FolderRuleKind.Name ? _folderRuleName.Text : "",
            _folderRuleUseColor?.IsChecked == true ? new FolderRuleColor(color.R, color.G, color.B) : null,
            _folderRuleExpanded?.SelectedIndex switch { 1 => true, 2 => false, _ => null }, enabled)
            { Id = _editingFolderRule ?? Guid.NewGuid(), Kind = kind, MinimumFiles = minimum, MaximumFiles = maximum,
                HideFolder = _folderRuleExpanded?.SelectedIndex == 3, ApplyToRootFolders = _folderRuleIncludeRoots?.IsChecked == true };
        var index = _folderRuleItems.ToList().FindIndex(item => item.Rule.Id == rule.Id);
        var proposed = _folderRuleItems.Where(item => !item.IsDraft || item.Rule.Id == rule.Id)
            .Select(item => item.Rule.Id == rule.Id ? rule : item.SavedRule!).ToList();
        if (index < 0) proposed.Add(rule);
        try { FolderNameRule.ValidateAll(proposed); }
        catch (ArgumentException error) { if (_folderRuleError is not null) _folderRuleError.Text = error.Message; return; }
        var replacement = CreateRuleItem(rule);
        _folderRuleSync = true;
        try
        {
            if (index < 0) _folderRuleItems.Add(replacement); else _folderRuleItems[index] = replacement;
            _folderRulesList.SelectedItem = replacement;
        }
        finally { _folderRuleSync = false; }
        LoadFolderRule(rule); PublishFolderRules(proposed, forceApply: true);
    }
    private void RemoveFolderRule()
    {
        if (_folderRulesList?.SelectedItem is not FolderRuleEditorItem item) return;
        _folderRuleItems.Remove(item);
        _folderRulesList.SelectedIndex = _folderRuleItems.Count > 0 ? 0 : -1;
        LoadFolderRule((_folderRulesList.SelectedItem as FolderRuleEditorItem)?.Rule);
        PublishFolderRules(_folderRuleItems.Where(row => !row.IsDraft).Select(row => row.SavedRule!).ToArray());
    }
    private void MoveFolderRule(int direction)
    {
        if (_folderRulesList?.SelectedItem is not FolderRuleEditorItem item) return;
        var index = _folderRuleItems.IndexOf(item); var target = index + direction;
        if (target < 0 || target >= _folderRuleItems.Count) return;
        _folderRuleItems.Move(index, target); _folderRulesList.SelectedItem = item;
        PublishFolderRules(_folderRuleItems.Where(row => !row.IsDraft).Select(row => row.SavedRule!).ToArray()); UpdateFolderRuleButtons();
    }
    private void PublishFolderRules(IReadOnlyList<FolderNameRule> rules, bool forceApply = false)
    {
        if (_folderRules.SequenceEqual(rules) && !forceApply) return;
        FolderNameRule.ValidateAll(rules); _folderRules = rules.ToArray();
        FolderNameRulesChanged?.Invoke(_folderRules);
    }
    private void UpdateFolderRuleButtons()
    {
        foreach (var item in _folderRuleItems) item.Selected = ReferenceEquals(item, _folderRulesList?.SelectedItem);
        var index = _folderRulesList?.SelectedIndex ?? -1;
        if (_folderRuleRemove is not null) _folderRuleRemove.IsEnabled = index >= 0;
        if (_folderRuleUp is not null) _folderRuleUp.IsEnabled = index > 0;
        if (_folderRuleDown is not null) _folderRuleDown.IsEnabled = index >= 0 && index < _folderRuleItems.Count - 1;
        if (_folderRuleAdd is not null) _folderRuleAdd.IsEnabled = _folderRuleItems.Count < 256;
    }
    private void DetachFolderRuleSource()
    {
        // Disconnect the retained managed source while the island is still alive.
        // A cancelled/uncommitted drag must not leave the next visit in a different
        // order than the saved rules if its deferred completion was invalidated.
        _folderRuleUiVersion++; _folderRuleSync = true;
        try
        {
            if (_folderRulesList is not null) _folderRulesList.ItemsSource = null;
            if (!_folderRuleItems.Select(item => item.Rule).SequenceEqual(_folderRules))
            {
                _folderRuleItems.Clear();
                foreach (var rule in _folderRules) _folderRuleItems.Add(CreateRuleItem(rule));
            }
        }
        finally { _folderRuleSync = false; }
    }
    private void ResumeFolderRuleSource()
    {
        if (_folderRulesList is null || ReferenceEquals(_folderRulesList.ItemsSource, _folderRuleItems)) return;
        _folderRuleUiVersion++;
        _folderRuleSync = true;
        try
        {
            _folderRulesList.ItemsSource = _folderRuleItems;
            _folderRulesList.SelectedItem = _folderRuleItems.FirstOrDefault(item => item.Rule.Id == _editingFolderRule) ?? _folderRuleItems.FirstOrDefault();
        }
        finally { _folderRuleSync = false; }
        LoadFolderRule((_folderRulesList.SelectedItem as FolderRuleEditorItem)?.Rule);
    }
    private void ReleaseFolderRuleControls()
    {
        DetachFolderRuleSource();
        _folderRulesList = null; _folderRuleName = null; _folderRuleUseColor = null;
        _folderRuleMinimum = null; _folderRuleMaximum = null; _folderRuleKind = null; _folderRuleRange = null;
        _folderRuleColor = null; _folderRuleExpanded = null; _folderRuleIncludeRoots = null; _folderRuleError = null;
        _folderRuleSave = _folderRuleAdd = _folderRuleRemove = _folderRuleUp = _folderRuleDown = null;
    }
    private FolderRuleEditorItem CreateRuleItem(FolderNameRule rule, bool draft = false) => new(rule, item => QueueFolderRuleAction(() =>
    {
        if (!item.IsDraft && _folderRuleItems.Contains(item)) PublishFolderRules(_folderRuleItems.Where(row => !row.IsDraft).Select(row => row.SavedRule!).ToArray());
    }), draft);
    private void UpdateRuleConditionVisibility()
    {
        var count = _folderRuleKind?.SelectedIndex == 1;
        if (_folderRuleName is not null) _folderRuleName.Visibility = _folderRuleKind?.SelectedIndex != 0 ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        if (_folderRuleRange is not null) _folderRuleRange.Visibility = count ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }
    private void UpdateRootActionToggle()
    {
        if (_folderRuleIncludeRoots is not null) _folderRuleIncludeRoots.IsEnabled = _folderRuleExpanded?.SelectedIndex > 0;
    }
    private static bool TryFileCount(string? text, long? empty, out long? count)
    {
        if (string.IsNullOrWhiteSpace(text)) { count = empty; return true; }
        if (long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0) { count = value; return true; }
        count = null; return false;
    }
}
