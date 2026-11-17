using System.Windows;
using MediFiler3.Application.Files;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;

namespace MediFiler3.Views;

public partial class BrowseView
{
    private FileQuery _fileQuery = FileQuery.AllFiles;
    private CancellationTokenSource _queryLifetime = new();
    private FolderTreeEntry? _inventoryFolder;
    private IReadOnlyList<FileEntry> _inventory = [];
    private bool _inventoryIncomplete;
    private IReadOnlyList<FileEntry> FolderInventory => ReferenceEquals(_inventoryFolder, _navigationState.ActiveFolder)
        ? _inventory : [];

    public async Task ApplyFileQueryAsync(FileQuery query)
    {
        query.Validate();
        if (query == _fileQuery) return;
        _fileQuery = query;
        CancelFileQueryWork();
        _navigationController.CancelPendingNavigation();

        var folder = _navigationState.ActiveFolder;
        if (folder is null || !ReferenceEquals(folder, _inventoryFolder)) return;
        var inventory = _inventory;
        await ProjectFilesAsync(inventory, _queryLifetime.Token, results =>
        {
            if (!ReferenceEquals(folder, _navigationState.ActiveFolder) || !ReferenceEquals(inventory, _inventory)) return false;
            var previous = _navigationState.CurrentFile;
            PublishFileResults(results, null, Math.Max(0, _navigationState.CurrentFilePosition - 1), queryChanged: true);
            if (previous != _navigationState.CurrentFile && _viewerActive)
                _ = LoadCurrentImageAsync(clearCurrentImage: true);
            _ = RefreshCarouselAsync();
            return true;
        });
    }

    private void CancelFileQueryWork()
    {
        _queryLifetime.Cancel();
        _queryLifetime.Dispose();
        _queryLifetime = new();
    }

    private async Task<bool> ProjectFilesAsync(IReadOnlyList<FileEntry> inventory, CancellationToken token,
        Func<IReadOnlyList<FileEntry>, bool> publish)
    {
        while (!token.IsCancellationRequested && !_queryLifetime.IsCancellationRequested)
        {
            var query = _fileQuery;
            if (query == FileQuery.AllFiles) return publish(inventory);
            var revision = _queryLifetime;
            using var work = CancellationTokenSource.CreateLinkedTokenSource(token, revision.Token);
            var workerToken = work.Token;
            try
            {
                var results = await Task.Run(() => query.Apply(inventory, workerToken), workerToken);
                if (!workerToken.IsCancellationRequested) return publish(results);
            }
            catch (OperationCanceledException) when (workerToken.IsCancellationRequested) { }
        }
        return false;
    }

    private void StoreInventory(FolderTreeEntry folder, IReadOnlyList<FileEntry> files, bool incomplete)
    {
        _inventoryFolder = folder;
        _inventory = files;
        _inventoryIncomplete = incomplete;
    }

    private void PublishFileResults(IReadOnlyList<FileEntry> results, string? preferredPath, int preferredIndex, bool queryChanged = false, bool skipUnchanged = false)
    {
        var filtered = _fileQuery != FileQuery.AllFiles;
        var preferred = preferredPath is null ? null : results.FirstOrDefault(file => PathsEqual(file.FullPath, preferredPath));
        var useFirst = filtered && !queryChanged;
        if (queryChanged && IsReader)
        {
            var pages = results.Where(ReaderSessionState.Supports).ToArray();
            preferred = AtPosition(pages, ReaderPageIndex);
        }
        else
        {
            preferred ??= useFirst && IsReader ? results.FirstOrDefault(ReaderSessionState.Supports) :
                AtPosition(results, useFirst ? 0 : preferredIndex);
        }

        if ((!queryChanged && !skipUnchanged) || !results.SequenceEqual(_navigationState.Files))
        {
            using var readerDeferral = DeferReaderReconciliation();
            if (queryChanged) _readerKeepQueryPosition = true;
            else if (filtered)
            {
                var anchor = _navigationState.Reader.Anchor;
                _readerUseFirstMatch |= anchor is null || !results.Any(file => PathsEqual(file.FullPath, anchor.Path));
            }
            _navigationState.ReplaceFiles(results, preferred, selectFirst: !(useFirst || queryChanged) || !IsReader);
        }

        EmptyFilesNotice.Text = _inventoryIncomplete ? "The folder listing is incomplete." :
            filtered ? "No files match the current filter." : "No files in this folder.";
        EmptyFilesNotice.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _reader?.SetFilterNotice(filtered, _inventoryIncomplete);
        _gallery?.SetFilterNotice(filtered, _inventoryIncomplete);
        FileQueryStatusChanged?.Invoke(filtered ? $"{results.Count} of {_inventory.Count}{(_inventoryIncomplete ? " (partial)" : "")}" : "");
    }

    private static FileEntry? AtPosition(IReadOnlyList<FileEntry> files, int position) =>
        files.Count == 0 ? null : files[Math.Clamp(position, 0, files.Count - 1)];

    public event Action<string>? FileQueryStatusChanged;

    private async Task<bool> FolderHasQueryMatchAsync(FolderTreeEntry folder, CancellationToken token)
    {
        var query = _fileQuery;
        if (query == FileQuery.AllFiles) return true;
        var listing = await _fileListingService.GetFilesAsync(folder.Path, token);
        return await Task.Run(() =>
        {
            foreach (var file in listing.Files)
            {
                token.ThrowIfCancellationRequested();
                if (query.Matches(file)) return true;
            }
            return false;
        }, token);
    }

    public void FocusActiveViewer()
    {
        if (IsReader) FocusReader();
        else if (IsGallery) _gallery?.FocusItems();
        else ViewerHost.FocusViewer();
    }
}
