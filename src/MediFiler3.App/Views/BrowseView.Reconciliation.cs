using System.IO;
using System.Windows.Threading;
using MediFiler3.Application.Files;
using MediFiler3.Application.Folders;
using MediFiler3.Domain.Files;

namespace MediFiler3.Views;

public partial class BrowseView
{
    private readonly IFolderChangeMonitor? _changeMonitor;
    private CancellationTokenSource? _watchStop;
    private Task? _watchTask;
    private string[] _watchedRoots = [];
    private int _reconciliationDeferrals;
    private long _commandRevision;
    private string? _observedFolder;

    private IDisposable DeferReconciliation()
    {
        _reconciliationDeferrals++;
        _commandRevision++;
        return new ReconciliationDeferral(this);
    }

    private sealed class ReconciliationSupersededException : Exception;

    private sealed class ReconciliationDeferral(BrowseView owner) : IDisposable
    {
        public void Dispose() { owner._reconciliationDeferrals--; owner._observedFolder = owner._navigationState.ActiveFolder?.Path; }
    }

    private void StopWatching()
    {
        _watchStop?.Cancel();
        _watchStop?.Dispose();
        _watchStop = null;
    }

    private void StartWatching()
    {
        var requested = _navigationState.Roots.Select(r => r.Path).ToArray();
        if (_watchStop is not null && !(_watchTask?.IsCompleted ?? false) && requested.SequenceEqual(_watchedRoots, StringComparer.OrdinalIgnoreCase)) return;
        StopWatching();
        if (!_viewerActive || _changeMonitor is null || requested.Length == 0) return;
        _watchedRoots = requested;
        _watchStop = new CancellationTokenSource();
        var token = _watchStop.Token;
        var roots = _navigationState.Roots.Select(r => r.Path).ToArray();
        _observedFolder = _navigationState.ActiveFolder?.Path;
        _watchTask = Task.Run(async () =>
        {
            try
            {
                await _changeMonitor.RunAsync(roots, () => Volatile.Read(ref _observedFolder), async (batch, ct) =>
                {
                    // One awaited dispatcher consumer; no unbounded event-to-dispatcher queue.
                    while (!ct.IsCancellationRequested)
                    {
                        var applied = await Dispatcher.InvokeAsync(async () =>
                        {
                            if (ct.IsCancellationRequested || !_viewerActive) return true;
                            if (_reconciliationDeferrals != 0 || ExternalOpenState.Busy || !IsEnabled || System.Windows.Application.Current?.Windows.OfType<System.Windows.Window>().Any(w => w.Owner == System.Windows.Window.GetWindow(this) && w.IsVisible) == true) return false;
                            try { return await ReconcileExternalChangesAsync(batch, ct); }
                            catch (ReconciliationSupersededException) { return false; }
                        }, DispatcherPriority.Background, ct).Task.Unwrap().ConfigureAwait(false);
                        if (applied) break;
                        await Task.Delay(100, ct).ConfigureAwait(false);
                    }
                }, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    await Dispatcher.InvokeAsync(() => ShowOperationNotice($"Live updates failed: {ex.Message}. Refresh (F5) remains available."));
            }
        }, token);
    }

    private async Task<bool> ReconcileExternalChangesAsync(FolderChangeBatch batch, CancellationToken token)
    {
        var revision = _commandRevision;
        bool Current() => !token.IsCancellationRequested && _viewerActive && revision == _commandRevision && _reconciliationDeferrals == 0;
        var folder = _navigationState.ActiveFolder;
        var owner = _navigationState.ActiveRoot;
        var selected = _navigationState.CurrentFile;
        var position = Math.Max(0, _navigationState.CurrentFilePosition - 1);
        var roots = _navigationState.Roots.ToArray();
        var reconcileRoots = batch.ReconcileRoots ?? [];
        var contentPaths = new List<string>();
        foreach (var path in batch.Paths)
        {
            if (batch.RescanRoots.Count == 0 && batch.StructuralPaths?.Contains(path, StringComparer.OrdinalIgnoreCase) == true &&
                _knownFileMoves.TryGetValue(path, out var known) && known.Expires >= DateTimeOffset.UtcNow &&
                await _fileOrganizationService.MatchesFileVersionAsync(path, known.Identity, known.Size, known.ModifiedUtc, token)) continue;
            _knownFileMoves.Remove(path);
            contentPaths.Add(path);
        }
        var invalidatedPaths = contentPaths.ToArray();
        if (_thumbnailService is IThumbnailCacheMaintenance cache)
        {
            foreach (var path in batch.RescanRoots) await cache.InvalidateAsync(path, true, token);
            foreach (var path in invalidatedPaths) await cache.InvalidateAsync(path, true, token);
        }
        if (!Current()) return false;
        _gallery?.InvalidateThumbnails(invalidatedPaths.Concat(batch.RescanRoots));
        _reader?.Invalidate(invalidatedPaths.Concat(batch.RescanRoots));
        if (batch.Status is not null) StatusChanged?.Invoke(batch.Status);

        if (batch.HierarchyChanged || batch.RescanRoots.Count > 0 || reconcileRoots.Count > 0)
        {
            // Scan temporary nodes, merge by path, and keep existing node identity/expansion.
            var loaded = await _folderTreeService.LoadRootsAsync(roots.Select(r => r.Path), token);
            if (!Current()) return false;
            foreach (var missing in roots.Where(r => !loaded.Roots.Any(p => PathsEqual(p.Path, r.Path))))
                missing.ReconcileChildren([]);
            await foreach (var result in _folderTreeService.LoadHierarchyAsync(loaded.Roots, token))
            {
                if (!Current()) return false;
                if (result.Errors.Count == 0)
                    foreach (var existing in FindFolderOccurrences(roots, result.Parent.Path))
                        await existing.ReconcileChildrenAsync(CloneFolderChildren(result.Children), async () =>
                        {
                            await Dispatcher.Yield(DispatcherPriority.Background);
                            if (!Current()) throw new ReconciliationSupersededException();
                        }, token);
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
            if (!Current()) return false;
            if (folder is not null && _navigationState.GetRootForFolder(folder) is null)
            {
                var fallback = _fileQuery.IncludeSubfolders && owner is not null && roots.Contains(owner)
                    ? owner : roots.FirstOrDefault();
                if (fallback is not null) await ActivateFolderAsync(fallback);
                return true;
            }
            StartFolderCounts();
        }
        else
        {
            foreach (var path in batch.Paths)
            {
                var parent = FindFolder(roots, Path.GetDirectoryName(path));
                if (parent is null) continue;
                await foreach (var count in _folderTreeService.CountFilesAsync([parent.Path], token))
                {
                    if (!Current()) return false;
                    if (count.Count is { } number) UpdateFolderFileCounts(parent.Path, (int)number);
                }
            }
        }
        var scope = _scopeLifetime;
        var recursive = _fileQuery.IncludeSubfolders;
        bool InScope(string path) => folder is not null && (recursive ? IsPathWithin(path, folder.Path) :
            PathsEqual(path, folder.Path) || PathsEqual(Path.GetDirectoryName(path) ?? "", folder.Path));
        var affectsFolder = folder is not null && (batch.RescanRoots.Concat(reconcileRoots).Any(root => IsPathWithin(folder.Path, root)) ||
            batch.Paths.Any(InScope));
        if (!affectsFolder || folder is null) return true;
        var listing = await ReadQueryScopeAsync(folder, token);
        if (listing is null || !ReferenceEquals(scope, _scopeLifetime)) return false;
        if (!Current() || !ReferenceEquals(folder, _navigationState.ActiveFolder) || !ReferenceEquals(selected, _navigationState.CurrentFile)) return false;
        if (reconcileRoots.Any(root => IsPathWithin(folder.Path, root)))
        {
            var currentFiles = listing.Files.ToDictionary(f => f.FullPath, StringComparer.OrdinalIgnoreCase);
            var changed = FolderInventory.Where(old => !currentFiles.TryGetValue(old.FullPath, out var file) || file != old)
                .Select(old => old.FullPath).ToArray();
            if (_thumbnailService is IThumbnailCacheMaintenance maintenance)
                foreach (var path in changed) await maintenance.InvalidateAsync(path, false, token);
            if (!Current() || !ReferenceEquals(selected, _navigationState.CurrentFile)) return false;
            _gallery?.InvalidateThumbnails(changed);
            _reader?.Invalidate(changed);
            foreach (var item in CarouselItems.OfType<CarouselThumbnailItem>().Where(i => changed.Contains(i.File.FullPath, StringComparer.OrdinalIgnoreCase)))
            {
                item.ThumbnailCancellation?.Cancel();
                item.Thumbnail = null; item.UncroppedThumbnail = null; item.ThumbnailTask = null;
            }
        }
        var previousInventory = FolderInventory;
        var replacement = listing.Files.FirstOrDefault(f => selected is not null && PathsEqual(f.FullPath, selected.FullPath));
        var selectedChanged = selected is null && listing.Files.Count > 0 || selected != replacement || selected is not null &&
            (batch.RescanRoots.Any(root => IsPathWithin(selected.FullPath, root)) || invalidatedPaths.Any(p => PathsEqual(p, selected.FullPath)));
        var thumbnailsChanged = batch.Paths.Any(InScope);
        if (listing.Files.SequenceEqual(previousInventory) && _inventoryIncomplete == (listing.Errors.Count > 0) &&
            (!selectedChanged || selected is null && _navigationState.Files.Count == 0) && !thumbnailsChanged) return true;
        // Keep the selected record for unaffected media/text/image presentation.
        var previousFiles = previousInventory.ToDictionary(f => f.FullPath, StringComparer.OrdinalIgnoreCase);
        var files = listing.Files.Select(f => (!selectedChanged || selected != f) && previousFiles.TryGetValue(f.FullPath, out var old) && old == f ? old : f).ToArray();
        if (!await ProjectFilesAsync(files, token, matches =>
        {
            if (!Current() || !ReferenceEquals(scope, _scopeLifetime) || !ReferenceEquals(selected, _navigationState.CurrentFile)) return false;
            _inventoryDiscovering = false;
            StoreInventory(folder, files, listing.Errors.Count > 0);
            PublishFileResults(matches, selected?.FullPath, position,
                skipUnchanged: _fileQuery != FileQuery.AllFiles && !selectedChanged);
            selectedChanged |= selected != _navigationState.CurrentFile;
            if (listing.DirectCount is { } direct) UpdateFolderFileCounts(folder.Path, direct);
            return true;
        })) return false;
        // Existing carousel items can otherwise keep stale same-size/timestamp images.
        foreach (var item in CarouselItems.OfType<CarouselThumbnailItem>())
        {
            if (!batch.RescanRoots.Any(root => IsPathWithin(item.File.FullPath, root)) &&
                !invalidatedPaths.Any(path => IsPathWithin(item.File.FullPath, path))) continue;
            item.ThumbnailCancellation?.Cancel();
            item.Thumbnail = null; item.UncroppedThumbnail = null; item.ThumbnailTask = null;
        }
        var reconciledSelection = _navigationState.CurrentFile;
        await RefreshCarouselAsync();
        if (!Current() || !ReferenceEquals(reconciledSelection, _navigationState.CurrentFile)) return true;
        if (selectedChanged) await LoadCurrentImageAsync(clearCurrentImage: true);
        return true;
    }
}
