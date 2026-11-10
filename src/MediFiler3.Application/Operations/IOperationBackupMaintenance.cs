namespace MediFiler3.Application.Operations;

public interface IFileOperationBackupStore
{
    string AllocatePath(string originalPath);
}

public sealed record BackupCleanupResult(int RemovedCount, IReadOnlyList<string> Issues);

public interface IOperationBackupMaintenance
{
    Task<BackupCleanupResult> ClearAsync(CancellationToken cancellationToken = default);
}
