using MediFiler3.Interop;
using MediFiler3.Views;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using MediFiler3.Application.Folders;
using System.IO;
using MediFiler3.Application.Files;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Operations;
using MediFiler3.Services;
using MediFiler3.Application.Viewing;
using MediFiler3.Application.Persistence;

namespace MediFiler3;

public partial class MainWindow : Window
{
    private const uint DwmwaCaptionColor = 35;
    private const uint DwmwaTextColor = 36;
    private const double CompactSidebarWidth = 56;

    public static readonly DependencyProperty TitleBarBackgroundProperty = DependencyProperty.Register(
        nameof(TitleBarBackground),
        typeof(Brush),
        typeof(MainWindow),
        new FrameworkPropertyMetadata(Brushes.Black, OnCaptionColorPropertyChanged));

    public static readonly DependencyProperty TitleBarForegroundProperty = DependencyProperty.Register(
        nameof(TitleBarForeground),
        typeof(Brush),
        typeof(MainWindow),
        new FrameworkPropertyMetadata(Brushes.White, OnCaptionColorPropertyChanged));

    public static readonly DependencyProperty IsSidebarVisibleProperty = DependencyProperty.Register(
        nameof(IsSidebarVisible),
        typeof(bool),
        typeof(MainWindow),
        new FrameworkPropertyMetadata(true, OnSidebarLayoutPropertyChanged));

    private readonly HomeView _homeView;
    private readonly BrowseView _browseView;
    private readonly SettingsView _settingsView;
    private readonly ILogger<MainWindow> _logger;
    private readonly NavigationSessionState _navigationState;
    private readonly IFileOperationJournal _operationJournal;
    private readonly IFileOrganizationService _fileOrganizationService;
    private bool _sourceInitialized;
    private long _historyRequest;
    private CancellationTokenSource? _historyAvailabilityCancellation;
    private readonly WindowLayoutController _windowLayout;
    private double _galleryWidth = 150, _galleryHeight = 150;

    public Brush TitleBarBackground
    {
        get => (Brush)GetValue(TitleBarBackgroundProperty);
        set => SetValue(TitleBarBackgroundProperty, value);
    }

    public Brush TitleBarForeground
    {
        get => (Brush)GetValue(TitleBarForegroundProperty);
        set => SetValue(TitleBarForegroundProperty, value);
    }

    public bool IsSidebarVisible
    {
        get => (bool)GetValue(IsSidebarVisibleProperty);
        set => SetValue(IsSidebarVisibleProperty, value);
    }

