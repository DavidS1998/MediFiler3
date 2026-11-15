using System.Collections.ObjectModel;
using MediFiler3.Application.Files;
using MediFiler3.Application.Folders;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MediFiler3.Interop;

public sealed partial class WinUiCarouselSettingsIsland
{
    // Reuse the rule-card presentation; file records themselves expose only name/color.
    private readonly ObservableCollection<FolderRuleEditorItem> _fileRuleItems = [];
    private FileNameColorRule[] _fileRules = [];
    private ListView? _fileRulesList;
    private TextBox? _fileRuleName;
    private ColorPicker? _fileRuleColor;
    private TextBlock? _fileRuleError;
    private Button? _fileRuleSave, _fileRuleAdd, _fileRuleRemove, _fileRuleUp, _fileRuleDown;
    private bool _fileRuleSync, _fileRuleLoading;
    private long _fileRuleUiVersion;
    public event Action<IReadOnlyList<FileNameColorRule>>? FileNameRulesChanged;

    private static FolderNameRule FileEditorRecord(FileNameColorRule rule) => new(rule.Contains, rule.Color, Enabled: rule.Enabled) { Id = rule.Id };
    private static FileNameColorRule FileRuleRecord(FolderNameRule rule) => new(rule.Contains, rule.TextColor!.Value, rule.Enabled) { Id = rule.Id };
    private FolderRuleEditorItem CreateFileRuleItem(FolderNameRule rule, bool draft = false) => new(rule, item => QueueFileRuleAction(() =>
    {
        if (_fileRuleItems.Contains(item) && !item.IsDraft) PublishFileRules();
    }), draft);

