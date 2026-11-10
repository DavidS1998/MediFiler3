using System.Windows;
using System.Windows.Controls;
using MediFiler3.Application.Folders;
using MediFiler3.Interop;

namespace MediFiler3.Views;

public partial class HomeView : UserControl
{
    private readonly RootFolderHistoryService _history;
    private readonly IRootFolderAvailabilityService _availability;
    private CancellationTokenSource? _refresh;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;
    private bool _stopped;
    private int _activation;
    public event Action? AddFoldersRequested;
    public event Action<string[]>? FoldersDropped;
    public Func<string, Task>? OpenRootRequested { get; set; }
    public Func<string, Task<string?>>? LocateFolderRequested { get; set; }

    public HomeView(RootFolderHistoryService history, IRootFolderAvailabilityService availability)
    {
        _history = history;
        _availability = availability;
        InitializeComponent();
        HomeIsland.AddFoldersRequested += () => AddFoldersRequested?.Invoke();
        HomeIsland.FoldersDropped += paths => FoldersDropped?.Invoke(paths);
        HomeIsland.RefreshRequested += async () => await RefreshAsync();
        HomeIsland.FolderActionRequested += async action => await HandleActionAsync(action);
        Loaded += async (_, _) => { _activation++; await RefreshAsync(); };
        Unloaded += (_, _) => { _activation++; _refresh?.Cancel(); };
        _history.Changed += OnHistoryChanged;
    }

    private void OnHistoryChanged()
    {
        if (_stopped || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() => { if (IsLoaded && !_stopped) _ = RefreshAsync(); }));
    }

    public async Task RefreshAsync()
    {
        if (_stopped) return;
        _refresh?.Cancel(); _refresh?.Dispose();
        _refresh = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _refresh.Token;
        try
        {
            // Publish local history before checking removable/network folders.
            var lists = await _history.GetListsAsync(token);
            if (token.IsCancellationRequested) return;
            HomeIsland.SetLists(lists);
            var progress = new Progress<RootFolderStatus>(status =>
            {
                if (!token.IsCancellationRequested && !_stopped) HomeIsland.SetAvailability(status);
            });
            await _history.CheckAvailabilityAsync(token, progress);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested && !_stopped)
                HomeIsland.SetMessage($"Could not load your library: {exception.Message}", true);
        }
    }

    private async Task HandleActionAsync(HomeFolderAction action)
    {
        if (_busy || _stopped) return;
        var activation = _activation;
        _busy = true;
        HomeIsland.SetBusy(true);
        HomeIsland.SetMessage(null);
        try
        {
            switch (action.Action)
            {
                case HomeAction.Open:
                    var status = await _availability.CheckAsync(action.Path, _lifetime.Token);
                    if (_activation != activation || !IsLoaded || _stopped) break;
                    if (status == RootFolderAvailability.Available)
                    {
                        if (OpenRootRequested is not null && IsLoaded && !_stopped) await OpenRootRequested(action.Path);
                    }
                    else
                    {
                        var lists = await _history.GetListsAsync();
                        var root = lists.Frequent.Concat(lists.Recent).Concat(lists.Favorites)
                            .FirstOrDefault(r => string.Equals(r.Path, action.Path, StringComparison.OrdinalIgnoreCase));
                        if (root is not null) HomeIsland.SetAvailability(new(root, status));
                        HomeIsland.SetMessage("This folder is unavailable. Choose Locate or Remove below its entry.");
                    }
                    break;
                case HomeAction.Favorite:
                    await _history.SetFavoriteAsync(action.Path, action.Favorite);
                    break;
                case HomeAction.Locate:
                    var selected = LocateFolderRequested is null ? null : await LocateFolderRequested(action.Path);
                    if (selected is not null)
                    {
                        await _history.LocateAsync(action.Path, selected, _lifetime.Token);
                        HomeIsland.SetMessage("Folder location updated. Visit history and favorite state were preserved.");
                    }
                    break;
                case HomeAction.Remove:
                    await _history.RemoveAsync(action.Path);
                    HomeIsland.SetMessage("Saved entry removed. Files and folders were left unchanged.");
                    break;
            }
            if (IsLoaded && !_stopped) _ = RefreshAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_stopped)
            {
                HomeIsland.SetMessage($"Could not complete that action: {exception.Message}", true);
                if (IsLoaded) _ = RefreshAsync(); // Restore star state if persistence rejected the toggle.
            }
        }
        finally
        {
            _busy = false;
            if (!_stopped) HomeIsland.SetBusy(false);
        }
    }

    public void Stop()
    {
        _stopped = true;
        _history.Changed -= OnHistoryChanged;
        _refresh?.Cancel(); _refresh?.Dispose(); _refresh = null;
        _lifetime.Cancel();
    }
}
