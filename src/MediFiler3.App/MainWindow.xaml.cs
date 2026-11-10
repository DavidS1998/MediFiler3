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
        IFileOrganizationService fileOrganizationService, IFileOperationJournal operationJournal, ITextContentService textContentService, IExternalFileService externalFileService, IDefaultApplicationService defaultApplications, ApplicationSettingsService settings, RootFolderHistoryService rootHistory, IRootFolderAvailabilityService rootAvailability, IFileCollisionPreviewService collisionPreviews, IOperationBackupMaintenance backupMaintenance, WindowLayoutService windowLayout, IWindowPlacementPlatform windowPlacement)
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
            new MediaAudioSettings(settings.Current.Volume, settings.Current.Muted), rootHistory);
        _browseView.RootHistoryPersistenceFailed += exception => _logger.LogWarning(exception, "Could not save root-folder history.");
        _browseView.SetCarouselNeighborCount((settings.Current.CarouselIconCount - 1) / 2);
        _browseView.SetFolderPinned(settings.Current.FolderTreePinned);
        _browseView.AudioSettingsChanged += (volume, muted) => settings.Update(s => s with { Volume = volume, Muted = muted });
        _browseView.FolderPinChanged += pinned => settings.Update(s => s with { FolderTreePinned = pinned });
        Closed += (_, _) => { _historyAvailabilityCancellation?.Cancel(); _homeView.Stop(); _browseView.StopBackgroundFolderCounts(); };
        _settingsView = new SettingsView(_browseView.CarouselNeighborCount);
        _settingsView.CarouselNeighborCountChanged += count =>
        {
            _browseView.SetCarouselNeighborCount(count);
            settings.Update(s => s with { CarouselIconCount = count * 2 + 1 });
        };
        InitializeComponent();
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
            ToolbarIsland.ShowFolderMenu(location, folder.Name,
                action => _browseView.ExecuteFolderMenuActionAsync(folder, action));
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

        Sidebar.Visibility = IsSidebarVisible ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = IsSidebarVisible ? new GridLength(CompactSidebarWidth) : new GridLength(0);
    }

    private void OnNavigationSelectionChanged(string destination)
    {
        PageContent.Content = destination switch
        {
            "Browse" => _browseView,
            "Settings" => _settingsView,
            _ => _homeView
        };
        _browseView.SetViewerActive(destination == "Browse");
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
        if (root is null || PageContent.Content != _browseView) return;
        try
        {
            var operations = await _fileOrganizationService.ForRootAsync(root.Path, token);
            var undo = await operations.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo, token);
            var redo = await operations.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Redo, token);
            if (request != _historyRequest || !ReferenceEquals(root, _navigationState.ActiveRoot) || PageContent.Content != _browseView) return;
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
        ToolbarIsland.SetFileActionsEnabled(PageContent.Content == _browseView && _navigationState.CurrentFile is not null);
        ToolbarIsland.SetRefreshEnabled(PageContent.Content == _browseView && _navigationState.Roots.Count > 0);
    }

    private async Task RefreshBrowseAsync()
    {
        if (PageContent.Content == _browseView) await _browseView.RefreshAsync();
    }

    private async void OnMainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (PageContent.Content == _browseView && e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await RefreshBrowseAsync();
            return;
        }
        if (PageContent.Content != _browseView || Keyboard.FocusedElement is TextBox or MenuItem)
            return;

        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            await _browseView.UndoLastOperationAsync();
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            await _browseView.RedoLastOperationAsync();
        }
        else if (e.Key == Key.F2)
        {
            e.Handled = true;
            await _browseView.PromptRenameCurrentFileAsync();
        }
        else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await _browseView.SendCurrentFileToRecycleBinAsync();
        }
        else if (e.Key == Key.N && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            await _browseView.CreateSubfolderInActiveFolderAsync();
        }
    }

    private void UpdateToolbarContext()
    {
        var folder = _navigationState.ActiveFolder;
        ToolbarIsland.SetFolderContext(folder?.Name ?? "MediFiler3", folder?.Path ?? "No folder loaded");
        var metadata = PageContent.Content == _browseView && _navigationState.CurrentFile is { } file
            ? _browseView.GetViewerMetadata(file) : null;
        ToolbarIsland.SetFileMetadata(ViewerMetadataFormatter.Format(metadata));
    }

    private void UpdateViewerControls()
    {
        ToolbarIsland.SetViewerCapabilities(
        PageContent.Content == _browseView && _navigationState.CurrentFile is not null
            ? _browseView.ViewerCapabilities : MediFiler3.Application.Viewing.ViewerCapabilities.None, _browseView.CanZoom);
        var external = _browseView.ExternalOpenState;
        ToolbarIsland.SetExternalOpenState(PageContent.Content == _browseView ? external : external with { Visible = false });
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