    public void SetFileNameRules(IReadOnlyList<FileNameColorRule> rules)
    {
        FileNameColorRule.ValidateAll(rules);
        if (_fileRules.SequenceEqual(rules)) return;
        _fileRules = rules.ToArray(); ResetFileRuleItems();
    }
    private void ResetFileRuleItems()
    {
        _fileRuleSync = true;
        try
        {
            _fileRuleItems.Clear();
            foreach (var rule in _fileRules) _fileRuleItems.Add(CreateFileRuleItem(FileEditorRecord(rule)));
            if (_fileRulesList is not null) _fileRulesList.SelectedItem = _fileRuleItems.FirstOrDefault();
        }
        finally { _fileRuleSync = false; }
        LoadFileRule();
    }
    private void AddFileRuleSettings(StackPanel page)
    {
        page.Children.Add(Description("Match text in the filename, including its extension. The first enabled match sets the Browse title bar and Gallery filename gradient. Reader is unchanged."));
        _fileRulesList = new ListView { ItemsSource = _fileRuleItems, SelectionMode = ListViewSelectionMode.Single,
            CanDragItems = true, CanReorderItems = true, AllowDrop = true, MaxHeight = 480,
            HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            ItemContainerStyle = _folderRulesList!.ItemContainerStyle, ItemTemplate = _folderRulesList.ItemTemplate };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_fileRulesList, "File color rules, highest priority first");
        page.Children.Add(_fileRulesList);
        Button Command(string text, Action action) { var button = new Button { Content = text }; button.Click += (_, _) => QueueFileRuleAction(action); return button; }
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _fileRuleAdd = Command("Add rule", () =>
        {
            if (_fileRuleItems.Count >= 256) return;
            var item = CreateFileRuleItem(new("", new(255, 113, 206)), true);
            _fileRuleSync = true;
            try { _fileRuleItems.Add(item); _fileRulesList.SelectedItem = item; }
            finally { _fileRuleSync = false; }
            LoadFileRule(); _fileRulesList.ScrollIntoView(item); _fileRuleName?.Focus(FocusState.Programmatic);
        });
        _fileRuleRemove = Command("Remove rule", () =>
        {
            if (_fileRulesList.SelectedItem is not FolderRuleEditorItem item) return;
            _fileRuleItems.Remove(item); _fileRulesList.SelectedItem = _fileRuleItems.FirstOrDefault();
            LoadFileRule(); PublishFileRules();
        });
        _fileRuleUp = Command("Move up", () => MoveFileRule(-1)); _fileRuleDown = Command("Move down", () => MoveFileRule(1));
        commands.Children.Add(_fileRuleAdd); commands.Children.Add(_fileRuleRemove); commands.Children.Add(_fileRuleUp); commands.Children.Add(_fileRuleDown);
        page.Children.Add(commands);
        _fileRuleName = new TextBox { Header = "File name contains", PlaceholderText = "Text to find in the filename", MaxLength = 256 };
        _fileRuleColor = new ColorPicker { IsAlphaEnabled = false, MaxWidth = 320 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_fileRuleColor, "File rule background color");
        page.Children.Add(RuleColumns(_fileRuleName, new Expander { Header = "Background color", Content = _fileRuleColor,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch }));
        _fileRuleError = Description(""); _fileRuleError.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(_fileRuleError, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        page.Children.Add(_fileRuleError);
        _fileRuleSave = Command("Save rule", SaveFileRule); page.Children.Add(_fileRuleSave);
        _fileRulesList.SelectionChanged += (_, _) => { if (!_fileRuleSync) LoadFileRule(); };
        _fileRulesList.DragItemsCompleted += (_, _) =>
        {
            var snapshot = SavedFileRules();
            QueueFileRuleAction(() => { PublishFileRules(snapshot); UpdateFileRuleButtons(); });
        };
        _fileRuleName.RegisterPropertyChangedCallback(TextBox.TextProperty, (_, _) => PreviewFileRule());
        _fileRuleColor.RegisterPropertyChangedCallback(ColorPicker.ColorProperty, (_, _) => PreviewFileRule());
        _fileRulesList.SelectedItem = _fileRuleItems.FirstOrDefault();
        LoadFileRule();
    }
    private FileNameColorRule[] SavedFileRules() => _fileRuleItems.Where(item => !item.IsDraft).Select(item => FileRuleRecord(item.SavedRule!)).ToArray();
    private void LoadFileRule()
    {
        if (_fileRuleName is null || _fileRuleColor is null) return;
        _fileRuleLoading = true;
        try
        {
            var rule = (_fileRulesList?.SelectedItem as FolderRuleEditorItem)?.Rule;
            _fileRuleName.Text = rule?.Contains ?? "";
            var color = rule?.TextColor ?? new FolderRuleColor(255, 113, 206);
            _fileRuleColor.Color = ColorHelper.FromArgb(255, color.Red, color.Green, color.Blue);
            SetFileRuleError(""); UpdateFileRuleButtons();
        }
        finally { _fileRuleLoading = false; }
    }
    private void PreviewFileRule()
    {
        if (_fileRuleLoading || _fileRuleSync || _fileRuleColor is null || _fileRulesList?.SelectedItem is not FolderRuleEditorItem item) return;
        var color = _fileRuleColor.Color;
        item.Preview(item.Rule with { Contains = _fileRuleName?.Text ?? "", TextColor = new(color.R, color.G, color.B) });
    }
    private void SaveFileRule()
    {
        if (_fileRulesList is null || _fileRuleName is null || _fileRuleColor is null) return;
        var item = _fileRulesList.SelectedItem as FolderRuleEditorItem;
        var color = _fileRuleColor.Color;
        var rule = new FileNameColorRule(_fileRuleName.Text, new(color.R, color.G, color.B), item?.Enabled ?? true) { Id = item?.Rule.Id ?? Guid.NewGuid() };
        var snapshot = _fileRuleItems.Where(row => !row.IsDraft || ReferenceEquals(row, item))
            .Select(row => ReferenceEquals(row, item) ? rule : FileRuleRecord(row.SavedRule!)).ToList();
        if (item is null) snapshot.Add(rule);
        try { FileNameColorRule.ValidateAll(snapshot); }
        catch (ArgumentException error) { SetFileRuleError(error.Message); return; }
        var replacement = CreateFileRuleItem(FileEditorRecord(rule));
        _fileRuleSync = true;
        try
        {
            if (item is null) _fileRuleItems.Add(replacement); else _fileRuleItems[_fileRuleItems.IndexOf(item)] = replacement;
            _fileRulesList.SelectedItem = replacement;
        }
        finally { _fileRuleSync = false; }
        LoadFileRule(); PublishFileRules(snapshot);
    }
    private void SetFileRuleError(string text)
    {
        if (_fileRuleError is null) return;
        _fileRuleError.Text = text; _fileRuleError.Visibility = text.Length == 0 ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    }
    private void MoveFileRule(int delta)
    {
        if (_fileRulesList?.SelectedItem is not FolderRuleEditorItem item) return;
        var index = _fileRuleItems.IndexOf(item); var next = index + delta;
        if (next < 0 || next >= _fileRuleItems.Count) return;
        _fileRuleItems.Move(index, next); _fileRulesList.SelectedItem = item; PublishFileRules(); UpdateFileRuleButtons();
    }
    private void UpdateFileRuleButtons()
    {
        foreach (var item in _fileRuleItems) item.Selected = ReferenceEquals(item, _fileRulesList?.SelectedItem);
        var index = _fileRulesList?.SelectedIndex ?? -1;
        if (_fileRuleAdd is not null) _fileRuleAdd.IsEnabled = _fileRuleItems.Count < 256;
        if (_fileRuleRemove is not null) _fileRuleRemove.IsEnabled = index >= 0;
        if (_fileRuleUp is not null) _fileRuleUp.IsEnabled = index > 0;
        if (_fileRuleDown is not null) _fileRuleDown.IsEnabled = index >= 0 && index + 1 < _fileRuleItems.Count;
    }
    private void PublishFileRules(IReadOnlyList<FileNameColorRule>? rules = null)
    {
        rules ??= SavedFileRules(); FileNameColorRule.ValidateAll(rules);
        if (_fileRules.SequenceEqual(rules)) return;
        _fileRules = rules.ToArray(); FileNameRulesChanged?.Invoke(_fileRules);
    }
    private void QueueFileRuleAction(Action action)
    {
        if (!IsLoaded || _fileRuleSync || _fileRulesList is null) return;
        var list = _fileRulesList; var version = _fileRuleUiVersion;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_xamlSource is not null && IsLoaded && ReferenceEquals(list, _fileRulesList) && version == _fileRuleUiVersion) action();
        }));
    }
    private void DetachFileRuleSource()
    {
        _fileRuleUiVersion++;
        _fileRuleSync = true;
        try { if (_fileRulesList is not null) _fileRulesList.ItemsSource = null; }
        finally { _fileRuleSync = false; }
        // Reset previews and cancelled drag order while the managed source is detached.
        ResetFileRuleItems();
    }
    private void ResumeFileRuleSource()
    {
        if (_fileRulesList is null || ReferenceEquals(_fileRulesList.ItemsSource, _fileRuleItems)) return;
        _fileRuleUiVersion++; _fileRuleSync = true;
        try { _fileRulesList.ItemsSource = _fileRuleItems; _fileRulesList.SelectedItem = _fileRuleItems.FirstOrDefault(); }
        finally { _fileRuleSync = false; }
        LoadFileRule();
    }
    private void ReleaseFileRuleControls()
    {
        DetachFileRuleSource(); _fileRulesList = null; _fileRuleName = null; _fileRuleColor = null; _fileRuleError = null;
        _fileRuleSave = _fileRuleAdd = _fileRuleRemove = _fileRuleUp = _fileRuleDown = null;
    }
}
