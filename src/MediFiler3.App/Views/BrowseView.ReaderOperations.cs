using System.ComponentModel;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;

namespace MediFiler3.Views;

public partial class BrowseView
{
    private CancellationTokenSource? _readerCommandCancellation;

    private async Task RunReaderFileCommandAsync(FolderTreeEntry? destination)
    {
        if (!IsReader || !_viewerActive || _readerCommandCancellation is not null || _galleryCommandCancellation is not null || _historyBusy) return;
        using var deferral = DeferReconciliation();
        using var readerDeferral = DeferReaderReconciliation();
        var root = _navigationState.ActiveRoot; var folder = _navigationState.ActiveFolder;
        var file = _navigationState.CurrentFile; var revision = _commandRevision;
        if (root is null || file is null || !ReaderSessionState.Supports(file) ||
            _navigationState.Reader.Pages.ElementAtOrDefault(ReaderPageIndex) != file ||
            destination is not null && _navigationState.GetRootForFolder(destination) is null) return;
        using var stop = new CancellationTokenSource();
        _readerCommandCancellation = stop;
        bool Current() => _viewerActive && IsReader && revision == _commandRevision &&
            ReferenceEquals(root, _navigationState.ActiveRoot) && ReferenceEquals(folder, _navigationState.ActiveFolder) &&
            _navigationState.CurrentFile == file && _navigationState.Reader.Pages.ElementAtOrDefault(ReaderPageIndex) == file && _navigationState.ContainsFile(file);
        void Changed(object? sender, PropertyChangedEventArgs args) { if (!Current()) stop.Cancel(); }
        _navigationState.PropertyChanged += Changed;
        try
        {
            var operations = await GetRootOperationsAsync(root, true, stop.Token, scopeIsCurrent: Current, allowReader: true);
            if (!Current()) return;
            // Use the existing grouped result contract even for one page: cancelled, failed and
            // unattempted outcomes remain explicit, with the same journal/compensation rules.
            var result = destination is null
                ? await operations.SendFilesToRecycleBinAsync([file.FullPath], stop.Token)
                : await operations.MoveFilesAsync([file.FullPath], destination.Path, stop.Token);
            RememberKnownFileMoves(result.Results);
            await RefreshOperationCountsAsync(result.Results);
            OperationHistoryChanged?.Invoke();
            if (!Current()) return;
            ShowOperationNotice(result.Message);
            if (!result.Results.Any(item => item.Succeeded && item.OperationId is not null)) return;
            var pages = _navigationState.Reader.Pages;
            var index = pages.ToList().IndexOf(file);
            var next = pages.Skip(index + 1).FirstOrDefault() ?? pages.Take(Math.Max(0, index)).LastOrDefault();
            await ReloadFilesInActiveFolderAsync(next?.FullPath, Current, preferReaderCurrent: true);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception exception) when (IsOperationUiException(exception))
        { ShowOperationNotice($"Could not complete Reader file operation: {exception.Message}"); }
        finally
        {
            _navigationState.PropertyChanged -= Changed;
            _readerCommandCancellation = null;
        }
    }
}
