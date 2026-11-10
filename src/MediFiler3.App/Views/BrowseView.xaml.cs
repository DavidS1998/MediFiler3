using System.Windows.Controls;
using System.Windows;
using System.IO;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using MediFiler3.Application.Files;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Operations;
using MediFiler3.Domain.Files;
using MediFiler3.Interop;
using MediFiler3.Services;
using MediFiler3.Application.Viewing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;
using System.Diagnostics;
using System.Windows.Input;

namespace MediFiler3.Views;

public partial class BrowseView : UserControl
{
    private readonly RootFolderHistoryService? _rootHistory;
    public event Action<Exception>? RootHistoryPersistenceFailed;
    private readonly IFolderTreeService _folderTreeService;
    private readonly IFileListingService _fileListingService;
    private readonly NavigationSessionState _navigationState;
    private readonly NavigationController _navigationController;
    private readonly IThumbnailService _thumbnailService;
    private readonly IFileOrganizationService _fileOrganizationService;
    public IWinUiDialogService? DialogService { get; set; }
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _fileLoadCancellation;
    private CancellationTokenSource? _carouselLoadCancellation;
    private CancellationTokenSource? _countCancellation;
    private bool _folderOverlayVisible;
    private bool _folderOverlayPinned;
    private bool _carouselOverlayVisible;
    private bool _carouselPanelAnimating;
    private CarouselSpacerItem? _carouselLeadingSpacer;
    private CarouselSpacerItem? _carouselTrailingSpacer;
    private readonly DispatcherTimer _carouselScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTime _carouselScrollStartedAt;
    private double _carouselScrollStartOffset;
    private double _carouselScrollTargetOffset;
    private double _carouselOffsetBeforeRefresh;
    private ScrollViewer? _carouselScrollViewer;
    private int _carouselStartIndex;
    private int _carouselEndIndex;
    private int _carouselFileCount;
    private int _carouselCurrentIndex;
    private int _carouselRefreshVersion;
    private int _carouselWheelDeltaRemainder;
    private bool _hasCenteredCarouselItem;
    private int _carouselNeighborsOnEachSide = 5;
    private readonly DispatcherTimer _operationNoticeTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    public event Action<string>? StatusChanged;
    public event Action<FolderTreeEntry, Point>? FolderMenuRequested;
    public event Action? OperationHistoryChanged;
    public ObservableCollection<object> CarouselItems { get; } = [];
    public double CurrentZoomLevel => ViewerHost.CurrentZoomLevel;
    public ExternalOpenState ExternalOpenState => ViewerHost.ExternalOpenState;
    public Task OpenExternallyAsync(ExternalFileAction action) => ViewerHost.OpenExternallyAsync(action);
    public MediaPlaybackState PlaybackState => ViewerHost.PlaybackState;
    public event Action? PlaybackStateChanged;
    public event Action<double, bool>? AudioSettingsChanged;
    public event Action<bool>? FolderPinChanged;
    public event Action? RefreshRequested;
    public void SetFolderPinned(bool pinned)
    {
        FolderPinButton.IsChecked = pinned;
        ApplyFolderPin();
    }
    public void TogglePlayback() => ViewerHost.TogglePlayback();
    public void StopPlayback() => ViewerHost.StopPlayback();
    public void SeekPlayback(double seconds) => ViewerHost.SeekPlayback(seconds);
    public void SetPlaybackVolume(double volume) => ViewerHost.SetPlaybackVolume(volume);
    public void TogglePlaybackMute() => ViewerHost.TogglePlaybackMute();
    public void JumpPlayback(double seconds) => ViewerHost.JumpPlayback(seconds);
    public bool CanZoom => ViewerHost.CanZoom;
    public bool IsTextInteraction => ViewerHost.IsTextInteraction;
    public bool CanReadPreviousTextWindow => ViewerHost.CanReadPreviousTextWindow;
    public bool CanReadNextTextWindow => ViewerHost.CanReadNextTextWindow;
    public void ToggleTextInteraction() => ViewerHost.ToggleTextInteraction();
    public Task ReadTextWindowAsync(bool next) => ViewerHost.ReadTextWindowAsync(next);
    public ViewerCapabilities ViewerCapabilities => ViewerHost.Capabilities;
    public ViewerMetadata? ViewerMetadata => ViewerHost.Metadata;
    public ViewerMetadata GetViewerMetadata(FileEntry file) => ViewerHost.GetMetadataFor(file);
    public event Action? PresentationChanged;
    public void ZoomInFromToolbar() => ViewerHost.ZoomInFromToolbar();
    public void ZoomOutFromToolbar() => ViewerHost.ZoomOutFromToolbar();
    public void SetCarouselNeighborCount(int count)
    {
        var boundedCount = Math.Clamp(count, 0, 12);
        if (_carouselNeighborsOnEachSide == boundedCount)
        {
            return;
        }

        _carouselNeighborsOnEachSide = boundedCount;
        _hasCenteredCarouselItem = false;
        _ = RefreshCarouselAsync();
    }

    public int CarouselNeighborCount => _carouselNeighborsOnEachSide;
    public event Action<double>? ZoomLevelChanged;

    public BrowseView(IFolderTreeService folderTreeService, IFileListingService fileListingService, NavigationSessionState navigationState,
        WicImageDecoder imageDecoder, IThumbnailService thumbnailService, IFileOrganizationService fileOrganizationService, ITextContentService textContentService, IExternalFileService externalFileService, IDefaultApplicationService defaultApplications, MediaAudioSettings? audioSettings = null, RootFolderHistoryService? rootHistory = null)
    {
        _rootHistory = rootHistory;
        _folderTreeService = folderTreeService;
        _fileListingService = fileListingService;
        _navigationState = navigationState;
        _navigationController = new NavigationController(navigationState, folder => ActivateFolderAsync(folder));
        _navigationController.FileSelected += PresentSelectedFile;
        _navigationController.StatusChanged += message => StatusChanged?.Invoke(message);
        _thumbnailService = thumbnailService;
        _fileOrganizationService = fileOrganizationService;
        InitializeComponent();
        if (audioSettings is not null) ViewerHost.InitializeAudioSettings(audioSettings);
        ViewerHost.AudioSettingsChanged += (volume, muted) => AudioSettingsChanged?.Invoke(volume, muted);
        ViewerHost.RefreshRequested += () => RefreshRequested?.Invoke();
        ViewerHost.Initialize(imageDecoder, thumbnailService, textContentService, externalFileService, defaultApplications);
        ViewerHost.NavigationRequested += NavigateByOneFile;
        CompositionTarget.Rendering += OnVideoSurfaceRendering;
        ViewerHost.PlaybackStateChanged += () => PlaybackStateChanged?.Invoke();
        ViewerHost.ZoomLevelChanged += value => ZoomLevelChanged?.Invoke(value);
        ViewerHost.PresentationChanged += () => PresentationChanged?.Invoke();

        DataContext = navigationState;
        CarouselList.ItemsSource = CarouselItems;
        _carouselScrollTimer.Tick += OnCarouselScrollTimerTick;
        _operationNoticeTimer.Tick += (_, _) =>
        {
            _operationNoticeTimer.Stop();
            OperationNotice.Visibility = Visibility.Collapsed;
        };
    }

