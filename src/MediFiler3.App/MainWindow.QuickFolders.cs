using MediFiler3.Application.Folders;

namespace MediFiler3;

public partial class MainWindow
{
    private void InitializeQuickFolders(RootFolderHistoryService history)
    {
        var closed = false;
        var revision = 0;

        void ScheduleRefresh()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (closed) return;
                var request = ++revision;
                _ = RunViewShortcutAsync(async () =>
                {
                    var lists = await history.GetListsAsync();
                    if (!closed && request == revision)
                        NavigationIsland.SetQuickFolders(lists.Favorites.Select(folder => folder.Path));
                });
            }));
        }

        history.Changed += ScheduleRefresh;
        Closed += (_, _) =>
        {
            closed = true;
            history.Changed -= ScheduleRefresh;
        };
        NavigationIsland.QuickFolderOpenRequested += async path =>
        {
            if (IsEnabled && !_browseView.IsGalleryMoveDragActive)
                await RunViewShortcutAsync(() => LoadFolderPathsAsync([path]));
        };
        NavigationIsland.QuickFolderMoveRequested += async path =>
        {
            if (IsEnabled && PageContent.Content == _browseView && !_browseView.IsReader && !_browseView.IsGalleryMoveDragActive &&
                (_browseView.IsGallery ? _navigationState.GallerySelection.Count > 0 : _navigationState.CurrentFile is not null))
                await RunViewShortcutAsync(() => _browseView.MoveToQuickFolderAsync(path));
        };
        ScheduleRefresh();
    }
}
