namespace MediFiler3.Application.Folders;

public sealed record FolderChangeBatch(IReadOnlyList<string> Paths, IReadOnlyList<string> RescanRoots,
    bool HierarchyChanged, string? Status = null, IReadOnlyList<string>? ReconcileRoots = null);

public interface IFolderChangeMonitor
{
    // Callback is awaited: producers coalesce into bounded state while the consumer is busy.
    Task RunAsync(IReadOnlyList<string> roots, Func<string?> activeFolder,
        Func<FolderChangeBatch, CancellationToken, Task> reconcile, CancellationToken token);
}
