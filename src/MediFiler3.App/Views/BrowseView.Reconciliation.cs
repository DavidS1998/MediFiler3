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
        var selected = _navigationState.CurrentFile;
        var position = Math.Max(0, _navigationState.CurrentFilePosition - 1);
        var roots = _navigationState.Roots.ToArray();
        if (_thumbnailService is IThumbnailCacheMaintenance cache)
        {
            foreach (var path in batch.RescanRoots) await cache.InvalidateAsync(path, true, token);
            foreach (var path in batch.Paths) await cache.InvalidateAsync(path, true, token);
        }
        if (!Current()) return false;
        _gallery?.InvalidateThumbnails(batch.Paths.Concat(batch.RescanRoots));
        if (batch.Status is not null) StatusChanged?.Invoke(batch.Status);

        if (batch.HierarchyChanged || batch.RescanRoots.Count > 0)
        {
            // Scan temporary nodes, merge by path, and keep existing node identity/expansion.
            var loaded = await _folderTreeService.LoadRootsAsync(roots.Select(r => r.Path), token);
            if (!Current()) return false;
            foreach (var missing in roots.Where(r => !loaded.Roots.Any(p => PathsEqual(p.Path, r.Path))))
                missing.ReconcileChildren([]);
            await foreach (var result in _folderTreeService.LoadHierarchyAsync(loaded.Roots, token))
            {
                if (!Current()) return false;
                var existing = FindFolder(roots, result.Parent.Path);
                if (existing is not null && result.Errors.Count == 0) await existing.ReconcileChildrenAsync(result.Children, async () =>
                {
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    if (!Current()) throw new ReconciliationSupersededException();
                }, token);
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
            if (!Current()) return false;
            if (folder is not null && FindFolder(roots, folder.Path) is null)
            {
                var fallback = roots.FirstOrDefault();
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
        var affectsFolder = folder is not null && (batch.RescanRoots.Any(root => IsPathWithin(folder.Path, root)) ||
            batch.Paths.Any(path => PathsEqual(path, folder.Path) || PathsEqual(Path.GetDirectoryName(path) ?? "", folder.Path)));
        if (!affectsFolder || folder is null) return true;
        var listing = await _fileListingService.GetFilesAsync(folder.Path, token);
        if (!Current() || !ReferenceEquals(folder, _navigationState.ActiveFolder) || !ReferenceEquals(selected, _navigationState.CurrentFile)) return false;
        var replacement = listing.Files.FirstOrDefault(f => selected is not null && PathsEqual(f.FullPath, selected.FullPath));
        var selectedChanged = selected is null && listing.Files.Count > 0 || selected != replacement || selected is not null &&
            (batch.RescanRoots.Any(root => IsPathWithin(selected.FullPath, root)) || batch.Paths.Any(p => PathsEqual(p, selected.FullPath)));
        var thumbnailsChanged = batch.Paths.Any(p => PathsEqual(Path.GetDirectoryName(p) ?? "", folder.Path));
        if (listing.Files.SequenceEqual(_navigationState.Files) && !selectedChanged && !thumbnailsChanged) return true;
        // Keep the selected record for unaffected media/text/image presentation.
        var previousFiles = _navigationState.Files.ToDictionary(f => f.FullPath, StringComparer.OrdinalIgnoreCase);
        var files = listing.Files.Select(f => (!selectedChanged || selected != f) && previousFiles.TryGetValue(f.FullPath, out var old) && old == f ? old : f).ToArray();
        _navigationState.ReplaceFiles(files);
        _navigationState.SetCurrentFile(!selectedChanged && selected is not null ? selected : replacement ?? files.ElementAtOrDefault(Math.Clamp(position, 0, Math.Max(0, files.Length - 1))));
        UpdateFolderFileCounts(folder.Path, files.Length);
        EmptyFilesNotice.Visibility = files.Length == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        // Existing carousel items can otherwise keep stale same-size/timestamp images.
        foreach (var item in CarouselItems.OfType<CarouselThumbnailItem>())
        {
            if (!batch.RescanRoots.Any(root => IsPathWithin(item.File.FullPath, root)) &&
                !batch.Paths.Any(path => IsPathWithin(item.File.FullPath, path))) continue;
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
