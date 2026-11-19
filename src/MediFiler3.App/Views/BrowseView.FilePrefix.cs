using MediFiler3.Application.Navigation;

namespace MediFiler3.Views;

public partial class BrowseView
{
    public async Task ChangeLeadingPlusAsync(bool add)
    {
        if (IsReader || !_viewerActive || _historyBusy || _galleryCommandCancellation is not null) return;
        var root = _navigationState.ActiveRoot;
        var folder = _navigationState.ActiveFolder;
        var mode = _navigationState.ViewingMode;
        var current = _navigationState.CurrentFile;
        var files = IsGallery ? _navigationState.GallerySelection.GetSelectedFiles(_navigationState.Files)
            : current is null ? [] : new[] { current };
        if (root is null || files.Count == 0 || files.Any(file => !ReferenceEquals(GetResultOwner(file), root))) return;

        using var deferral = DeferReconciliation();
        using var playbackRelease = ViewerHost.SuspendMediaForFileOperation();
        using var cancellation = new CancellationTokenSource();
        _galleryCommandCancellation = cancellation;
        var revision = _commandRevision;
        bool Current() => _viewerActive && mode == _navigationState.ViewingMode && revision == _commandRevision &&
            ReferenceEquals(root, _navigationState.ActiveRoot) && ReferenceEquals(folder, _navigationState.ActiveFolder);
        bool CanContinue() => Current() && (mode == ViewingMode.Gallery
            ? files.SequenceEqual(_navigationState.GallerySelection.GetSelectedFiles(_navigationState.Files))
            : ReferenceEquals(current, _navigationState.CurrentFile));

        try
        {
            var service = await GetRootOperationsAsync(root, requireActive: true, cancellation.Token,
                allowGallery: IsGallery, scopeIsCurrent: CanContinue);
            if (!CanContinue()) return;
            var result = await service.ChangeLeadingPlusAsync(files.Select(file => file.FullPath).ToArray(), add, cancellation.Token);
            var renamed = result.Results.Where(item => item.Succeeded && item.OperationId is not null &&
                    item.PreviousPath is not null && item.AffectedPath is not null)
                .ToDictionary(item => item.PreviousPath!, item => item.AffectedPath!, StringComparer.OrdinalIgnoreCase);
            if (renamed.Count > 0) OperationHistoryChanged?.Invoke();
            if (!Current()) return;
            ShowOperationNotice(result.Message);
            if (renamed.Count == 0) return;

            string Destination(string path) => renamed.GetValueOrDefault(path, path);
            var preferred = _navigationState.CurrentFile is { } selected ? Destination(selected.FullPath) : null;
            var selectedPaths = _navigationState.GallerySelection.GetSelectedFiles(_navigationState.Files)
                .Select(file => Destination(file.FullPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            await ReloadFilesInActiveFolderAsync(preferred, Current);
            if (IsGallery && Current())
                _navigationState.GallerySelection.ReplaceSelection(_navigationState.Files,
                    _navigationState.Files.Where(file => selectedPaths.Contains(file.FullPath)), _navigationState.CurrentFile);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (IsOperationUiException(exception))
        {
            ShowOperationNotice($"Could not change the filename prefix: {exception.Message}");
        }
        finally { _galleryCommandCancellation = null; }
    }
}
