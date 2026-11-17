using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MediFiler3.Application.Operations;
using MediFiler3.Domain.Operations;

namespace MediFiler3.Infrastructure.Operations;

public sealed partial class FileSystemOperationBackupStore
{
    // File timestamps belong to the original data, not to its time in backup storage.
    private static DateTimeOffset SavedUtc(JournaledFileOperation entry) => entry.Events
        .Where(item => item.Kind is FileOperationEventKind.Completed or FileOperationEventKind.Redone)
        .Select(item => item.OccurredUtc).DefaultIfEmpty(entry.Operation.CreatedUtc).Max();

    public Task<IReadOnlyList<OverwriteBackupItem>> GetBackupsAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => OperationMaintenanceGate.RunAsync(() => GetBackupsCoreAsync(cancellationToken), cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<OverwriteBackupItem>> GetBackupsCoreAsync(CancellationToken token)
    {
        var entries = await journal.GetAllAsync(token).ConfigureAwait(false);
        var history = new BackupHistoryView(entries);
        var result = new List<OverwriteBackupItem>();
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            var operation = entry.Operation;
            if (operation.Kind is not (FileOperationKind.Move or FileOperationKind.Rename) || operation.DestinationPath is not { } path) continue;
            PathOperationPayload? payload;
            try { payload = JsonSerializer.Deserialize<PathOperationPayload>(operation.PayloadJson ?? "{}"); }
            catch (JsonException) { continue; }
            if (payload?.IsReplacementBackup != true) continue;
            string? retained = null;
            try
            {
                var identity = identities.GetIdentity(path);
                if (!identity.Exists && identity.Accessible) continue;
                if (!identity.Exists || !identity.Accessible || identity.Identity != payload.SourceIdentity)
                    throw new IOException("Backup identity cannot be verified; retained.");
                if (operation.PayloadVersion != 1 || File.GetAttributes(path).HasFlag(FileAttributes.Directory) != payload.IsDirectory)
                    throw new IOException("Unsupported backup format or changed item type; retained.");
                if (entry.State is not (FileOperationEventKind.Completed or FileOperationEventKind.Redone or
                    FileOperationEventKind.RedoInvalidated or FileOperationEventKind.HistoryDiscarded))
                    throw new IOException("Unresolved operation; retained for recovery.");
                ValidateBackupPath(path);
                EnsureSafeTree(path, token);
                history.Validate(entry, token);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
            { retained = exception.Message; }
            result.Add(new(operation.Id, operation.SourcePath ?? path, path, payload.IsDirectory, SavedUtc(entry), retained is null, retained));
        }
        return result.OrderBy(item => item.SavedUtc).ThenBy(item => item.OriginalPath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private sealed class BackupHistoryView(IReadOnlyList<JournaledFileOperation> entries)
    {
        private readonly Dictionary<Guid, JournaledFileOperation> _byId = entries.ToDictionary(item => item.Operation.Id);
        private readonly ILookup<(string?, Guid, bool), JournaledFileOperation> _originals = entries
            .Where(item => item.Operation.CompensatesOperationId is null).ToLookup(Key);
        private readonly ILookup<(string?, Guid), JournaledFileOperation> _children = entries
            .Where(item => item.Operation.CompensatesOperationId is not null)
            .ToLookup(item => (item.Operation.RootScope, item.Operation.CompensatesOperationId!.Value));
        private readonly Dictionary<(string?, Guid, bool), string?> _checked = [];
        private static (string?, Guid, bool) Key(JournaledFileOperation entry) =>
            (entry.Operation.RootScope, entry.Operation.BatchId ?? entry.Operation.Id, entry.Operation.BatchId.HasValue);

        public void Validate(JournaledFileOperation entry, CancellationToken token)
        {
            var route = new HashSet<Guid>();
            while (entry.Operation.CompensatesOperationId is { } parent)
            {
                token.ThrowIfCancellationRequested();
                if (!route.Add(entry.Operation.Id) || !_byId.TryGetValue(parent, out var ancestor) || ancestor.Operation.RootScope != entry.Operation.RootScope)
                    throw new IOException("Incomplete backup ownership; retained.");
                entry = ancestor;
            }
            var key = Key(entry);
            if (!_checked.TryGetValue(key, out var reason))
            {
                var group = _originals[key].ToList();
                var ids = group.Select(item => item.Operation.Id).ToHashSet();
                for (var index = 0; index < group.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var child in _children[(entry.Operation.RootScope, group[index].Operation.Id)])
                        if (ids.Add(child.Operation.Id)) group.Add(child);
                }
                reason = group.Any(item => item.State is not (FileOperationEventKind.Completed or FileOperationEventKind.Redone or
                    FileOperationEventKind.Undone or FileOperationEventKind.RedoInvalidated or FileOperationEventKind.HistoryDiscarded or FileOperationEventKind.Failed))
                    ? "History group needs recovery; retained." : null;
                _checked[key] = reason;
            }
            if (reason is not null) throw new IOException(reason);
        }
    }

    public Task RevealInExplorerAsync(Guid operationId, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        var entry = await journal.GetAsync(operationId, cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("The backup no longer exists in the journal.");
        var path = entry.Operation.DestinationPath ?? throw new IOException("Backup location is unavailable.");
        var payload = JsonSerializer.Deserialize<PathOperationPayload>(entry.Operation.PayloadJson ?? "{}");
        if (payload?.IsReplacementBackup != true) throw new IOException("This item is not an overwrite backup.");
        ValidateBackupPath(path);
        var identity = identities.GetIdentity(path);
        if (!identity.Exists || !identity.Accessible || identity.Identity != payload.SourceIdentity)
            throw new IOException("The stored backup could not be verified.");
        cancellationToken.ThrowIfCancellationRequested();
        // Reveal the saved item rather than opening/executing its contents.
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/select," + path);
        Process.Start(start)?.Dispose();
    }, cancellationToken);
}
