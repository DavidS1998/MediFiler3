using System.IO;
using MediFiler3.Application.Folders;

namespace MediFiler3.Infrastructure.Folders;

public sealed class FileSystemFolderChangeMonitor : IFolderChangeMonitor
{
    private static readonly SemaphoreSlim ProbeGate = new(2, 2);
    public static IReadOnlyList<string> ReduceRoots(IEnumerable<string> roots)
    {
        var paths = roots.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Length).ToArray();
        return paths.Where(p => !paths.Any(other => other.Length < p.Length && Within(p, other))).ToArray();
    }

    private static bool Within(string path, string root) => string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public async Task RunAsync(IReadOnlyList<string> roots, Func<string?> activeFolder,
        Func<FolderChangeBatch, CancellationToken, Task> reconcile, CancellationToken token)
    {
        var states = ReduceRoots(roots).Select(p => new WatchState(p)).ToArray();
        var buffer = new FolderChangeBuffer(states.Select(s => s.Path).ToArray());
        void Queue(string path, WatchState state, bool tree, bool overflow = false, bool structural = false)
        {
            if (token.IsCancellationRequested) return;
            // Reconciliation retains hierarchy reparse/hidden-backup exclusions.
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediFiler3");
            if (Within(path, Path.Combine(appData, "ThumbnailCache")) || Within(path, Path.Combine(appData, "UndoBackups"))) return;
            buffer.Add(path, tree, DateTimeOffset.UtcNow, overflow,
                overflow ? "Filesystem notifications interrupted; rescanning loaded folders." : null, structural);
        }
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                foreach (var state in states)
                {
                    if (state.RetryAt > DateTimeOffset.UtcNow) continue;
                    await ProbeGate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                    token.ThrowIfCancellationRequested();
                    if (state.Watcher is not null && !state.Failed && Directory.Exists(state.Path)) { state.RetryAt = DateTimeOffset.UtcNow.AddSeconds(5); continue; }
                    state.Watcher?.Dispose();
                    state.Watcher = null;
                    try
                    {
                        if ((File.GetAttributes(state.Path) & FileAttributes.ReparsePoint) != 0)
                            throw new IOException("Linked roots cannot be watched recursively.");
                        var watcher = new FileSystemWatcher(state.Path)
                        {
                            IncludeSubdirectories = true,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes
                        };
                        state.Watcher = watcher;
                        watcher.Changed += (_, e) =>
                        {
                            // Directory timestamp/attribute hints do not imply changed pixels in every descendant.
                            // Exact file hints and overflow/recovery retain strong freshness invalidation.
                            if (Directory.Exists(e.FullPath)) buffer.ReconcileRoot(state.Path, DateTimeOffset.UtcNow);
                            else Queue(e.FullPath, state, false);
                        };
                        watcher.Created += (_, e) => Queue(e.FullPath, state, true, structural: true);
                        watcher.Deleted += (_, e) => Queue(e.FullPath, state, true, structural: true);
                        watcher.Renamed += (_, e) => { Queue(e.OldFullPath, state, true, structural: true); Queue(e.FullPath, state, true, structural: true); };
                        watcher.Error += (_, _) => { state.RetryAt = DateTimeOffset.MinValue; state.Failed = true; Queue(state.Path, state, true, true); };
                        watcher.EnableRaisingEvents = true;
                        var recovering = state.Failed || state.HasConnected;
                        state.Failed = false;
                        state.HasConnected = true;
                        state.Delay = 1;
                        state.RetryAt = DateTimeOffset.UtcNow.AddSeconds(5);
                        if (recovering) Queue(state.Path, state, true, true);
                        else buffer.ReconcileRoot(state.Path, DateTimeOffset.UtcNow);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
                    {
                        state.Watcher?.Dispose(); state.Watcher = null;
                        state.Failed = true;
                        state.RetryAt = DateTimeOffset.UtcNow.AddSeconds(state.Delay);
                        state.Delay = Math.Min(60, state.Delay * 2);
                        buffer.Add(state.Path, false, DateTimeOffset.UtcNow, status:
                            "Live updates unavailable for some roots; retrying. Active folder polling and F5 remain available.");
                        if (activeFolder() is { } active && Within(active, state.Path)) Queue(active, state, false);
                    }
                    }
                    finally { ProbeGate.Release(); }
                }
                var batch = buffer.Take(DateTimeOffset.UtcNow);
                if (batch is not null) await reconcile(batch, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { foreach (var state in states) state.Watcher?.Dispose(); }
    }

    private sealed class WatchState(string path)
    {
        public string Path { get; } = path;
        public FileSystemWatcher? Watcher;
        public volatile bool Failed;
        public bool HasConnected;
        public int Delay = 1;
        public DateTimeOffset RetryAt;
    }
}
