namespace MediFiler3.Application.Operations;

public interface IFileOperationBackupStore
{
    string AllocatePath(string originalPath);
}

public sealed record BackupCleanupResult(int RemovedCount, IReadOnlyList<string> Issues);

public sealed record OverwriteBackupItem(Guid OperationId, string OriginalPath, string BackupPath,
    bool IsDirectory, DateTimeOffset SavedUtc, bool CanRemove, string? RetainedReason)
{
    public DateTimeOffset ExpiresUtc => SavedUtc.AddDays(30);
}

public interface IOperationBackupMaintenance
{
    event Action? Changed;
    Task<IReadOnlyList<OverwriteBackupItem>> GetBackupsAsync(CancellationToken cancellationToken = default);
    Task RevealInExplorerAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<BackupCleanupResult> ClearAsync(CancellationToken cancellationToken = default);
    Task<BackupCleanupResult> ClearAsync(IReadOnlyCollection<Guid> operationIds, CancellationToken cancellationToken = default);
    Task<BackupCleanupResult> ClearExpiredAsync(CancellationToken cancellationToken = default);
}
