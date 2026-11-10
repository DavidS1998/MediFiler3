using MediFiler3.Application.Persistence;

namespace MediFiler3.Application.Folders;

public static class RootFolderPath
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("A root path must be absolute.", nameof(path));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    public static string Name(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}

public enum RootFolderAvailability { Available, Missing, AccessDenied, TemporarilyUnavailable }
public interface IRootFolderAvailabilityService
{
    Task<RootFolderAvailability> CheckAsync(string path, CancellationToken cancellationToken = default);
}
public sealed record RootFolderStatus(RootFolderHistory Root, RootFolderAvailability Availability);
public sealed record RootFolderLists(IReadOnlyList<RootFolderHistory> Recent,
    IReadOnlyList<RootFolderHistory> Frequent, IReadOnlyList<RootFolderHistory> Favorites);

/// <summary>
/// One shared record per normalized Windows root path. Call RecordOpenedRootsAsync only
/// after explicit root loading succeeds, never from refresh or subfolder activation.
/// Changed may run on a worker; presentation subscribers must marshal to their dispatcher.
/// </summary>
public sealed class RootFolderHistoryService(IRootFolderHistoryStore store,
    IRootFolderAvailabilityService availability, TimeProvider? timeProvider = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<RootFolderHistory>? _roots;
    public event Action? Changed;

    public async Task<RootFolderLists> GetListsAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(token).ConfigureAwait(false);
            return new(
                _roots!.Where(r => r.ShowInRecent).OrderByDescending(r => r.LastOpenedUtc)
                    .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
                _roots!.Where(r => r.OpenCount > 0).OrderByDescending(r => r.OpenCount)
                    .ThenByDescending(r => r.LastOpenedUtc).ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
                _roots!.Where(r => r.Favorite).OrderBy(r => RootFolderPath.Name(r.Path), StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        finally { _gate.Release(); }
    }

    public async Task RecordOpenedRootsAsync(IEnumerable<string> successfulRootPaths, CancellationToken token = default)
    {
        var paths = successfulRootPaths.Select(RootFolderPath.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await MutateAsync(async () =>
        {
            var visited = (timeProvider ?? TimeProvider.System).GetUtcNow();
            foreach (var path in paths)
            {
                var old = Find(path) ?? new RootFolderHistory(path, 0, null, false, false);
                await SaveAsync(old with { OpenCount = checked(old.OpenCount + 1), LastOpenedUtc = visited,
                    ShowInRecent = true }, token).ConfigureAwait(false);
            }
        }, token).ConfigureAwait(false);
    }

    // Only existing root records can be starred. Subfolders have no records to favorite.
    public Task SetFavoriteAsync(string path, bool favorite, CancellationToken token = default) =>
        MutateAsync(() => SaveAsync(Require(path) with { Favorite = favorite }, token), token);

    public Task RemoveAsync(string path, CancellationToken token = default) => MutateAsync(async () =>
    {
        var root = Require(path);
        await Task.Run(() => store.RemoveAsync(root.Path, token), token).ConfigureAwait(false);
        _roots = _roots!.Where(r => !SamePath(r.Path, root.Path)).ToArray();
    }, token);

    public async Task LocateAsync(string oldPath, string selectedFolderPath, CancellationToken token = default)
    {
        var selected = RootFolderPath.Normalize(selectedFolderPath);
        if (await availability.CheckAsync(selected, token).ConfigureAwait(false) != RootFolderAvailability.Available)
            throw new InvalidOperationException("The selected replacement folder is not accessible.");
        await MutateAsync(async () =>
        {
            var old = Require(oldPath);
            if (!SamePath(old.Path, selected) && Find(selected) is not null)
                throw new InvalidOperationException("That folder already has a saved entry. No entries were merged or removed.");
            var replacement = old with { Path = selected };
            await Task.Run(() => store.RelocateAsync(old.Path, replacement, token), token).ConfigureAwait(false);
            _roots = _roots!.Select(r => SamePath(r.Path, old.Path) ? replacement : r).ToArray();
        }, token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RootFolderStatus>> CheckAvailabilityAsync(CancellationToken token = default, IProgress<RootFolderStatus>? progress = null)
    {
        RootFolderHistory[] roots;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { await EnsureLoadedAsync(token).ConfigureAwait(false); roots = _roots!.ToArray(); }
        finally { _gate.Release(); }
        var statuses = new RootFolderStatus[roots.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, roots.Length),
            new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = token }, async (index, cancellation) =>
            {
                var root = roots[index];
                var status = await availability.CheckAsync(root.Path, cancellation).ConfigureAwait(false);
                statuses[index] = new(root, status);
                progress?.Report(statuses[index]);
            }).ConfigureAwait(false);
        return statuses;
    }

    // Shutdown waits for already-started mutations; no UI callback is required.
    public async Task FlushAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
    }

    private async Task MutateAsync(Func<Task> change, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { await EnsureLoadedAsync(token).ConfigureAwait(false); await change().ConfigureAwait(false); }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }
    private async Task EnsureLoadedAsync(CancellationToken token)
    {
        if (_roots is null) _roots = await Task.Run(() => store.LoadAsync(token), token).ConfigureAwait(false);
    }
    private async Task SaveAsync(RootFolderHistory root, CancellationToken token)
    {
        await Task.Run(() => store.SaveAsync(root, token), token).ConfigureAwait(false);
        _roots = _roots!.Where(r => !SamePath(r.Path, root.Path)).Append(root).ToArray();
    }
    private RootFolderHistory? Find(string path) => _roots!.FirstOrDefault(r => SamePath(r.Path, path));
    private RootFolderHistory Require(string path) => Find(RootFolderPath.Normalize(path))
        ?? throw new KeyNotFoundException("This path has no saved root entry.");
    private static bool SamePath(string left, string right) => StringComparer.OrdinalIgnoreCase.Equals(left, right);
}
