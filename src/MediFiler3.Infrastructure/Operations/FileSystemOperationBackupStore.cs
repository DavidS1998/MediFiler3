using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediFiler3.Application.Operations;
using MediFiler3.Domain.Operations;

namespace MediFiler3.Infrastructure.Operations;

public sealed partial class FileSystemOperationBackupStore(IFileOperationJournal journal, IFileIdentityProvider identities,
    string? storageDirectory = null, TimeProvider? timeProvider = null) : IFileOperationBackupStore, IOperationBackupMaintenance
{
    public event Action? Changed;
    private readonly string _primary = Path.GetFullPath(storageDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediFiler3", "UndoBackups"));
    private static readonly string VolumeFolder = CreateVolumeFolderName();

    private static string CreateVolumeFolderName()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return ".MediFiler3-UndoBackups-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.User!.Value)))[..16];
    }

    public string AllocatePath(string originalPath)
    {
        var original = Path.GetFullPath(originalPath);
        var volume = RequireIdentity(original).Split(':')[0];
        var primaryAncestor = new DirectoryInfo(_primary);
        while (!primaryAncestor.Exists && primaryAncestor.Parent is not null) primaryAncestor = primaryAncestor.Parent;
        var root = RequireIdentity(primaryAncestor.FullName).Split(':')[0] == volume && !Within(_primary, original)
            ? _primary : Path.Combine(Path.GetPathRoot(original)!, VolumeFolder);
        EnsureSafeParents(root);
        Directory.CreateDirectory(root);
        EnsureSafeParents(root);
        if (RequireIdentity(root).Split(':')[0] != volume)
            throw new IOException("An app-managed backup folder on the destination volume is required for this replacement.");
        File.SetAttributes(root, File.GetAttributes(root) | FileAttributes.Hidden);
        var extension = File.GetAttributes(original).HasFlag(FileAttributes.Directory) ? string.Empty : Path.GetExtension(original);
        return Path.Combine(root, Guid.NewGuid().ToString("N") + extension);
    }

    public Task<BackupCleanupResult> ClearAsync(CancellationToken cancellationToken = default) =>
        RunCleanupAsync(null, null, cancellationToken);

    public Task<BackupCleanupResult> ClearAsync(IReadOnlyCollection<Guid> operationIds, CancellationToken cancellationToken = default) =>
        RunCleanupAsync(operationIds.ToHashSet(), null, cancellationToken);

    public Task<BackupCleanupResult> ClearExpiredAsync(CancellationToken cancellationToken = default) =>
        RunCleanupAsync(null, (timeProvider ?? TimeProvider.System).GetUtcNow().AddDays(-30), cancellationToken);

    private async Task<BackupCleanupResult> RunCleanupAsync(HashSet<Guid>? ids, DateTimeOffset? cutoff, CancellationToken token)
    {
        try { return await Task.Run(() => OperationMaintenanceGate.RunAsync(() => ClearCoreAsync(ids, cutoff, token), token), token).ConfigureAwait(false); }
        finally { Changed?.Invoke(); } // Partial/cancelled cleanup may already have retired history.
    }

    private async Task<BackupCleanupResult> ClearCoreAsync(HashSet<Guid>? ids, DateTimeOffset? cutoff, CancellationToken token)
    {
        var entries = await journal.GetAllAsync(token).ConfigureAwait(false);
        var removed = 0;
        var issues = new List<string>();
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            var operation = entry.Operation;
            if (ids is not null && !ids.Contains(operation.Id) || cutoff is { } before && SavedUtc(entry) > before) continue;
            if (operation.Kind is not (FileOperationKind.Move or FileOperationKind.Rename)) continue;
            PathOperationPayload? payload;
            try { payload = JsonSerializer.Deserialize<PathOperationPayload>(operation.PayloadJson ?? "{}"); }
            catch (JsonException) { continue; }
            if (payload?.IsReplacementBackup != true || operation.DestinationPath is not { } path) continue;
            try
            {
                var identity = identities.GetIdentity(path);
                if (!identity.Exists) continue; // Undo already returned this item to the user's folder.
                if (!identity.Accessible || identity.Identity != payload.SourceIdentity)
                    throw new IOException("Backup identity changed or cannot be verified; it was retained.");
                if (operation.PayloadVersion != 1 || File.GetAttributes(path).HasFlag(FileAttributes.Directory) != payload.IsDirectory)
                    throw new IOException("Backup format or item type is unsupported; it was retained.");
                if (entry.State is not (FileOperationEventKind.Completed or FileOperationEventKind.Redone or
                    FileOperationEventKind.RedoInvalidated or FileOperationEventKind.HistoryDiscarded))
                    throw new IOException("Backup has an unresolved operation and was retained for recovery.");
                ValidateBackupPath(path);
                EnsureSafeTree(path, token);
                // Retire the entire owning group durably BEFORE deleting any data. A crash
                // or locked file can leave an extra backup, never an enabled broken Undo.
                await journal.DiscardHistoryAsync(operation.Id, token).ConfigureAwait(false);
                if (RequireIdentity(path) != payload.SourceIdentity) throw new IOException("Backup changed before cleanup.");
                DeleteOwnedItem(path, payload.IsDirectory);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or System.Security.SecurityException)
            { issues.Add($"{path}: {exception.Message}"); }
        }
        return new(removed, issues);
    }

    private void ValidateBackupPath(string path)
    {
        var full = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(full)!;
        var leaf = Path.GetFileName(full);
        var managed = (Guid.TryParseExact(leaf, "N", out _) || Guid.TryParseExact(Path.GetFileNameWithoutExtension(leaf), "N", out _)) &&
            (parent.Equals(_primary, StringComparison.OrdinalIgnoreCase) ||
             parent.Equals(Path.Combine(Path.GetPathRoot(full)!, VolumeFolder), StringComparison.OrdinalIgnoreCase));
        var legacy = leaf.StartsWith(".MediFiler3-Undo-", StringComparison.Ordinal) &&
            Guid.TryParseExact(leaf[17..], "N", out _);
        if (!managed && !legacy) throw new IOException("Path is outside managed backup storage; it was retained.");
        EnsureSafeParents(parent);
    }

    private static bool Within(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private string RequireIdentity(string path) => identities.GetIdentity(path) is { Exists: true, Accessible: true, Identity: { } id }
        ? id : throw new IOException($"Cannot verify '{path}'.");

    private static void EnsureSafeParents(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Backup storage cannot traverse symbolic links, junctions or mount points.");
    }

    private static void EnsureSafeTree(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("A backup contains a link and was retained.");
        if (attributes.HasFlag(FileAttributes.Directory))
            foreach (var child in Directory.EnumerateFileSystemEntries(path)) EnsureSafeTree(child, token);
    }

    private static void DeleteOwnedItem(string path, bool directory)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Backup changed into a link during cleanup; it was retained.");
        // Every descendant was checked for reparse points before this traversal.
        if (directory)
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
                DeleteOwnedItem(child, File.GetAttributes(child).HasFlag(FileAttributes.Directory));
            File.SetAttributes(path, FileAttributes.Directory);
            Directory.Delete(path);
        }
        else
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }
}