    public async Task LoadRootsAsync(IEnumerable<string> paths)
    {
        _navigationController.CancelPendingNavigation();
        CancelFolderCounts();
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;
        _fileLoadCancellation?.Cancel();
        ClearActivePresentation("Loading folder hierarchy...", string.Empty);
        _navigationState.ReplaceRoots([]);
        var requestedRoots = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requestedRoots.Length == 0)
        {
            StatusChanged?.Invoke("No folders selected.");
            return;
        }

        StatusChanged?.Invoke("Loading folder hierarchy...");
        try
        {
            var result = await _folderTreeService.LoadRootsAsync(requestedRoots, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _navigationState.ReplaceRoots(result.Roots);
            _ = PopulateHierarchyAsync(result.Roots, cancellationToken);

            if (result.Errors.Count > 0)
            {
                StatusChanged?.Invoke($"Loaded {result.Roots.Count} root(s); {result.Errors.Count} folder(s) could not be accessed.");
                ShowOperationNotice($"Some folders could not be loaded: {string.Join("; ", result.Errors.Take(3))}");
            }
            else
            {
                StatusChanged?.Invoke($"Loaded {result.Roots.Count} folder root(s).");
            }

            if (result.Roots.FirstOrDefault() is { } firstRoot)
            {
                _ = ActivateFolderAsync(firstRoot);
            }
            else
            {
                ViewerHost.ShowImageMessage("No accessible folders were loaded.", string.Empty);
            }
            if (_rootHistory is not null && result.Roots.Count > 0)
            {
                try
                {
                    // Roots have been published successfully. Count every distinct root once,
                    // regardless of which UI entry point initiated loading. Refresh never calls this.
                    await _rootHistory.RecordOpenedRootsAsync(result.Roots.Select(root => root.Path));
                }
                catch (Exception exception)
                {
                    RootHistoryPersistenceFailed?.Invoke(exception);
                    if (!cancellationToken.IsCancellationRequested)
                        ShowOperationNotice("Folders loaded, but their visit history could not be saved.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer context replaced this load.
        }
    }

    private async void OnFolderSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderTreeEntry folder)
        {
            if (!ReferenceEquals(folder, _navigationState.ActiveFolder)) _navigationController.CancelPendingNavigation();
            await ActivateFolderAsync(folder);
        }
    }

    private async Task ActivateFolderAsync(FolderTreeEntry folder, string? preferredFilePath = null, int preferredFileIndex = 0)
    {
        if (ReferenceEquals(_navigationState.ActiveFolder, folder))
        {
            return;
        }

        _fileLoadCancellation?.Cancel();
        _fileLoadCancellation?.Dispose();
        _fileLoadCancellation = new CancellationTokenSource();
        var cancellationToken = _fileLoadCancellation.Token;
        _navigationState.SetActiveFolder(folder);
        ClearActivePresentation($"Loading files in {folder.Name}...", folder.Path);
        StatusChanged?.Invoke($"Loading files in {folder.Name}...");

        try
        {
            var result = await _fileListingService.GetFilesAsync(folder.Path, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_navigationState.ActiveFolder, folder))
            {
                return;
            }

            _navigationState.ReplaceFiles(result.Files);
            UpdateFolderFileCounts(folder.Path, result.Files.Count);
            if (preferredFilePath is not null)
            {
                var preferredFile = result.Files.FirstOrDefault(file => PathsEqual(file.FullPath, preferredFilePath));
                if (preferredFile is not null) _navigationState.SetCurrentFile(preferredFile);
                else if (result.Files.Count > 0)
                    _navigationState.SetCurrentFile(result.Files[Math.Clamp(preferredFileIndex, 0, result.Files.Count - 1)]);
            }
            EmptyFilesNotice.Visibility = result.Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _ = RefreshCarouselAsync();
            _ = LoadCurrentImageAsync(clearCurrentImage: true);
            StatusChanged?.Invoke(result.Errors.Count == 0
                ? $"{result.Files.Count} file(s) in {folder.Name}."
                : $"Loaded {result.Files.Count} file(s); {result.Errors.Count} item(s) could not be read.");
            if (result.Errors.Count > 0)
            {
                var errorSample = result.Errors.Take(5).ToList();
                if (result.Errors.Count > errorSample.Count)
                {
                    errorSample.Add($"...and {result.Errors.Count - errorSample.Count} more.");
                }

                ShowOperationNotice($"Some files could not be read: {string.Join("; ", errorSample)}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer folder selection replaced this listing.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (ReferenceEquals(_navigationState.ActiveFolder, folder))
            {
                _navigationState.ReplaceFiles([]);
                EmptyFilesNotice.Visibility = Visibility.Visible;
                StatusChanged?.Invoke($"Could not read files in {folder.Name}.");
                ShowOperationNotice($"Folder could not be read: {exception.Message}");
            }
        }
    }

    private void OnFolderMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: FolderTreeEntry folder } button) return;
        e.Handled = true;
        RequestFolderMenu(button, folder);
    }

    private void OnFolderMenuButtonPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: FolderTreeEntry folder } button) return;
        e.Handled = true;
        RequestFolderMenu(button, folder);
    }

    private void RequestFolderMenu(Button button, FolderTreeEntry folder)
    {
        var location = button.PointToScreen(new Point(button.ActualWidth, 0));
        FolderMenuRequested?.Invoke(folder, location);
    }

    public async Task ExecuteFolderMenuActionAsync(FolderTreeEntry folder, string action)
    {
        try
        {
            switch (action)
            {
                case "new-folder": await CreateSubfolderAsync(folder); break;
                case "rename-folder": await RenameFolderAsync(folder); break;
                case "move-folder": await MoveFolderAsync(folder); break;
                case "recycle-folder": await DeleteFolderAsync(folder); break;
                case "refresh-tree": await RefreshFolderTreeFromMenuAsync(folder); break;
                case "open-explorer": await OpenFolderInExplorerAsync(folder); break;
                case "properties": await ShowFolderPropertiesAsync(folder); break;
                default: throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown folder menu action.");
            }
        }
        catch (Exception exception) when (IsOperationUiException(exception))
        {
            ShowOperationNotice(exception.Message);
        }
    }

    private async void OnFolderTreePreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not FolderTreeEntry destination) return;
        e.Handled = true;
        await MoveCurrentFileToFolderAsync(destination);
    }

    private async Task CreateSubfolderAsync(FolderTreeEntry parent)
    {
        var root = _navigationState.GetRootForFolder(parent);
        var name = await RequireDialogService().PromptTextAsync(
            "New folder", $"Create a folder inside {parent.Name}.", "New folder", "Create");
        if (string.IsNullOrWhiteSpace(name)) return;

        await RunFolderOperationAsync(
            async () => await (await GetRootOperationsAsync(root)).CreateFolderAsync(parent.Path, name),
            preferredFolderPath: _navigationState.ActiveFolder?.Path);
    }

    private IWinUiDialogService RequireDialogService() =>
        DialogService ?? throw new InvalidOperationException("The WinUI dialog service has not been initialized.");

    private async Task RenameFolderAsync(FolderTreeEntry folder)
    {
        var root = _navigationState.GetRootForFolder(folder);
        var name = await RequireDialogService().PromptTextAsync(
            "Rename folder", "Enter the folder's new name.", folder.Name, "Rename");
        if (string.IsNullOrWhiteSpace(name)) return;
        var destinationPath = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder.Path)) ?? "", name);
        var activePath = _navigationState.ActiveFolder?.Path;
        var mappedActivePath = MapDescendantPath(activePath, folder.Path, destinationPath);

        await RunFolderOperationAsync(
            async () => await (await GetRootOperationsAsync(root)).RenameFolderAsync(folder.Path, name),
            oldPath: folder.Path,
            newPath: destinationPath,
            preferredFolderPath: mappedActivePath);
    }

    private async Task MoveFolderAsync(FolderTreeEntry folder)
    {
        var root = _navigationState.GetRootForFolder(folder);
        var initialPath = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder.Path)) ?? folder.Path;
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = $"Move {folder.Name} to",
            InitialDirectory = initialPath,
            Multiselect = false
        };
        var destinationFolder = picker.ShowDialog(Window.GetWindow(this)) == true ? picker.FolderName : null;
        if (string.IsNullOrWhiteSpace(destinationFolder)) return;
        var destinationPath = Path.Combine(destinationFolder,
            Path.GetFileName(Path.TrimEndingDirectorySeparator(folder.Path)));
        var mappedActivePath = MapDescendantPath(_navigationState.ActiveFolder?.Path, folder.Path, destinationPath);

        await RunFolderOperationAsync(
            async () => await (await GetRootOperationsAsync(root)).MoveFolderAsync(folder.Path, destinationFolder),
            oldPath: folder.Path,
            newPath: destinationPath,
            preferredFolderPath: mappedActivePath);
    }

    private async Task DeleteFolderAsync(FolderTreeEntry folder)
    {
        var root = _navigationState.GetRootForFolder(folder);
        using var playbackRelease = ViewerHost.SuspendMediaForFileOperation();
        var activePath = _navigationState.ActiveFolder?.Path;
        var fallbackPath = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder.Path));
        var mappedActivePath = IsPathWithin(activePath, folder.Path) ? fallbackPath : activePath;
        var operations = await GetRootOperationsAsync(root);
        var result = await operations.SendToRecycleBinAsync(folder.Path, isDirectory: true);
        if (result.Outcome == FileOrganizationOutcome.ConfirmationRequired)
        {
            var confirmed = await RequireDialogService().ConfirmAsync(
                "Send folder to Recycle Bin",
                $"'{folder.Name}' contains files or subfolders. Send the folder and all its contents to the Windows Recycle Bin?\n\n{folder.Path}",
                "Send to Recycle Bin");
            if (!confirmed) return;
            result = await operations.SendToRecycleBinAsync(
                folder.Path, isDirectory: true, confirmNonEmptyDirectory: true);
        }

        await CompleteFolderOperationAsync(result, deletedPath: folder.Path, preferredFolderPath: mappedActivePath);
    }

    private async Task RefreshFolderTreeFromMenuAsync(FolderTreeEntry folder) =>
        await RefreshFolderTreeAsync(_navigationState.ActiveFolder?.Path ?? folder.Path);

    private Task OpenFolderInExplorerAsync(FolderTreeEntry folder)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder.Path}\"") { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private Task ShowFolderPropertiesAsync(FolderTreeEntry folder)
    {
        var owner = Window.GetWindow(this);
        var ownerHandle = owner is null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(owner).Handle;
        if (!SHObjectProperties(ownerHandle, ShopFilePath, folder.Path, null))
            throw new IOException($"Windows could not open properties for '{folder.Path}'.", Marshal.GetExceptionForHR(Marshal.GetLastWin32Error()));
        return Task.CompletedTask;
    }

    private const uint ShopFilePath = 2;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHObjectProperties(IntPtr hwnd, uint objectType, string objectName, string? propertyPage);

    private async Task MoveCurrentFileToFolderAsync(FolderTreeEntry destination)
    {
        var root = _navigationState.ActiveRoot;
        using var playbackRelease = ViewerHost.SuspendMediaForFileOperation();
        var currentFile = _navigationState.CurrentFile;
        if (currentFile is null)
        {
            ShowOperationNotice("Select a file before choosing a destination folder.");
            return;
        }

        var files = _navigationState.Files.ToArray();
        var index = Array.IndexOf(files, currentFile);
        var nextPath = index + 1 < files.Length ? files[index + 1].FullPath : index > 0 ? files[index - 1].FullPath : null;
        try
        {
            var result = await (await GetRootOperationsAsync(root, requireActive: true)).MoveFileAsync(currentFile.FullPath, destination.Path);
            if (result.OperationId is not null) OperationHistoryChanged?.Invoke();
            if (!result.Succeeded)
            {
                ShowOperationResult(result);
                return;
            }

            ShowOperationNotice($"Moved {currentFile.Name} to {destination.Name}.");
            await ReloadFilesInActiveFolderAsync(nextPath);
            var destinationListing = await _fileListingService.GetFilesAsync(destination.Path, CancellationToken.None);
            UpdateFolderFileCounts(destination.Path, destinationListing.Files.Count);
        }
        catch (Exception exception) when (IsOperationUiException(exception))
        {
            ShowOperationNotice($"Could not move file: {exception.Message}");
        }
    }

    public async Task PromptRenameCurrentFileAsync()
    {
        var root = _navigationState.ActiveRoot;
        using var playbackRelease = ViewerHost.SuspendMediaForFileOperation();
        var currentFile = _navigationState.CurrentFile;
        if (currentFile is null) return;
        var name = await RequireDialogService().PromptTextAsync(
            "Rename file", "Enter the file's new name.", currentFile.Name, "Rename");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var result = await (await GetRootOperationsAsync(root, requireActive: true)).RenameFileAsync(currentFile.FullPath, name);
            if (result.OperationId is not null) OperationHistoryChanged?.Invoke();
            if (!result.Succeeded)
            {
                ShowOperationResult(result);
                return;
            }

            ShowOperationNotice($"Renamed file to {name}.");
            await ReloadFilesInActiveFolderAsync(result.AffectedPath);
        }
        catch (Exception exception) when (IsOperationUiException(exception))
        {
            ShowOperationNotice($"Could not rename file: {exception.Message}");
        }
    }

    public async Task CreateSubfolderInActiveFolderAsync()
    {
        if (_navigationState.ActiveFolder is { } activeFolder)
            await CreateSubfolderAsync(activeFolder);
    }

    public async Task SendCurrentFileToRecycleBinAsync()
    {
        var root = _navigationState.ActiveRoot;
        using var playbackRelease = ViewerHost.SuspendMediaForFileOperation();
        var currentFile = _navigationState.CurrentFile;
        if (currentFile is null) return;
        var files = _navigationState.Files.ToArray();
        var index = Array.IndexOf(files, currentFile);
        var nextPath = index + 1 < files.Length ? files[index + 1].FullPath : index > 0 ? files[index - 1].FullPath : null;
        try
        {
            var result = await (await GetRootOperationsAsync(root, requireActive: true)).SendToRecycleBinAsync(currentFile.FullPath, isDirectory: false);
            if (result.OperationId is not null) OperationHistoryChanged?.Invoke();
            if (!result.Succeeded)
            {
                ShowOperationResult(result);
                return;
            }

            ShowOperationNotice($"Sent {currentFile.Name} to the Recycle Bin.");
            await ReloadFilesInActiveFolderAsync(nextPath);
        }
        catch (Exception exception) when (IsOperationUiException(exception))
        {
            ShowOperationNotice($"Could not send file to the Recycle Bin: {exception.Message}");
        }
    }

    public Task UndoLastOperationAsync() => ApplyHistoryOperationAsync(undo: true);

    public Task RedoLastOperationAsync() => ApplyHistoryOperationAsync(undo: false);

    private async Task ApplyHistoryOperationAsync(bool undo)
    {
        if (_historyBusy) return;
        using var playbackRelease = ViewerHost.SuspendMediaForFileOperation();
        var activePath = _navigationState.ActiveFolder?.Path;
        var root = _navigationState.ActiveRoot;
        if (root is null || !_viewerActive) return;
        using var cancellation = new CancellationTokenSource();
        _historyCancellation = cancellation;
        _historyBusy = true;
        void OnContextChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (!ReferenceEquals(root, _navigationState.ActiveRoot)) cancellation.Cancel();
        }
        _navigationState.PropertyChanged += OnContextChanged;
        try
        {
            var operations = await GetRootOperationsAsync(root, requireActive: true, cancellation.Token);
            var result = undo
                ? await operations.UndoAsync(cancellation.Token)
                : await operations.RedoAsync(cancellation.Token);
            if (!ReferenceEquals(root, _navigationState.ActiveRoot) || !_viewerActive) return;
            ShowOperationNotice(result.Message);
            if (result.SucceededCount > 0) OperationHistoryChanged?.Invoke();
            if (result.SucceededCount == 0) return;

            var pathMappings = result.Results.Where(item => item.Succeeded && item.PreviousPath is not null &&
                    item.AffectedPath is not null)
                .Select(item => (OldPath: item.PreviousPath!, NewPath: item.AffectedPath!)).ToArray();
            var preferredPath = activePath;
            foreach (var mapping in pathMappings)
                preferredPath = MapDescendantPath(preferredPath, mapping.OldPath, mapping.NewPath);
            if (!Directory.Exists(preferredPath))
            {
                preferredPath = result.Results.Select(item => item.AffectedPath)
                    .FirstOrDefault(path => path is not null && Directory.Exists(path));
            }
            while (!string.IsNullOrWhiteSpace(preferredPath) && !Directory.Exists(preferredPath))
                preferredPath = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(preferredPath));
            var preferredFilePath = result.Results.Select(item => item.AffectedPath)
                .FirstOrDefault(path => path is not null && File.Exists(path) &&
                    PathsEqual(Path.GetDirectoryName(path) ?? string.Empty, preferredPath ?? string.Empty));
            await RefreshFolderTreeAsync(preferredPath, pathMappings: pathMappings, preferredFilePath: preferredFilePath,
                mapPreferredFolderWithMappings: false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (IsOperationUiException(exception))
        {
            ShowOperationNotice($"Could not {(undo ? "undo" : "redo")} the operation: {exception.Message}");
        }
        finally { _navigationState.PropertyChanged -= OnContextChanged; _historyBusy = false; _historyCancellation = null; }
    }

    private async Task RunFolderOperationAsync(
        Func<Task<FileOrganizationResult>> operation,
        string? oldPath = null,
        string? newPath = null,
        string? deletedPath = null,
        string? preferredFolderPath = null)
    {
        using var playbackRelease = ViewerHost.SuspendMediaForFileOperation();
        try
        {
            var result = await operation();
            await CompleteFolderOperationAsync(result, oldPath, newPath, deletedPath, preferredFolderPath);
        }
        catch (Exception exception) when (IsOperationUiException(exception))
        {
            ShowOperationNotice($"Folder action failed: {exception.Message}");
        }
    }

    private async Task CompleteFolderOperationAsync(
        FileOrganizationResult result,
        string? oldPath = null,
        string? newPath = null,
        string? deletedPath = null,
        string? preferredFolderPath = null)
    {
        if (result.OperationId is not null) OperationHistoryChanged?.Invoke();
        if (!result.Succeeded)
        {
            ShowOperationResult(result);
            return;
        }

        ShowOperationNotice(result.Message);
        await RefreshFolderTreeAsync(preferredFolderPath, oldPath, newPath, deletedPath);
    }

    public Task RefreshAsync()
    {
        if (_navigationState.Roots.Count == 0) return Task.CompletedTask;
        return RefreshFolderTreeAsync(_navigationState.ActiveFolder?.Path,
            preferredFilePath: _navigationState.CurrentFile?.FullPath,
            preferredFileIndex: Math.Max(0, _navigationState.CurrentFilePosition - 1));
    }

    private async Task RefreshFolderTreeAsync(
        string? preferredFolderPath,
        string? oldPath = null,
        string? newPath = null,
        string? deletedPath = null,
        IReadOnlyList<(string OldPath, string NewPath)>? pathMappings = null,
        string? preferredFilePath = null,
        bool mapPreferredFolderWithMappings = true,
        int preferredFileIndex = 0)
    {
        _navigationController.CancelPendingNavigation();
        _fileLoadCancellation?.Cancel();
        CancelFolderCounts();
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;
        var roots = _navigationState.Roots
            .Select(root =>
            {
                var path = root.Path;
                if (oldPath is not null && PathsEqual(path, oldPath) && newPath is not null) path = newPath;
                if (pathMappings is not null)
                    foreach (var mapping in pathMappings)
                        path = MapDescendantPath(path, mapping.OldPath, mapping.NewPath) ?? path;
                return path;
            })
            .ToArray();
        try
        {
            var loaded = await _folderTreeService.LoadRootsAsync(roots, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (loaded.Errors.Count > 0)
                ShowOperationNotice($"Some folders could not be loaded: {string.Join("; ", loaded.Errors.Take(3))}");
            _navigationState.ReplaceRoots(loaded.Roots, preserveActiveContext: true);
            var preferred = preferredFolderPath;
            if (oldPath is not null && newPath is not null)
                preferred = MapDescendantPath(preferred, oldPath, newPath);
            if (mapPreferredFolderWithMappings && pathMappings is not null)
                foreach (var mapping in pathMappings)
                    preferred = MapDescendantPath(preferred, mapping.OldPath, mapping.NewPath);
            if (deletedPath is not null && IsPathWithin(preferred, deletedPath))
                preferred = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(deletedPath));

            _ = PopulateHierarchyAsync(loaded.Roots, cancellationToken, preferred, preferredFilePath, preferredFileIndex);

            var active = FindFolder(loaded.Roots, preferred);
            // Keep a nested folder active while its replacement node is discovered.
            // This avoids jumping to a root's first file during a refresh.
            if (active is null && preferred is not null && _navigationState.ActiveFolder is not null) return;
            active ??= loaded.Roots.FirstOrDefault();
            if (active is not null)
            {
                await ActivateFolderAsync(active, preferredFilePath, preferredFileIndex);
            }
            else
            {
                _navigationState.SetActiveFolder(null);
                ClearActivePresentation("No accessible folders are loaded.", string.Empty);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer tree load superseded this refresh.
        }
        catch (Exception exception) when (IsOperationUiException(exception))
        {
            ShowOperationNotice($"Folder tree refresh failed: {exception.Message}");
        }
    }

    private async Task ReloadFilesInActiveFolderAsync(string? preferredFilePath)
    {
        var folder = _navigationState.ActiveFolder;
        if (folder is null) return;
        try
        {
            var listing = await _fileListingService.GetFilesAsync(folder.Path, CancellationToken.None);
            UpdateFolderFileCounts(folder.Path, listing.Files.Count);
            _navigationState.ReplaceFiles(listing.Files);
            if (preferredFilePath is not null)
            {
                var preferred = listing.Files.FirstOrDefault(file => PathsEqual(file.FullPath, preferredFilePath));
                if (preferred is not null) _navigationState.SetCurrentFile(preferred);
            }
            EmptyFilesNotice.Visibility = listing.Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _ = RefreshCarouselAsync();
            _ = LoadCurrentImageAsync(clearCurrentImage: true);
        }
        catch (Exception exception) when (IsOperationUiException(exception))
        {
            ShowOperationNotice($"Folder refresh failed: {exception.Message}");
        }
    }

    private async Task PopulateHierarchyAsync(IReadOnlyList<FolderTreeEntry> roots, CancellationToken cancellationToken,
        string? preferredFolderPath = null, string? preferredFilePath = null, int preferredFileIndex = 0)
    {
        var errors = new List<string>();
        try
        {
            var initialFolder = _navigationState.ActiveFolder;
            var initialFile = _navigationState.CurrentFile;
            // Publish roots and allow a render before requesting descendants.
            await Dispatcher.Yield(DispatcherPriority.Background);
            await foreach (var result in _folderTreeService.LoadHierarchyAsync(roots, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var index = 0; index < result.Children.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    result.Parent.AddChild(result.Children[index]);
                    if (index % 32 == 31) await Dispatcher.Yield(DispatcherPriority.Background);
                }
                // Failed discovery leaves subtree totals unknown.
                if (result.Errors.Count == 0) result.Parent.CompleteChildrenLoad();
                errors.AddRange(result.Errors.Take(Math.Max(0, 3 - errors.Count)));
                if (preferredFolderPath is not null &&
                    (_navigationState.ActiveFolder is null || (ReferenceEquals(_navigationState.ActiveFolder, initialFolder) &&
                     ReferenceEquals(_navigationState.CurrentFile, initialFile))))
                {
                    var preferred = FindFolder(roots, preferredFolderPath);
                    if (preferred is not null)
                    {
                        await ActivateFolderAsync(preferred, preferredFilePath, preferredFileIndex);
                        preferredFolderPath = null;
                    }
                }
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (preferredFolderPath is not null && ReferenceEquals(_navigationState.ActiveFolder, initialFolder) &&
                ReferenceEquals(_navigationState.CurrentFile, initialFile))
            {
                var fallback = roots.FirstOrDefault();
                if (fallback is not null) await ActivateFolderAsync(fallback);
                else
                {
                    _navigationState.SetActiveFolder(null);
                    ClearActivePresentation("No accessible folders are loaded.", string.Empty);
                }
            }
            if (errors.Count > 0)
                ShowOperationNotice($"Some folders could not be loaded: {string.Join("; ", errors)}");
            StartFolderCounts();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A new load, refresh, or shutdown replaced this tree.
        }
        catch (Exception exception) when (IsOperationUiException(exception))
        {
            ShowOperationNotice($"Folder hierarchy loading failed: {exception.Message}");
        }
    }

    private void CancelFolderCounts()
    {
        _countCancellation?.Cancel();
        _countCancellation?.Dispose();
        _countCancellation = null;
    }

    private void StartFolderCounts()
    {
        CancelFolderCounts();
        _countCancellation = new CancellationTokenSource();
        _ = PopulateFolderCountsAsync(_countCancellation.Token);
    }

    private async Task PopulateFolderCountsAsync(CancellationToken cancellationToken)
    {
        // Capture versions on the UI thread. Newer folder listings/file operations win over scan results.
        var entries = EnumerateFoldersInTreeOrder(_navigationState.Roots)
            .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => group.Select(entry => (Entry: entry, Version: entry.CountVersion)).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        try
        {
            await foreach (var result in _folderTreeService.CountFilesAsync(entries.Keys, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Count is { } count)
                    foreach (var snapshot in entries[result.Path])
                        if (snapshot.Entry.CountVersion == snapshot.Version)
                            snapshot.Entry.UpdateDirectFileCount(count);
                errors.AddRange(result.Errors.Take(Math.Max(0, 3 - errors.Count)));
                // Do not flood the dispatcher; allow input/rendering between progressive updates.
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (errors.Count > 0)
                ShowOperationNotice($"Some folder counts could not be loaded: {string.Join("; ", errors)}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A new tree load or shutdown superseded this scan.
        }
    }

    public void StopBackgroundFolderCounts()
    {
        _historyCancellation?.Cancel();
        _viewerActive = false;
        CompositionTarget.Rendering -= OnVideoSurfaceRendering;
        _navigationController.CancelPendingNavigation();
        _loadCancellation?.Cancel();
        _fileLoadCancellation?.Cancel();
        CancelFolderCounts();
        ViewerHost.SetActive(false);
        ViewerHost.Clear();
    }

    private void UpdateFolderFileCounts(string folderPath, int count)
    {
        // A folder can appear beneath more than one loaded root.
        foreach (var entry in EnumerateFoldersInTreeOrder(_navigationState.Roots))
            if (PathsEqual(entry.Path, folderPath)) entry.UpdateDirectFileCount(count);
    }

    private void ShowOperationResult(FileOrganizationResult result)
    {
        var message = result.Outcome == FileOrganizationOutcome.RecoveryRequired
            ? $"Review needed. {result.Message}"
            : result.Message;
        ShowOperationNotice(message);
    }

    private void ShowOperationNotice(string message)
    {
        OperationNoticeText.Text = message;
        OperationNotice.Visibility = Visibility.Visible;
        _operationNoticeTimer.Stop();
        _operationNoticeTimer.Start();
        StatusChanged?.Invoke(message);
    }

    private static FolderTreeEntry? FindFolder(IEnumerable<FolderTreeEntry> folders, string? path)
    {
        if (path is null) return null;
        foreach (var folder in folders)
        {
            if (PathsEqual(folder.Path, path)) return folder;
            var child = FindFolder(folder.Children, path);
            if (child is not null) return child;
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T ancestor) return ancestor;
            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private static string? MapDescendantPath(string? path, string source, string destination)
    {
        if (path is null || !IsPathWithin(path, source)) return path;
        var relative = Path.GetRelativePath(source, path);
        return relative == "." ? destination : Path.Combine(destination, relative);
    }

    private static bool IsPathWithin(string? path, string root)
    {
        if (path is null) return false;
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return PathsEqual(normalizedPath, normalizedRoot) ||
               normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    private static bool IsOperationUiException(Exception exception) => exception is IOException or
        UnauthorizedAccessException or ArgumentException or System.Security.SecurityException or
        System.Runtime.InteropServices.ExternalException or InvalidOperationException or OperationCanceledException or
        System.Text.Json.JsonException;

    private void OnCarouselSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_carouselOverlayVisible)
        {
            RestoreCarouselSelection();
            return;
        }

        if (CarouselList.SelectedItem is not CarouselThumbnailItem selectedItem || ReferenceEquals(selectedItem.File, _navigationState.CurrentFile))
        {
            return;
        }

        SelectFile(selectedItem.File);
    }

    private void OnCarouselPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_carouselOverlayVisible || _carouselPanelAnimating)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        while (source is not null && !ReferenceEquals(source, CarouselList))
        {
            if (source is ListBoxItem { DataContext: CarouselThumbnailItem item })
            {
                e.Handled = true;
                if (!ReferenceEquals(item.File, _navigationState.CurrentFile))
                {
                    SelectFile(item.File);
                }

                return;
            }

            source = VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source);
        }
    }

    private void RestoreCarouselSelection()
    {
        var currentItem = CarouselItems.OfType<CarouselThumbnailItem>()
            .FirstOrDefault(item => ReferenceEquals(item.File, _navigationState.CurrentFile));
        if (currentItem is not null)
        {
            CarouselList.SelectedItem = currentItem;
        }
    }

    private void OnCarouselPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Delta == 0)
        {
            return;
        }

        _carouselWheelDeltaRemainder += e.Delta;
        var notches = _carouselWheelDeltaRemainder / 120;
        _carouselWheelDeltaRemainder %= 120;
        if (notches == 0)
        {
            return;
        }

        e.Handled = true;
        var direction = notches < 0 ? 1 : -1;
        for (var notch = 0; notch < Math.Abs(notches); notch++)
        {
            NavigateByOneFile(direction);
        }
    }

    private void OnBrowsePreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!ViewerHost.IsKeyboardFocusWithin && !CarouselList.IsKeyboardFocusWithin)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        var ownsInput = FindAncestor<System.Windows.Controls.Primitives.TextBoxBase>(source) is not null ||
            FindAncestor<System.Windows.Controls.Primitives.RangeBase>(source) is not null ||
            FindAncestor<ButtonBase>(source) is not null;
        if (ViewerHost.HandleKey(e)) return;
        if (!ViewerInputPolicy.CanNavigateWithArrow(ownsInput, ViewerHost.Capabilities,
                CarouselList.IsKeyboardFocusWithin ? ViewerInteraction.Navigation : ViewerHost.Interaction)) return;

        var direction = e.Key switch
        {
            System.Windows.Input.Key.Left or System.Windows.Input.Key.Up => -1,
            System.Windows.Input.Key.Right or System.Windows.Input.Key.Down => 1,
            _ => 0
        };

        if (direction == 0)
        {
            return;
        }

        e.Handled = true;
        NavigateByOneFile(direction);
    }

    private void OnBrowseSurfaceMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var point = e.GetPosition(BrowseSurface);
        var showFolder = _folderOverlayPinned || point.X <= 32 ||
                         (_folderOverlayVisible && point.X <= FolderOverlay.Width);
        var showCarousel = point.Y >= BrowseSurface.ActualHeight - 26 ||
                           (_carouselOverlayVisible && point.Y >= BrowseSurface.ActualHeight - CarouselOverlay.Height);
        SetFolderOverlayVisible(showFolder);
        SetCarouselOverlayVisible(showCarousel);
    }

    private void OnBrowseSurfaceMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        SetFolderOverlayVisible(_folderOverlayPinned);
        SetCarouselOverlayVisible(false);
    }

    private void OnFolderPinButtonClick(object sender, RoutedEventArgs e) => ApplyFolderPin();

    private void ApplyFolderPin()
    {
        _folderOverlayPinned = FolderPinButton.IsChecked == true;
        FolderPinChanged?.Invoke(_folderOverlayPinned);
        FolderPinGlyph.Text = _folderOverlayPinned ? "\uE77A" : "\uE718";
        FolderPinButton.ToolTip = _folderOverlayPinned ? "Unpin folder panel" : "Pin folder panel open";
        FolderPinButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,
            _folderOverlayPinned ? "Unpin folder panel" : "Pin folder panel open");
        SetFolderOverlayVisible(_folderOverlayPinned || _folderOverlayVisible);
    }

    private void OnVideoSurfaceRendering(object? sender, EventArgs args)
    {
        if (!ViewerHost.HasVideoSurface || !IsVisible) return;
        // Native HWNDs otherwise cover WPF overlays. Cut out only their visible regions, preserving video layout.
        var width = BrowseSurface.ActualWidth;
        var height = BrowseSurface.ActualHeight;
        if (width <= 0 || height <= 0) return;
        var folderWidth = Math.Clamp(FolderOverlay.ActualWidth + FolderOverlayTransform.X, 0, width);
        var carouselHeight = Math.Clamp(CarouselOverlay.ActualHeight - CarouselOverlayTransform.Y, 0, height);
        var exclusions = new List<Rect>
        {
            new(0, 0, Math.Max(26, folderWidth), height),
            new(0, Math.Max(0, height - Math.Max(26, carouselHeight)), width, Math.Max(26, carouselHeight))
        };
        if (OperationNotice.Visibility == Visibility.Visible)
            exclusions.Add(OperationNotice.TransformToAncestor(BrowseSurface).TransformBounds(new Rect(OperationNotice.RenderSize)));
        ViewerHost.SetVideoExclusions(exclusions);
    }

    private void OnBrowseSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCarouselSpacerWidths();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(CenterCurrentCarouselItem));
    }

    private void SetFolderOverlayVisible(bool isVisible)
    {
        isVisible |= _folderOverlayPinned;
        if (_folderOverlayVisible == isVisible)
        {
            return;
        }

        _folderOverlayVisible = isVisible;
        FolderOverlay.IsHitTestVisible = isVisible;
        FolderOverlayTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            To = isVisible ? 0 : -FolderOverlay.Width,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void SetCarouselOverlayVisible(bool isVisible)
    {
        if (_carouselOverlayVisible == isVisible)
        {
            return;
        }

        _carouselOverlayVisible = isVisible;
        _carouselPanelAnimating = true;
        CarouselOverlay.IsHitTestVisible = false;
        CarouselList.IsHitTestVisible = false;
        var animation = new DoubleAnimation
        {
            To = isVisible ? 0 : CarouselOverlay.Height * 0.875,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CarouselOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            To = isVisible ? 1 : 0.65,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        animation.Completed += (_, _) =>
        {
            if (_carouselOverlayVisible == isVisible)
            {
                _carouselPanelAnimating = false;
                CarouselOverlay.IsHitTestVisible = isVisible;
                CarouselList.IsHitTestVisible = isVisible;
            }
        };

        CarouselOverlayTransform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private static IEnumerable<FolderTreeEntry> EnumerateFoldersInTreeOrder(IEnumerable<FolderTreeEntry> folders)
    {
        foreach (var folder in folders)
        {
            yield return folder;
            foreach (var child in EnumerateFoldersInTreeOrder(folder.Children))
            {
                yield return child;
            }
        }
    }

    private void SelectFile(FileEntry file) => _navigationController.SelectFile(file);
    private void NavigateByOneFile(int direction) => _navigationController.NavigateByOneFile(direction);
    private void ResetBoundaryAttempts() => _navigationController.ResetBoundaryAttempts();

    private void PresentSelectedFile(FileEntry file)
    {
        var item = CarouselItems.OfType<CarouselThumbnailItem>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.File, file));
        ViewerHost.ResetZoomState();
        ResetBoundaryAttempts();
        _ = RefreshCarouselAsync();
        _ = LoadCurrentImageAsync(clearCurrentImage: true, immediatePreview: item?.UncroppedThumbnail);
    }

    private async Task RefreshCarouselAsync()
    {
        var refreshVersion = ++_carouselRefreshVersion;
        if (_carouselLoadCancellation is null || _carouselLoadCancellation.IsCancellationRequested)
        {
            _carouselLoadCancellation?.Dispose();
            _carouselLoadCancellation = new CancellationTokenSource();
        }

        var cancellationToken = _carouselLoadCancellation.Token;
        _carouselOffsetBeforeRefresh = FindVisualChild<ScrollViewer>(CarouselList)?.HorizontalOffset ?? 0;

        var files = _navigationState.Files.ToArray();
        var currentFile = _navigationState.CurrentFile;
        var currentIndex = currentFile is null ? -1 : _navigationState.Files.IndexOf(currentFile);
        if (currentIndex < 0)
        {
            _carouselLeadingSpacer = null;
            _carouselTrailingSpacer = null;
            return;
        }

        _carouselLeadingSpacer ??= new CarouselSpacerItem(0);
        _carouselTrailingSpacer ??= new CarouselSpacerItem(0);
        if (CarouselItems.Count == 0 || !ReferenceEquals(CarouselItems[0], _carouselLeadingSpacer))
        {
            CarouselItems.Insert(0, _carouselLeadingSpacer);
        }

        if (!CarouselItems.Contains(_carouselTrailingSpacer))
        {
            CarouselItems.Add(_carouselTrailingSpacer);
        }

        const int thumbnailSize = 150;
        var start = Math.Max(0, currentIndex - _carouselNeighborsOnEachSide);
        var end = Math.Min(files.Length - 1, currentIndex + _carouselNeighborsOnEachSide);
        _carouselStartIndex = start;
        _carouselEndIndex = end;
        _carouselFileCount = files.Length;
        _carouselCurrentIndex = currentIndex;
        var visibleItems = new List<CarouselThumbnailItem>(end - start + 1);
        var existingItems = new Dictionary<FileEntry, CarouselThumbnailItem>(ReferenceEqualityComparer.Instance);
        foreach (var existingItem in CarouselItems.OfType<CarouselThumbnailItem>())
        {
            existingItems[existingItem.File] = existingItem;
        }

        var desiredFiles = new HashSet<FileEntry>(ReferenceEqualityComparer.Instance);
        for (var index = start; index <= end; index++)
        {
            desiredFiles.Add(files[index]);
        }
        for (var index = CarouselItems.Count - 2; index >= 1; index--)
        {
            if (CarouselItems[index] is CarouselThumbnailItem item && !desiredFiles.Contains(item.File))
            {
                CarouselItems.RemoveAt(index);
            }
        }

        for (var index = start; index <= end; index++)
        {
            var file = files[index];
            var item = existingItems.TryGetValue(file, out var existingItem)
                ? existingItem
                : new CarouselThumbnailItem(file, index == currentIndex);
            item.SetCurrent(index == currentIndex);
            var destinationIndex = index - start + 1;
            var currentItemIndex = CarouselItems.IndexOf(item);
            if (currentItemIndex < 0)
            {
                CarouselItems.Insert(destinationIndex, item);
            }
            else if (currentItemIndex != destinationIndex)
            {
                CarouselItems.RemoveAt(currentItemIndex);
                CarouselItems.Insert(destinationIndex, item);
            }

            visibleItems.Add(item);
        }

        var trailingIndex = CarouselItems.IndexOf(_carouselTrailingSpacer);
        if (trailingIndex != CarouselItems.Count - 1)
        {
            CarouselItems.RemoveAt(trailingIndex);
            CarouselItems.Add(_carouselTrailingSpacer);
        }

        UpdateCarouselSpacerWidths();

        var currentItem = visibleItems[currentIndex - start];
        CarouselList.SelectedItem = currentItem;
        if (refreshVersion != _carouselRefreshVersion)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            if (refreshVersion != _carouselRefreshVersion)
            {
                return;
            }

            _carouselScrollViewer = FindVisualChild<ScrollViewer>(CarouselList);
            if (_carouselScrollViewer is not null)
            {
                _carouselScrollViewer.ScrollToHorizontalOffset(
                    Math.Clamp(_carouselOffsetBeforeRefresh, 0, _carouselScrollViewer.ScrollableWidth));
            }

            CenterCurrentCarouselItem();
        }, DispatcherPriority.Loaded);

        if (refreshVersion != _carouselRefreshVersion)
        {
            return;
        }

        var loads = visibleItems
            .Where(item => item.Thumbnail is null && (item.ThumbnailTask is null || item.ThumbnailTask.IsCompleted))
            .Select(item => item.ThumbnailTask = LoadCarouselThumbnailAsync(item, thumbnailSize, cancellationToken));

        await Task.WhenAll(loads);
    }

    private void UpdateCarouselSpacerWidths()
    {
        const double carouselItemWidth = 156;
        var sideSpace = Math.Max(0, (CarouselList.ActualWidth - carouselItemWidth) / 2);
        if (_carouselLeadingSpacer is not null)
        {
            _carouselLeadingSpacer.Width = sideSpace + _carouselStartIndex * carouselItemWidth;
        }

        if (_carouselTrailingSpacer is not null)
        {
            _carouselTrailingSpacer.Width = sideSpace + Math.Max(0, _carouselFileCount - 1 - _carouselEndIndex) * carouselItemWidth;
        }
    }

    private void CenterCurrentCarouselItem()
    {
        if (CarouselList.SelectedItem is not CarouselThumbnailItem selectedItem ||
            CarouselList.ItemContainerGenerator.ContainerFromItem(selectedItem) is not ListBoxItem container ||
            (_carouselScrollViewer ??= FindVisualChild<ScrollViewer>(CarouselList)) is not { } scrollViewer)
        {
            return;
        }

        const double carouselItemWidth = 156;
        var targetOffset = _carouselCurrentIndex * carouselItemWidth;
        targetOffset = Math.Clamp(targetOffset, 0, scrollViewer.ScrollableWidth);
        if (!_hasCenteredCarouselItem || Math.Abs(targetOffset - scrollViewer.HorizontalOffset) < 1)
        {
            _carouselScrollTimer.Stop();
            scrollViewer.ScrollToHorizontalOffset(targetOffset);
            _hasCenteredCarouselItem = true;
            return;
        }

        _carouselScrollTimer.Stop();
        _carouselScrollStartOffset = scrollViewer.HorizontalOffset;
        _carouselScrollTargetOffset = targetOffset;
        _carouselScrollStartedAt = DateTime.UtcNow;
        _carouselScrollTimer.Start();
    }

    private void OnCarouselScrollTimerTick(object? sender, EventArgs e)
    {
        if (_carouselScrollViewer is null)
        {
            _carouselScrollTimer.Stop();
            return;
        }

        var progress = Math.Clamp((DateTime.UtcNow - _carouselScrollStartedAt).TotalMilliseconds / 180d, 0d, 1d);
        var easedProgress = 1d - Math.Pow(1d - progress, 3d);
        var offset = _carouselScrollStartOffset + (_carouselScrollTargetOffset - _carouselScrollStartOffset) * easedProgress;
        _carouselScrollViewer.ScrollToHorizontalOffset(offset);
        if (progress >= 1d)
        {
            _carouselScrollTimer.Stop();
            _carouselScrollViewer.ScrollToHorizontalOffset(_carouselScrollTargetOffset);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private async Task<BitmapSource?> LoadCarouselThumbnailAsync(CarouselThumbnailItem item, int thumbnailSize,
        CancellationToken cancellationToken)
    {
        try
        {
            var pngBytes = await _thumbnailService.GetThumbnailPngAsync(item.File, thumbnailSize, thumbnailSize, cancellationToken);
            if (pngBytes is null || cancellationToken.IsCancellationRequested || !CarouselItems.Contains(item))
            {
                return null;
            }

            var bitmap = await Task.Run(() => DecodePng(pngBytes), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!cancellationToken.IsCancellationRequested && CarouselItems.Contains(item))
            {
                item.UncroppedThumbnail = bitmap;
                var cropped = await Task.Run(() => CropThumbnail(bitmap), cancellationToken);
                if (cancellationToken.IsCancellationRequested || !CarouselItems.Contains(item)) return null;
                item.Thumbnail = cropped;
                return cropped;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The active folder or carousel view was replaced.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException or
                                           InvalidDataException or FormatException or NotSupportedException)
        {
            // Keep the file-type placeholder when no thumbnail is available.
        }

        return null;
    }

    private Task LoadCurrentImageAsync(bool clearCurrentImage, BitmapSource? immediatePreview = null) =>
        ViewerHost.LoadAsync(_navigationState.CurrentFile, clearCurrentImage, immediatePreview);

    public void ResetZoomToFit() => ViewerHost.ResetZoomToFit();
    private bool _viewerActive = true;
    private bool _historyBusy;
    private CancellationTokenSource? _historyCancellation;
    public void SetViewerActive(bool active) { _viewerActive = active; if (!active) _historyCancellation?.Cancel(); ViewerHost.SetActive(active); }

    private async Task<IFileOrganizationService> GetRootOperationsAsync(FolderTreeEntry? root,
        bool requireActive = false, CancellationToken token = default)
    {
        if (root is null) throw new InvalidOperationException("Select a folder in a loaded root first.");
        var service = await _fileOrganizationService.ForRootAsync(root.Path, token);
        if (!_navigationState.Roots.Contains(root) || (requireActive && (!ReferenceEquals(root, _navigationState.ActiveRoot) || !_viewerActive)))
            throw new InvalidOperationException("The active root changed before the command could run.");
        return service;
    }

    private void ClearActivePresentation(string message, string detail)
    {
        ViewerHost.ResetZoomState();
        _carouselRefreshVersion++;
        _carouselScrollTimer.Stop();
        _hasCenteredCarouselItem = false;
        _carouselOffsetBeforeRefresh = 0;
        _carouselScrollViewer = null;
        _carouselStartIndex = 0;
        _carouselEndIndex = -1;
        _carouselFileCount = 0;
        _carouselCurrentIndex = 0;
        ViewerHost.Clear();
        _ = ViewerHost.LoadAsync(null, true);
        _carouselLoadCancellation?.Cancel();
        _carouselLoadCancellation?.Dispose();
        _carouselLoadCancellation = null;
        CarouselItems.Clear();
        _carouselLeadingSpacer = null;
        _carouselTrailingSpacer = null;
        CarouselList.SelectedItem = null;
        EmptyFilesNotice.Visibility = Visibility.Visible;
        ViewerHost.ShowImageMessage(message, detail);
    }

    private static BitmapSource DecodePng(byte[] pngBytes)
    {
        using var stream = new MemoryStream(pngBytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static BitmapSource CropThumbnail(BitmapSource source)
    {
        var cropSize = Math.Min(source.PixelWidth, source.PixelHeight);
        var crop = new Int32Rect(
            (source.PixelWidth - cropSize) / 2,
            (source.PixelHeight - cropSize) / 2,
            cropSize,
            cropSize);
        var cropped = new CroppedBitmap(source, crop);
        cropped.Freeze();
        return cropped;
    }
}
