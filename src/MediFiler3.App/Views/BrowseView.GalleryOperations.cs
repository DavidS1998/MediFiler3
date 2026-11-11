using System.ComponentModel;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Folders;
using MediFiler3.Interop;

namespace MediFiler3.Views;

public partial class BrowseView
{
    private CancellationTokenSource? _galleryCommandCancellation;

    private async Task RunGalleryFileCommandAsync(FolderTreeEntry? destination)
    {
        if (_galleryCommandCancellation is not null || _historyBusy || !_viewerActive || !IsGallery) return;
        using var deferral = DeferReconciliation();
        using var playbackRelease = ViewerHost.SuspendMediaForFileOperation();
        var root = _navigationState.ActiveRoot;
        var folder = _navigationState.ActiveFolder;
        var revision = _commandRevision;
        var selected = _navigationState.GallerySelection.GetSelectedFiles(_navigationState.Files).ToArray();
        if (root is null || selected.Length == 0) return;
        using var cancellation = new CancellationTokenSource();
        _galleryCommandCancellation = cancellation;
        bool Current() => _viewerActive && IsGallery && revision == _commandRevision &&
            ReferenceEquals(root, _navigationState.ActiveRoot) && ReferenceEquals(folder, _navigationState.ActiveFolder) &&
            selected.All(_navigationState.ContainsFile);
        void Changed(object? sender, PropertyChangedEventArgs args) { if (!Current()) cancellation.Cancel(); }
        _navigationState.PropertyChanged += Changed;
        try
        {
            if (destination is null && selected.Length > 1 && !await RequireDialogService().ConfirmAsync("Send selected files to Recycle Bin",
                    $"Send {selected.Length} selected files to the Recycle Bin? Restore is best effort and depends on Windows retaining the items.", "Recycle")) return;
            if (!Current() || !selected.SequenceEqual(_navigationState.GallerySelection.GetSelectedFiles(_navigationState.Files))) return;
            var operations = await GetRootOperationsAsync(root, true, cancellation.Token, allowGallery: true, scopeIsCurrent: Current);
            if (!Current()) return;
            var paths = selected.Select(file => file.FullPath).ToArray();
            var result = destination is null
                ? await operations.SendFilesToRecycleBinAsync(paths, cancellation.Token)
                : await operations.MoveFilesAsync(paths, destination.Path, cancellation.Token);
            RememberKnownFileMoves(result.Results);
            await RefreshOperationCountsAsync(result.Results);
            OperationHistoryChanged?.Invoke();
            if (!Current()) return;
            ShowOperationNotice(result.Message);
            var current = _navigationState.CurrentFile;
            var changedPaths = result.Results.Select((item, index) => (item, index))
                .Where(pair => pair.item.Succeeded && pair.item.OperationId is not null)
                .Select(pair => selected[pair.index].FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var index = current is null ? 0 : _navigationState.Files.IndexOf(current);
            var next = _navigationState.Files.Skip(Math.Max(0, index)).Concat(_navigationState.Files.Take(Math.Max(0, index)).Reverse())
                .FirstOrDefault(file => !changedPaths.Contains(file.FullPath));
            await ReloadFilesInActiveFolderAsync(current is not null && !changedPaths.Contains(current.FullPath) ? current.FullPath : next?.FullPath, Current);
            if (destination is not null)
            {
                var listing = await _fileListingService.GetFilesAsync(destination.Path, CancellationToken.None);
                if (_viewerActive && IsGallery && ReferenceEquals(root, _navigationState.ActiveRoot) && ReferenceEquals(folder, _navigationState.ActiveFolder))
                    UpdateFolderFileCounts(destination.Path, listing.Files.Count);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (IsOperationUiException(exception))
        { ShowOperationNotice($"Could not complete selected-file operation: {exception.Message}"); }
        finally
        {
            _navigationState.PropertyChanged -= Changed;
            _galleryCommandCancellation = null;
        }
    }
}