    public MainWindow(ILogger<MainWindow> logger, IFolderTreeService folderTreeService, IFileListingService fileListingService,
        NavigationSessionState navigationState, WicImageDecoder imageDecoder, IThumbnailService thumbnailService,
        IFileOrganizationService fileOrganizationService, IFileOperationJournal operationJournal, ITextContentService textContentService, IExternalFileService externalFileService, IDefaultApplicationService defaultApplications, ApplicationSettingsService settings, RootFolderHistoryService rootHistory, IRootFolderAvailabilityService rootAvailability, IFileCollisionPreviewService collisionPreviews, IOperationBackupMaintenance backupMaintenance, WindowLayoutService windowLayout, IWindowPlacementPlatform windowPlacement, IFolderChangeMonitor? changeMonitor = null, IReaderImageService? readerImages = null)
    {
        _homeView = new HomeView(rootHistory, rootAvailability);
        _homeView.AddFoldersRequested += OnLoadFoldersRequested;
        _homeView.FoldersDropped += async paths => await LoadFolderPathsAsync(paths);
        _homeView.OpenRootRequested = path => LoadFolderPathsAsync([path]);
        _homeView.LocateFolderRequested = oldPath =>
        {
            var picker = new OpenFolderDialog { Title = "Locate saved root folder", Multiselect = false };
            return Task.FromResult(picker.ShowDialog(this) == true ? picker.FolderName : null);
        };
        _logger = logger;
        _navigationState = navigationState;
        _operationJournal = operationJournal;
        _fileOrganizationService = fileOrganizationService;
        _browseView = new BrowseView(folderTreeService, fileListingService, navigationState, imageDecoder,
            thumbnailService, fileOrganizationService, textContentService, externalFileService, defaultApplications,
            new MediaAudioSettings(settings.Current.Volume, settings.Current.Muted), rootHistory, changeMonitor, readerImages);
        _browseView.RootHistoryPersistenceFailed += exception => _logger.LogWarning(exception, "Could not save root-folder history.");
        _browseView.SetCarouselNeighborCount((settings.Current.CarouselIconCount - 1) / 2);
        _browseView.SetFolderPinned(settings.Current.FolderTreePinned);
        _browseView.SetCarouselEnabled(settings.Current.CarouselEnabled);
        _browseView.AudioSettingsChanged += (volume, muted) => settings.Update(s => s with { Volume = volume, Muted = muted });
        _browseView.FolderPinChanged += pinned => settings.Update(s => s with { FolderTreePinned = pinned });
        Closed += (_, _) => { _historyAvailabilityCancellation?.Cancel(); _homeView.Stop(); _browseView.StopBackgroundFolderCounts(); };
        _browseView.SetReaderOptions(settings.Current.GetReaderOptions());
        _settingsView = new SettingsView(_browseView.CarouselNeighborCount, settings.Current.CarouselEnabled);
        _settingsView.SetReaderOptions(settings.Current.GetReaderOptions());
        void ApplyReaderOptions(ReaderOptions options)
        {
            settings.Update(s => s.WithReaderOptions(options));
            _browseView.SetReaderOptions(options);
            _settingsView.SetReaderOptions(options);
            ToolbarIsland.SetReaderControls(PageContent.Content == _browseView && _browseView.IsReader, options);
        }
        _settingsView.ReaderOptionsChanged += ApplyReaderOptions;
        _settingsView.CarouselNeighborCountChanged += count =>
        {
            _browseView.SetCarouselNeighborCount(count);
            settings.Update(s => s with { CarouselIconCount = count * 2 + 1 });
        };
        _settingsView.CarouselEnabledChanged += enabled =>
        {
            _browseView.SetCarouselEnabled(enabled);
            settings.Update(s => s with { CarouselEnabled = enabled });
        };
        InitializeComponent();
        ToolbarIsland.ReaderNavigateRequested += command => { if (ReaderCommandsAvailable) _browseView.NavigateReader(command); };
        ToolbarIsland.ReaderJumpRequested += async () => await RunViewShortcutAsync(PromptReaderJumpAsync);
        ToolbarIsland.ReaderOpenBrowseRequested += () => { if (ReaderCommandsAvailable) _browseView.OpenReaderInBrowse(); };
        ToolbarIsland.ReaderOptionsChanged += options =>
        {
            if (PageContent.Content == _browseView && _browseView.IsReader) ApplyReaderOptions(options);
        };
        _galleryWidth = settings.Current.GalleryThumbnailWidth;
        _galleryHeight = settings.Current.GalleryThumbnailHeight;
        _browseView.SetGalleryTileSize(_galleryWidth, _galleryHeight);
        ToolbarIsland.SetGalleryControls(false, _galleryWidth, _galleryHeight);
        ToolbarIsland.GallerySizeChanged += (width, height) =>
        {
            _galleryWidth = width; _galleryHeight = height;
            _browseView.SetGalleryTileSize(width, height);
            settings.Update(s => s with { GalleryThumbnailWidth = (int)width, GalleryThumbnailHeight = (int)height });
        };
        _browseView.BrowseRequested += () => NavigationIsland.SelectDestination("Browse");
        _browseView.GalleryRequested += () => NavigationIsland.SelectDestination("Gallery");
        ToolbarIsland.FullscreenRequested += ToggleFullscreen;
        if (thumbnailService is IThumbnailCacheMaintenance thumbnailCache)
        {
            _settingsView.CacheUsageRequested = () => thumbnailCache.GetUsageAsync();
            _settingsView.ClearThumbnailCacheRequested = () => thumbnailCache.MaintainAsync(clear: true);
        }
        ToolbarIsland.CollisionPreviews = collisionPreviews;
        _settingsView.ClearBackupsRequested = async () =>
        {
            if (!await ToolbarIsland.ConfirmAsync("Clear overwrite backups",
                "Permanently delete stored overwrite backups? Undo and Redo will be removed for the affected operations. Current files will remain in place.", "Clear backups")) return;
            _settingsView.SetBackupCleanupStatus("Clearing overwrite backups…");
            try
            {
                var result = await backupMaintenance.ClearAsync();
                _settingsView.SetBackupCleanupStatus($"Cleared {result.RemovedCount} backups." +
                    (result.Issues.Count == 0 ? string.Empty : $" {result.Issues.Count} backups could not be cleared.\n" + string.Join("\n", result.Issues.Take(3))));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
            {
                _logger.LogWarning(exception, "Could not clear overwrite backups.");
                _settingsView.SetBackupCleanupStatus($"Could not finish clearing backups: {exception.Message}");
            }
            finally { await UpdateHistoryAvailabilityAsync(); }
        };
        _browseView.DialogService = ToolbarIsland;
        PageContent.Content = _homeView;
        NavigationIsland.DestinationSelected += OnNavigationSelectionChanged;
        NavigationIsland.RefreshRequested += async () => await RefreshBrowseAsync();
        ToolbarIsland.LoadFoldersRequested += OnLoadFoldersRequested;
        ToolbarIsland.RefreshRequested += async () => await RefreshBrowseAsync();
        _browseView.RefreshRequested += async () => await RefreshBrowseAsync();
        _browseView.FolderMenuRequested += (folder, location) =>
        {
            if (IsFullscreen) SetFullscreenChrome(true, _fullscreenSidebarVisible);
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                ToolbarIsland.ShowFolderMenu(location, folder.Name,
                    action => _browseView.ExecuteFolderMenuActionAsync(folder, action))));
        };
        ToolbarIsland.ResetZoomRequested += _browseView.ResetZoomToFit;
        ToolbarIsland.ZoomInRequested += _browseView.ZoomInFromToolbar;
        ToolbarIsland.ZoomOutRequested += _browseView.ZoomOutFromToolbar;
        ToolbarIsland.OpenDefaultRequested += async () => await _browseView.OpenExternallyAsync(ExternalFileAction.DefaultApplication);
        ToolbarIsland.OpenWithRequested += async () => await _browseView.OpenExternallyAsync(ExternalFileAction.ChooseApplication);
        ToolbarIsland.TogglePlaybackRequested += _browseView.TogglePlayback;
        ToolbarIsland.StopPlaybackRequested += _browseView.StopPlayback;
        ToolbarIsland.SeekPlaybackRequested += _browseView.SeekPlayback;
        ToolbarIsland.PlaybackVolumeRequested += _browseView.SetPlaybackVolume;
        ToolbarIsland.TogglePlaybackMuteRequested += _browseView.TogglePlaybackMute;
        ToolbarIsland.JumpPlaybackRequested += _browseView.JumpPlayback;
        _browseView.PlaybackStateChanged += () => ToolbarIsland.SetMediaPlaybackState(_browseView.PlaybackState);
        ToolbarIsland.ToggleTextInteractionRequested += _browseView.ToggleTextInteraction;
        ToolbarIsland.PreviousTextWindowRequested += async () => await _browseView.ReadTextWindowAsync(false);
        ToolbarIsland.NextTextWindowRequested += async () => await _browseView.ReadTextWindowAsync(true);
        ToolbarIsland.RenameFileRequested += OnRenameFileRequested;
        ToolbarIsland.DeleteFileRequested += OnDeleteFileRequested;
        ToolbarIsland.UndoRequested += OnUndoRequested;
        ToolbarIsland.RedoRequested += OnRedoRequested;
        _browseView.ZoomLevelChanged += ToolbarIsland.SetZoomLevel;
        _browseView.PresentationChanged += UpdateViewerControls;
        _browseView.PresentationChanged += UpdateToolbarContext;
        _browseView.OperationHistoryChanged += () => _ = UpdateHistoryAvailabilityAsync();
        _navigationState.PropertyChanged += OnNavigationStateChanged;
        _navigationState.GallerySelection.Changed += UpdateFileActionAvailability;
        ToolbarIsland.SetZoomLevel(_browseView.CurrentZoomLevel);
        _ = UpdateHistoryAvailabilityAsync();
        UpdateFileActionAvailability();
        UpdateToolbarContext();
        UpdateWindowTitle();
        _browseView.SetViewerActive(false);
        UpdateViewerControls();
        SourceInitialized += OnWindowSourceInitialized;
        ApplySidebarLayout();
        _windowLayout = new WindowLayoutController(this, windowLayout, windowPlacement);
        _windowLayout.PlacementFailed += exception => _logger.LogWarning(exception, "Could not capture or reconcile window placement.");
        InitializeFullscreen();
        InitializeFolderHover();
        logger.LogInformation("Main window initialized.");
    }

    private static void OnCaptionColorPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is MainWindow window && window._sourceInitialized)
        {
            window.ApplyCaptionColors();
        }
    }

    private static void OnSidebarLayoutPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is MainWindow window && window.IsInitialized)
        {
            window.ApplySidebarLayout();
        }
    }

    private void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        _sourceInitialized = true;
        ApplyCaptionColors();
    }

    private void ApplyCaptionColors()
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        ApplyCaptionColor(windowHandle, DwmwaCaptionColor, TitleBarBackground, nameof(TitleBarBackground));
        ApplyCaptionColor(windowHandle, DwmwaTextColor, TitleBarForeground, nameof(TitleBarForeground));
    }

    private void ApplyCaptionColor(IntPtr windowHandle, uint attribute, Brush brush, string propertyName)
    {
        if (brush is not SolidColorBrush solidBrush)
        {
            _logger.LogWarning("{PropertyName} must be a solid color brush for the native title bar.", propertyName);
            return;
        }

        var color = solidBrush.Color;
        var colorRef = (uint)(color.R | (color.G << 8) | (color.B << 16));
        var result = DwmSetWindowAttribute(windowHandle, attribute, ref colorRef, sizeof(uint));
        if (result < 0)
        {
            _logger.LogWarning("Windows could not apply {PropertyName}; DwmSetWindowAttribute returned 0x{HResult:X8}.", propertyName, result);
        }
    }

    private void ApplySidebarLayout()
    {
        if (!IsInitialized)
        {
            return;
        }

        Sidebar.Visibility = IsSidebarVisible && (!IsFullscreen || _fullscreenSidebarVisible) ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = IsSidebarVisible && !IsFullscreen ? new GridLength(CompactSidebarWidth) : new GridLength(0);
    }

    private void OnNavigationSelectionChanged(string destination)
    {
        if (destination is "Browse" or "Gallery" or "Reader")
            _browseView.SetViewingMode(destination switch { "Gallery" => ViewingMode.Gallery, "Reader" => ViewingMode.Reader, _ => ViewingMode.Browse });
        PageContent.Content = destination switch
        {
            "Browse" => _browseView,
            "Gallery" => _browseView,
            "Reader" => _browseView,
            "Settings" => _settingsView,
            _ => _homeView
        };
        _browseView.SetViewerActive(destination is "Browse" or "Gallery" or "Reader");
        UpdateViewerControls();
        UpdateFileActionAvailability();
        UpdateToolbarContext();
        _ = UpdateHistoryAvailabilityAsync();
    }

    private void OnNavigationStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationSessionState.ActiveRoot)) _ = UpdateHistoryAvailabilityAsync();
        if (e.PropertyName is nameof(NavigationSessionState.CurrentFilePosition) or nameof(NavigationSessionState.Files) or
            nameof(NavigationSessionState.ActiveFolder) or nameof(NavigationSessionState.CurrentFile))
        {
            UpdateFileActionAvailability();
            UpdateWindowTitle();
            UpdateToolbarContext();
            UpdateReaderNavigation();
        }
    }

    private async void OnRenameFileRequested() => await _browseView.PromptRenameCurrentFileAsync();

    private async void OnDeleteFileRequested() => await _browseView.SendCurrentFileToRecycleBinAsync();

    private async void OnUndoRequested() => await _browseView.UndoLastOperationAsync();

    private async void OnRedoRequested() => await _browseView.RedoLastOperationAsync();

    private async Task UpdateHistoryAvailabilityAsync()
    {
        var request = ++_historyRequest;
        _historyAvailabilityCancellation?.Cancel();
        _historyAvailabilityCancellation?.Dispose();
        _historyAvailabilityCancellation = new CancellationTokenSource();
        var token = _historyAvailabilityCancellation.Token;
        var root = _navigationState.ActiveRoot;
        ToolbarIsland.SetHistoryAvailability(false, false);
        if (root is null || PageContent.Content != _browseView || _browseView.IsReader) return;
        try
        {
            var operations = await _fileOrganizationService.ForRootAsync(root.Path, token);
            var undo = await operations.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo, token);
            var redo = await operations.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Redo, token);
            if (request != _historyRequest || !ReferenceEquals(root, _navigationState.ActiveRoot) || PageContent.Content != _browseView || _browseView.IsReader) return;
            ToolbarIsland.SetHistoryAvailability(undo is not null, redo is not null, undo, redo);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
                                           System.Security.SecurityException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(exception, "Could not update root operation history commands.");
            if (request == _historyRequest) ToolbarIsland.SetHistoryAvailability(false, false);
        }
    }

    private void UpdateFileActionAvailability()
    {
        ToolbarIsland.SetFileActionsEnabled(PageContent.Content == _browseView && !_browseView.IsReader && (_browseView.IsGallery ? _navigationState.GallerySelection.Count > 0 : _navigationState.CurrentFile is not null), _browseView.IsGallery, !_browseView.IsGallery || _navigationState.GallerySelection.Count == 1);
        ToolbarIsland.SetRefreshEnabled(PageContent.Content == _browseView && _navigationState.Roots.Count > 0);
    }

    private async Task RefreshBrowseAsync()
    {
        if (PageContent.Content == _browseView) await _browseView.RefreshAsync();
    }

    private void OnMainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!e.Handled) e.Handled = TryHandleViewShortcut(e.Key, Keyboard.Modifiers, e.IsRepeat);
    }

    private bool TryHandleViewShortcut(Key key, ModifierKeys modifiers, bool repeat = false)
    {
        if (!IsEnabled || OwnedWindows.OfType<Window>().Any(window => window.IsVisible)) return false;
        if (_browseView.IsGalleryMoveDragActive) return false; // Let the native drag loop own Escape/modifiers.
        Func<Task>? action = null;
        var pinTree = key == Key.F3 && modifiers == ModifierKeys.None;
        if (key == Key.F11 || IsFullscreen && key == Key.Escape)
            action = () => { ToggleFullscreen(); return Task.CompletedTask; };
        else if (PageContent.Content == _browseView)
        {
            if (ReaderIsActive && key == Key.G && modifiers == ModifierKeys.Control) action = PromptReaderJumpAsync;
            else if (ReaderIsActive && key == Key.Enter && modifiers == ModifierKeys.None) action = () => { _browseView.OpenReaderInBrowse(); return Task.CompletedTask; };
            else if (pinTree) action = () => { _browseView.ToggleFolderPinned(); return Task.CompletedTask; };
            else if (key == Key.F5 && modifiers == ModifierKeys.None) action = RefreshBrowseAsync;
            else if (key == Key.Z && modifiers == ModifierKeys.Control) action = _browseView.UndoLastOperationAsync;
            else if (key == Key.Y && modifiers == ModifierKeys.Control) action = _browseView.RedoLastOperationAsync;
            else if (key == Key.F2 && modifiers == ModifierKeys.None) action = _browseView.PromptRenameCurrentFileAsync;
            else if (key == Key.Delete && modifiers == ModifierKeys.None) action = _browseView.SendCurrentFileToRecycleBinAsync;
            else if (key == Key.N && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) action = _browseView.CreateSubfolderInActiveFolderAsync;
            else return _browseView.HandleViewShortcut(key, modifiers);
        }
        if (action is null) return false;
        if (!repeat)
        {
            var page = PageContent.Content; var mode = _navigationState.ViewingMode;
            var root = _navigationState.ActiveRoot; var folder = _navigationState.ActiveFolder;
            var current = _navigationState.CurrentFile;
            var selected = _navigationState.GallerySelection.GetSelectedFiles(_navigationState.Files);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsEnabled || OwnedWindows.OfType<Window>().Any(window => window.IsVisible)) return;
                if (key != Key.F11 && !(IsFullscreen && key == Key.Escape) &&
                    (page != PageContent.Content || mode != _navigationState.ViewingMode ||
                     pinTree && _browseView.IsGalleryMoveDragActive || !pinTree && (!ReferenceEquals(root, _navigationState.ActiveRoot) ||
                     !ReferenceEquals(folder, _navigationState.ActiveFolder) || !ReferenceEquals(current, _navigationState.CurrentFile) ||
                     !selected.SequenceEqual(_navigationState.GallerySelection.GetSelectedFiles(_navigationState.Files))))) return;
                _ = RunViewShortcutAsync(action);
            }));
        }
        return true;
    }

    private async Task RunViewShortcutAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) { _logger.LogWarning(exception, "View shortcut failed."); }
    }

    private void UpdateToolbarContext()
    {
        var folder = _navigationState.ActiveFolder;
        ToolbarIsland.SetFolderContext(folder?.Name ?? "MediFiler3", folder?.Path ?? "No folder loaded");
        var metadata = PageContent.Content == _browseView && _browseView.IsBrowse && _navigationState.CurrentFile is { } file
            ? _browseView.GetViewerMetadata(file) : null;
        ToolbarIsland.SetFileMetadata(ViewerMetadataFormatter.Format(metadata));
    }

    private void UpdateViewerControls()
    {
        UpdateReaderNavigation();
        ToolbarIsland.SetReaderControls(PageContent.Content == _browseView && _browseView.IsReader, _navigationState.Reader.Options);
        ToolbarIsland.SetGalleryControls(PageContent.Content == _browseView && _browseView.IsGallery, _galleryWidth, _galleryHeight);
        ToolbarIsland.SetViewerCapabilities(
        PageContent.Content == _browseView && _browseView.IsBrowse && _navigationState.CurrentFile is not null
            ? _browseView.ViewerCapabilities : MediFiler3.Application.Viewing.ViewerCapabilities.None, _browseView.CanZoom);
        var external = _browseView.ExternalOpenState;
        ToolbarIsland.SetExternalOpenState(PageContent.Content == _browseView && _browseView.IsBrowse ? external : external with { Visible = false });
        ToolbarIsland.SetTextViewerState(_browseView.IsTextInteraction,
            _browseView.CanReadPreviousTextWindow, _browseView.CanReadNextTextWindow);
        ToolbarIsland.SetMediaPlaybackState(_browseView.PlaybackState);
    }

    private async void OnLoadFoldersRequested()
    {
        await OnLoadFoldersClick(this, new RoutedEventArgs());
    }

    private void UpdateWindowTitle()
    {
        var currentFile = _navigationState.CurrentFile;
        Title = currentFile is null || _navigationState.CurrentFilePosition == 0
            ? "MediFiler3"
            : $"({_navigationState.CurrentFilePosition}/{_navigationState.Files.Count}) {currentFile.Name}";
    }

    private async Task OnLoadFoldersClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Load folders", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        await LoadFolderPathsAsync(dialog.FolderNames);
    }

    private void OnWindowDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        var folders = paths.Where(Directory.Exists).ToArray();
        var files = paths.Length - folders.Length;
        if (files > 0)
        {
            _logger.LogInformation("Ignored {FileCount} dropped file(s); only folders are loaded.", files);
        }

        if (folders.Length > 0)
        {
            await LoadFolderPathsAsync(folders);
        }
    }

    private async Task LoadFolderPathsAsync(IEnumerable<string> folderPaths)
    {
        _browseView.SetViewingMode(ViewingMode.Browse);
        PageContent.Content = _browseView;
        _browseView.SetViewerActive(true);
        NavigationIsland.SelectDestination("Browse");
        try
        {
            await _browseView.LoadRootsAsync(folderPaths);
        }
        catch (OperationCanceledException)
        {
            // A subsequent folder load replaced this one.
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, uint attribute, ref uint value, uint valueSize);
}
