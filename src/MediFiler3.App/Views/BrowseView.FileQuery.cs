using System.IO;
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
    private FolderTreeEntry? _inventoryRoot;
    private IReadOnlyList<FileEntry> _inventory = [];
    private bool _inventoryIncomplete;
    private bool _inventoryDiscovering;
    private bool _browseOverscrollEnabled = true;
    private CancellationTokenSource _scopeLifetime = new();
    private IReadOnlyList<FileEntry> FolderInventory => ReferenceEquals(_inventoryFolder, _navigationState.ActiveFolder)
        ? _inventory : [];

    public async Task ApplyFileQueryAsync(FileQuery query)
    {
        query.Validate();
        if (query == _fileQuery) return;
        var scopeChanged = query.IncludeSubfolders != _fileQuery.IncludeSubfolders;
        _fileQuery = query;
        _navigationController.SetOverscrollEnabled(_browseOverscrollEnabled && !query.IncludeSubfolders);
        CancelFileQueryWork();
        _navigationController.CancelPendingNavigation();

        var folder = _navigationState.ActiveFolder;
        if (scopeChanged)
        {
            ResetScopeWork();
            using var deferral = DeferReconciliation();
            if (folder is not null)
                await ReloadFilesInActiveFolderAsync(null, preferredFileIndex: Math.Max(0, _navigationState.CurrentFilePosition - 1),
                    scopeChanged: true);
            return;
        }
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

    private void ResetScopeWork()
    {
        _scopeLifetime.Cancel();
        _scopeLifetime.Dispose();
        _scopeLifetime = new();
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

    private async Task<ScopeListing?> ReadQueryScopeAsync(FolderTreeEntry folder, CancellationToken token,
        Func<ScopeListing, Task>? publishProgress = null)
    {
        var scope = _scopeLifetime;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, scope.Token);
        var work = cancellation.Token;
        try
        {
            if (!_fileQuery.IncludeSubfolders)
            {
                var listing = await _fileListingService.GetFilesAsync(folder.Path, work);
                work.ThrowIfCancellationRequested();
                return new(listing.Files, listing.Errors, listing.Errors.Count == 0 ? listing.Files.Count : null);
            }

            var files = new List<FileEntry>();
            var errors = new List<string>();
            int? directCount = null;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var first = true;
            var publication = System.Diagnostics.Stopwatch.StartNew();
            await foreach (var batch in _fileListingService.GetSubtreeFilesAsync(folder.Path, work))
            {
                work.ThrowIfCancellationRequested();
                foreach (var file in batch.Files)
                    if (visited.Add(file.FullPath)) files.Add(file);
                errors.AddRange(batch.Errors);
                if (first) directCount = batch.Errors.Count == 0 ? batch.Files.Count : null;
                if (publishProgress is not null && (first || publication.ElapsedMilliseconds >= 120))
                {
                    await publishProgress(new(files.ToArray(), errors.ToArray(), directCount));
                    publication.Restart();
                }
                first = false;
            }

            work.ThrowIfCancellationRequested();
            return new(files.ToArray(), errors, directCount);
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { return null; }
    }

    private sealed record ScopeListing(IReadOnlyList<FileEntry> Files, IReadOnlyList<string> Errors, int? DirectCount);

    private async Task<bool> PublishScopeListingAsync(FolderTreeEntry folder, ScopeListing listing,
        CancellationToken token, Func<bool> isCurrent, string? preferredPath, int preferredIndex,
        bool progress = false, bool queryChanged = false)
    {
        return await ProjectFilesAsync(listing.Files, token, results =>
        {
            if (!isCurrent() || !ReferenceEquals(folder, _navigationState.ActiveFolder)) return false;
            if (listing.DirectCount is { } direct) UpdateFolderFileCounts(folder.Path, direct);
            StoreInventory(folder, listing.Files, listing.Errors.Count > 0);
            _inventoryDiscovering = progress;
            PublishFileResults(results, preferredPath, preferredIndex, queryChanged,
                skipUnchanged: progress || preferredPath is null ||
                    _navigationState.CurrentFile is { } current && PathsEqual(preferredPath, current.FullPath));
            return true;
        });
    }

    private void StoreInventory(FolderTreeEntry folder, IReadOnlyList<FileEntry> files, bool incomplete)
    {
        _inventoryFolder = folder;
        _inventoryRoot = _navigationState.GetRootForFolder(folder);
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

        EmptyFilesNotice.Text = _inventoryDiscovering ? "Discovering files in subfolders..." : _inventoryIncomplete ? "The folder listing is incomplete." :
            filtered ? "No files match the current filter." : "No files in this folder.";
        EmptyFilesNotice.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _reader?.SetFilterNotice(filtered, _inventoryIncomplete, _inventoryDiscovering);
        _gallery?.SetFilterNotice(filtered, _inventoryIncomplete, _inventoryDiscovering);
        _gallery?.SetRelativeFileContext(_fileQuery.IncludeSubfolders ? _navigationState.ActiveFolder?.Path : null);
        var suffix = _inventoryDiscovering ? " (scanning)" : _inventoryIncomplete ? " (partial)" : "";
        FileQueryStatusChanged?.Invoke(filtered ? $"{results.Count} of {_inventory.Count}{suffix}" : "");
    }

    private static FileEntry? AtPosition(IReadOnlyList<FileEntry> files, int position) =>
        files.Count == 0 ? null : files[Math.Clamp(position, 0, files.Count - 1)];

    private FolderTreeEntry? GetResultOwner(FileEntry file)
    {
        if (!_navigationState.ContainsFile(file)) return null;
        var root = _navigationState.ActiveRoot;
        if (!_fileQuery.IncludeSubfolders) return root;
        // A physical subtree can also be loaded as a separate root. The active tree
        // occurrence owns the inventory and the journal for each of its results.
        return _inventoryFolder is { } folder && ReferenceEquals(folder, _navigationState.ActiveFolder) &&
            ReferenceEquals(_inventoryRoot, root) && IsPathWithin(file.FullPath, folder.Path) ? root : null;
    }

    private async Task ResumeScopeDiscoveryAsync()
    {
        using var deferral = DeferReconciliation();
        await ReloadFilesInActiveFolderAsync(null, preferredFileIndex: Math.Max(0, _navigationState.CurrentFilePosition - 1),
            scopeChanged: true);
    }

    public string GetResultDisplayName(FileEntry file) =>
        _fileQuery.IncludeSubfolders && _navigationState.ActiveFolder is { } folder
            ? Path.GetRelativePath(folder.Path, file.FullPath) : file.Name;

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
