using MediFiler3.Application.Files;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MediFiler3.Interop;

public sealed partial class WinUiTitleBarIsland
{
    private FrameworkElement CreateFileFilterEditor(Flyout flyout)
    {
        var query = _effectiveFileQuery;
        var active = ActiveSavedFilter;
        var updating = false;
        var editVersion = 0;
        var fields = new StackPanel { Spacing = 8 };
        var includes = new TextBox { Header = "Filename includes", Text = query.Includes, MaxLength = FileQuery.MaximumTextLength };
        var excludes = new TextBox { Header = "Filename excludes", Text = query.Excludes, MaxLength = FileQuery.MaximumTextLength };
        var filenameFields = new Grid { ColumnSpacing = 12 };
        filenameFields.ColumnDefinitions.Add(new ColumnDefinition());
        filenameFields.ColumnDefinitions.Add(new ColumnDefinition());
        filenameFields.Children.Add(includes);
        Grid.SetColumn(excludes, 1);
        filenameFields.Children.Add(excludes);
        fields.Children.Add(filenameFields);
        var includeSubfolders = new CheckBox { Content = "Include subfolders", IsChecked = query.IncludeSubfolders, MinWidth = 0 };
        var combineFolders = new CheckBox { Content = "Combine folders", IsChecked = query.CombineFolders,
            IsEnabled = query.IncludeSubfolders, MinWidth = 0 };
        var scopeOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        scopeOptions.Children.Add(includeSubfolders);
        scopeOptions.Children.Add(combineFolders);
        fields.Children.Add(scopeOptions);
        fields.Children.Add(new TextBlock { Text = "File categories" });
        var all = new CheckBox { Content = "All", IsChecked = query.Categories == FileCategories.All };
        fields.Children.Add(all);
        var categories = Enum.GetValues<FileCategories>().Where(category => category != FileCategories.All)
            .Select(category => (Category: category, Box: new CheckBox { Content = category.ToString(),
                IsChecked = query.Categories.HasFlag(category), IsEnabled = all.IsChecked != true })).ToArray();
        var categoryRow = new Grid { ColumnSpacing = 10 };
        for (var index = 0; index < categories.Length; index++)
        {
            var box = categories[index].Box;
            box.MinWidth = 0;
            categoryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(box, index);
            categoryRow.Children.Add(box);
        }
        fields.Children.Add(categoryRow);
        fields.Children.Add(new TextBlock { Text = "Sort by" });
        var sortOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var sortLabels = new[] { "Name", "Size", "Created", "Modified" };
        var sortGroup = Guid.NewGuid().ToString();
        var sortRadios = new Dictionary<FileSort, RadioButton>();
        var selectedSort = query.Sort;
        foreach (var option in Enum.GetValues<FileSort>())
        {
            var radio = new RadioButton { Content = sortLabels[(int)option], GroupName = sortGroup,
                MinWidth = 0, IsChecked = query.Sort == option };
            sortOptions.Children.Add(radio);
            sortRadios.Add(option, radio);
        }
        fields.Children.Add(sortOptions);
        var descending = new CheckBox { Content = "Descending", IsChecked = query.Descending };
        descending.MinWidth = 0;
        sortOptions.Children.Add(descending);
        var panel = new StackPanel { Spacing = 10 };
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = FileFilterName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        header.Children.Add(title);
        var clear = ActionButton("Clear filter", () => FileFilterSelected?.Invoke(null));
        Grid.SetColumn(clear, 1);
        header.Children.Add(clear);
        panel.Children.Add(header);

        panel.Children.Add(fields);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var saveAs = ActionButton("Save as", () => FileFilterSaveRequested?.Invoke(true, ReadQuery()));
        actions.Children.Add(saveAs);
        var save = ActionButton("Save changes", () => FileFilterSaveRequested?.Invoke(false, ReadQuery()));
        var remove = ActionButton("Remove saved filter", () => FileFilterRemoveRequested?.Invoke());
        actions.Children.Add(save);
        actions.Children.Add(remove);
        UpdateSavedActions();
        if (_savedFileFilters.Count > 0)
            panel.Children.Insert(1, CreateSavedFilterTags(id =>
            {
                if (!flyout.IsOpen) return;
                editVersion++;
                ToggleFileFilter(id);
                updating = true;
                query = _effectiveFileQuery;
                active = ActiveSavedFilter;
                title.Text = FileFilterName;
                includes.Text = query.Includes;
                excludes.Text = query.Excludes;
                includeSubfolders.IsChecked = query.IncludeSubfolders;
                combineFolders.IsChecked = query.CombineFolders;
                combineFolders.IsEnabled = query.IncludeSubfolders;
                all.IsChecked = query.Categories == FileCategories.All;
                foreach (var (category, box) in categories) box.IsChecked = query.Categories.HasFlag(category);
                sortRadios[query.Sort].IsChecked = true;
                selectedSort = query.Sort;
                descending.IsChecked = query.Descending;
                UpdateSavedActions();
                UpdateCategories();
                updating = false;
            }));
        panel.Children.Add(actions);
        foreach (var (option, radio) in sortRadios)
            radio.Checked += (_, _) => { selectedSort = option; PublishEdit(); };
        all.Checked += (_, _) => CategoriesChanged();
        all.Unchecked += (_, _) => CategoriesChanged();
        includes.TextChanged += (_, _) => PublishEdit();
        excludes.TextChanged += (_, _) => PublishEdit();
        descending.Checked += (_, _) => PublishEdit();
        descending.Unchecked += (_, _) => PublishEdit();
        includeSubfolders.Checked += (_, _) => OnScopeChanged();
        includeSubfolders.Unchecked += (_, _) => OnScopeChanged();
        combineFolders.Checked += (_, _) => PublishEdit();
        combineFolders.Unchecked += (_, _) => PublishEdit();
        foreach (var (_, box) in categories)
        {
            box.Checked += (_, _) => CategoriesChanged();
            box.Unchecked += (_, _) => CategoriesChanged();
        }
        UpdateCategories();
        return new ScrollViewer { Content = panel, Width = 446, MaxHeight = 520,
            HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        FileQuery ReadQuery()
        {
            var selected = all.IsChecked == true ? FileCategories.All : categories.Where(item => item.Box.IsChecked == true)
                .Aggregate(FileCategories.All, (flags, item) => flags | item.Category);
            return query with { Search = _effectiveFileQuery.Search, Includes = includes.Text, Excludes = excludes.Text, Categories = selected,
                Sort = selectedSort, Descending = descending.IsChecked == true, IncludeSubfolders = includeSubfolders.IsChecked == true,
                CombineFolders = includeSubfolders.IsChecked == true && combineFolders.IsChecked == true };
        }

        void UpdateCategories()
        {
            foreach (var (_, box) in categories) box.IsEnabled = all.IsChecked != true;
        }

        void OnScopeChanged()
        {
            combineFolders.IsEnabled = includeSubfolders.IsChecked == true;
            PublishEdit();
        }

        void CategoriesChanged()
        {
            UpdateCategories();
            PublishEdit();
        }

        void PublishEdit()
        {
            if (updating) return;
            var version = ++editVersion;
            var edited = ReadQuery();
            if (edited == _effectiveFileQuery) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (version != editVersion) return;
                FileFilterApplied?.Invoke(edited);
                title.Text = FileFilterName;
            }));
        }

        void UpdateSavedActions()
        {
            save.Visibility = remove.Visibility = active is null ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        }

        Button ActionButton(string label, Action action)
        {
            var button = new Button { Content = label };
            button.Click += (_, _) => CloseAndRun(action);
            return button;
        }

        void CloseAndRun(Action action)
        {
            editVersion++;
            flyout.Hide();
            Dispatcher.BeginInvoke(action);
        }
    }

    public async Task<string?> PromptFileFilterNameAsync(IReadOnlyList<SavedFileFilter> saved)
    {
        if (System.Windows.Window.GetWindow(this) is not { } owner) return null;
        string? name = null;
        var result = await WinUiContentDialogHost.ShowAsync(owner, "Save file filter", () =>
        {
            var dialog = new ContentDialog { PrimaryButtonText = "Save", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = false };
            var panel = new StackPanel { Spacing = 8 };
            var input = new TextBox { Header = "Filter name", MaxLength = SavedFileFilter.MaximumNameLength };
            var validation = new TextBlock();
            panel.Children.Add(input);
            panel.Children.Add(validation);
            dialog.Content = panel;
            input.TextChanged += (_, _) =>
            {
                name = input.Text.Trim();
                var duplicate = saved.Any(filter => StringComparer.OrdinalIgnoreCase.Equals(filter.Name, name));
                validation.Text = duplicate ? "A filter with this name already exists." : "";
                dialog.IsPrimaryButtonEnabled = name.Length > 0 && !duplicate;
            };
            dialog.Opened += (_, _) => input.Focus(FocusState.Programmatic);
            return dialog;
        }, 480);
        return result == ContentDialogResult.Primary ? name : null;
    }
}
