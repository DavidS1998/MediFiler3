namespace MediFiler3.Views;

public partial class BrowseView
{
    public Task NavigateFolderBreadcrumbAsync(string path)
    {
        if (!_viewerActive || _historyBusy || _readerCommandCancellation is not null ||
            _galleryCommandCancellation is not null || IsGalleryMoveDragActive) return Task.CompletedTask;

        // Resolve through actual ancestry, retaining ownership with overlapping loaded roots.
        for (var folder = _navigationState.ActiveFolder; folder is not null; folder = folder.Parent)
        {
            if (!PathsEqual(folder.Path, path)) continue;
            _navigationController.CancelPendingNavigation();
            return ActivateFolderAsync(folder);
        }
        return Task.CompletedTask;
    }
}
