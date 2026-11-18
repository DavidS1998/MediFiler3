using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;
using MediFiler3.Interop;

namespace MediFiler3.Views;

public partial class BrowseView
{
    private MediFiler3.Application.Files.FileNameColorRule[] _fileNameRules = [];
    public void SetFileNameRules(IReadOnlyList<MediFiler3.Application.Files.FileNameColorRule> rules)
    {
        MediFiler3.Application.Files.FileNameColorRule.ValidateAll(rules);
        _fileNameRules = rules.ToArray(); _gallery?.SetFileNameRules(_fileNameRules);
    }
    private WinUiGalleryIsland? _gallery;
    private bool _galleryRefreshQueued;
    private bool _galleryFilesDirty = true;
    private double _galleryWidth = 150, _galleryHeight = 150;
    public bool IsGallery => _navigationState.ViewingMode == ViewingMode.Gallery;
    public event Action? BrowseRequested;
    public event Action? GalleryRequested;

    private void OnGalleryContextChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_galleryMoveDrag is not null && !GalleryDragIsCurrent()) EndGalleryMoveDrag();
        if (args.PropertyName is nameof(NavigationSessionState.CurrentFile) or nameof(NavigationSessionState.ViewingMode) or nameof(NavigationSessionState.Files))
            UpdateReaderExternalApplication();
        if (args.PropertyName == nameof(NavigationSessionState.Files))
        {
            _galleryFilesDirty = true;
            QueueGalleryRefresh(); RefreshReaderFiles();
        }
    }

    private void QueueGalleryRefresh()
    {
        if (!IsGallery || !_viewerActive || _galleryRefreshQueued) return;
        _galleryRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _galleryRefreshQueued = false;
            if (IsGallery && _viewerActive && _gallery is not null && _galleryFilesDirty)
            {
                _gallery.SetFiles(_navigationState.Files, _navigationState.CurrentFile, snapshotListing: _fileQuery.IncludeSubfolders);
                _galleryFilesDirty = false;
            }
        }));
    }

    public void SetGalleryTileSize(double width, double height)
    {
        _galleryWidth = width; _galleryHeight = height;
        _gallery?.SetTileSize(width, height);
    }

    public void SetViewingMode(ViewingMode mode)
    {
        if (_navigationState.ViewingMode == mode) return;
        _historyCancellation?.Cancel();
        _readerCommandCancellation?.Cancel();
        _galleryCommandCancellation?.Cancel();
        _navigationController.SwitchViewingMode(mode);
        if (mode == ViewingMode.Gallery && _navigationState.CurrentFile is { } current)
            _navigationState.GallerySelection.Select(_navigationState.Files, current);
        _commandRevision++;
        ApplyViewingMode();
    }

    private void ApplyViewingMode()
    {
        UpdateTreeCarouselClearance();
        var gallery = IsGallery && _viewerActive;
        ViewerHost.Visibility = !IsBrowse ? Visibility.Collapsed : Visibility.Visible;
        CarouselOverlay.Visibility = IsBrowse && _carouselEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (!IsBrowse)
        {
            ViewerHost.SetActive(false);
            _carouselRefreshVersion++;
            _carouselLoadCancellation?.Cancel(); CancelCarouselItemLoads();
            _carouselScrollTimer.Stop(); CarouselGhosts.Children.Clear();
        }
        _gallery?.SetActive(gallery);
        GalleryHost.Visibility = gallery ? Visibility.Visible : Visibility.Collapsed;
        if (gallery && _gallery is null)
        {
            _gallery = new WinUiGalleryIsland(_thumbnailService, _navigationState.GallerySelection);
            _gallery.SetFileNameRules(_fileNameRules);
            _gallery.SetRelativeFileContext(_fileQuery.IncludeSubfolders ? _navigationState.ActiveFolder?.Path : null);
            _gallery.SetFilterNotice(_fileQuery != MediFiler3.Application.Files.FileQuery.AllFiles, _inventoryIncomplete, _inventoryDiscovering);
            _gallery.FileSelected += file =>
            {
                if (!IsGallery || !_viewerActive || !_navigationState.ContainsFile(file)) return;
                _navigationState.SetCurrentFile(file);
            };
            _gallery.OpenRequested += file =>
            {
                if (!IsGallery || !_viewerActive || !_navigationState.ContainsFile(file)) return;
                _navigationState.SetCurrentFile(file);
                BrowseRequested?.Invoke();
            };
            _gallery.MoveDragStarting += BeginGalleryMoveDrag;
            _gallery.MoveDragCompleted += EndGalleryMoveDrag;
            _gallery.RenameRequested += async () => await PromptRenameCurrentFileAsync();
            _gallery.DeleteRequested += async () => await SendCurrentFileToRecycleBinAsync();
            _gallery.UndoRequested += async () => await UndoLastOperationAsync();
            _gallery.RedoRequested += async () => await RedoLastOperationAsync();
            _gallery.RefreshRequested += () => RefreshRequested?.Invoke();
            _gallery.SetTileSize(_galleryWidth, _galleryHeight);
            _gallery.SetFiles(_navigationState.Files, _navigationState.CurrentFile, snapshotListing: _fileQuery.IncludeSubfolders);
            _galleryFilesDirty = false;
            GalleryHost.Children.Add(_gallery);
        }
        if (gallery && _gallery is not null)
        {
            if (_galleryFilesDirty)
            {
                _gallery.SetFiles(_navigationState.Files, _navigationState.CurrentFile, snapshotListing: _fileQuery.IncludeSubfolders);
                _galleryFilesDirty = false;
            }
            UpdateGalleryTreeInset();
            _gallery.RevealCurrentFile(_navigationState.CurrentFile, preserveSelection: true);
        }
        ApplyReaderMode();
        if (IsBrowse)
        {
            // Capture the shared file while inactive, then resume only the requested Browse context.
            _ = ViewerHost.LoadAsync(_navigationState.CurrentFile, false);
            ViewerHost.SetActive(_viewerActive);
            if (_viewerActive) _ = RefreshCarouselAsync();
        }
        PresentationChanged?.Invoke();
    }

    private void UpdateGalleryTreeInset() => _gallery?.SetTreeInset(_folderOverlayPinned
        ? FolderOverlay.Width + FolderOverlay.Margin.Left : 26);
}
