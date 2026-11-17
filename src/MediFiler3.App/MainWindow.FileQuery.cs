using System.Windows.Input;
using System.Windows.Threading;
using MediFiler3.Application.Files;
using MediFiler3.Application.Persistence;

namespace MediFiler3;

public partial class MainWindow
{
    private readonly DispatcherTimer _fileSearchDebounce = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(220)
    };
    private ApplicationSettingsService _filterSettings = null!;
    private FileQuery _effectiveFileQuery = FileQuery.AllFiles;
    private Guid? _activeFileFilter;
    private bool FileFiltersAvailable => PageContent.Content == _browseView;

    private void InitializeFileFilters(ApplicationSettingsService settings)
    {
        _filterSettings = settings;
        _settingsView.SetToolbarFilterTagCount(settings.Current.ToolbarFilterTagCount);
        _settingsView.ToolbarFilterTagCountChanged += count =>
        {
            settings.Update(current => current with { ToolbarFilterTagCount = count });
            UpdateFileFilterToolbar();
        };
        _browseView.FileQueryStatusChanged += TitleBarIsland.SetFileResultCount;
        TitleBarIsland.ViewShortcutHandler = TryHandleViewShortcut;
        _fileSearchDebounce.Tick += (_, _) => ApplyPendingFileSearch();
        TitleBarIsland.FileSearchChanged += search =>
        {
            if (!FileFiltersAvailable || _effectiveFileQuery.Search == search) return;
            _effectiveFileQuery = _effectiveFileQuery with { Search = search };
            UpdateFileFilterToolbar();
            _fileSearchDebounce.Stop();
            _fileSearchDebounce.Start();
        };
        TitleBarIsland.FileSearchSubmitted += () =>
        {
            _effectiveFileQuery = _effectiveFileQuery with { Search = TitleBarIsland.FileSearchText };
            UpdateFileFilterToolbar();
            ApplyPendingFileSearch();
        };
        TitleBarIsland.FileSearchDismissed += () =>
        {
            if (FileFiltersAvailable) _browseView.FocusActiveViewer();
        };
        TitleBarIsland.FileFilterSelected += SelectFileFilter;
        TitleBarIsland.FileFilterApplied += ApplyEditedFileFilter;
        TitleBarIsland.FileFilterSaveRequested += async (saveAs, query) => await RunViewShortcutAsync(() => SaveFileFilterAsync(saveAs, query));
        TitleBarIsland.FileFilterRemoveRequested += RemoveFileFilter;
        Closed += (_, _) => _fileSearchDebounce.Stop();
        UpdateFileFilterToolbar();
    }

    private void UpdateFileFilterToolbar()
    {
        var settings = _filterSettings.Current;
        TitleBarIsland.Focusable = FileFiltersAvailable;
        TitleBarIsland.SetFileSearchState(FileFiltersAvailable, _effectiveFileQuery.Search);
        TitleBarIsland.SetFileFilterState(FileFiltersAvailable, _effectiveFileQuery, _activeFileFilter,
            settings.SavedFileFilters ?? [], settings.RecentFileFilters ?? [], settings.ToolbarFilterTagCount);
        UpdateAppTitleBarLayout();
    }

    private void ApplyPendingFileSearch()
    {
        _fileSearchDebounce.Stop();
        _ = RunViewShortcutAsync(() => _browseView.ApplyFileQueryAsync(_effectiveFileQuery));
    }

    private void SelectFileFilter(Guid? id)
    {
        if (!FileFiltersAvailable || !IsEnabled) return;
        var filter = _filterSettings.Current.SavedFileFilters?.FirstOrDefault(item => item.Id == id);
        if (id is not null && filter is null) return;
        _activeFileFilter = id;
        _effectiveFileQuery = filter?.Query ?? FileQuery.AllFiles;
        if (id is { } selected)
            _filterSettings.Update(settings => settings with { RecentFileFilters = TouchRecent(settings, selected) });
        UpdateFileFilterToolbar();
        ApplyPendingFileSearch();
    }

    private static Guid[] TouchRecent(ApplicationSettings settings, Guid id) =>
        new[] { id }.Concat((settings.RecentFileFilters ?? []).Where(recent => recent != id))
            .Take(SavedFileFilter.RecentLimit).ToArray();

    private void ApplyEditedFileFilter(FileQuery query)
    {
        if (!FileFiltersAvailable || !IsEnabled || _effectiveFileQuery == query) return;
        _effectiveFileQuery = query;
        UpdateFileFilterToolbar();
        ApplyPendingFileSearch();
    }

    private async Task SaveFileFilterAsync(bool saveAs, FileQuery query)
    {
        if (!FileFiltersAvailable || !IsEnabled) return;
        var settings = _filterSettings.Current;
        var initial = _effectiveFileQuery;
        var id = _activeFileFilter;
        var folder = _navigationState.ActiveFolder;
        var mode = _navigationState.ViewingMode;
        var saved = settings.SavedFileFilters ?? [];
        var active = saved.FirstOrDefault(filter => filter.Id == id);
        if (saveAs || active is null)
        {
            var name = await TitleBarIsland.PromptFileFilterNameAsync(saved);
            if (name is null || !FileFilterDialogIsCurrent(initial, id, folder, mode)) return;
            active = new SavedFileFilter(Guid.NewGuid(), name, query);
            saved = saved.Append(active).ToArray();
        }
        else
        {
            active = active with { Query = query };
            saved = saved.Select(filter => filter.Id == id ? active : filter).ToArray();
        }
        _activeFileFilter = active.Id;
        _effectiveFileQuery = query;
        _filterSettings.Update(current => current with { SavedFileFilters = saved, RecentFileFilters = TouchRecent(current, active.Id) });
        UpdateFileFilterToolbar();
        ApplyPendingFileSearch();
    }

    private void RemoveFileFilter()
    {
        if (!FileFiltersAvailable || !IsEnabled || _activeFileFilter is not { } id) return;
        _filterSettings.Update(settings => settings with
        {
            SavedFileFilters = (settings.SavedFileFilters ?? []).Where(filter => filter.Id != id).ToArray(),
            RecentFileFilters = (settings.RecentFileFilters ?? []).Where(recent => recent != id).ToArray()
        });
        _activeFileFilter = null;
        UpdateFileFilterToolbar();
    }

    private bool FileFilterDialogIsCurrent(FileQuery query, Guid? filter, Application.Folders.FolderTreeEntry? folder,
        Application.Navigation.ViewingMode mode) =>
        FileFiltersAvailable && IsEnabled && _effectiveFileQuery == query && _activeFileFilter == filter &&
        ReferenceEquals(_navigationState.ActiveFolder, folder) && _navigationState.ViewingMode == mode;

    private void FocusToolbarFileSearch()
    {
        if (IsFullscreen)
        {
            SetFullscreenChrome(true, _fullscreenSidebarVisible);
            TitleBarFrame.UpdateLayout();
        }
        TitleBarIsland.FocusFileSearch();
    }
}
