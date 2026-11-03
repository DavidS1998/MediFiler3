namespace MediFiler3.Application.Operations;

public enum RecycleBinRestoreOutcome
{
    Restored,
    NotFound,
    Ambiguous,
    IdentityMismatch,
    Failed
}

public sealed record RecycleBinEntryDescriptor(string OriginalPath, string? ItemIdentity, bool IsDirectory);

public sealed record RecycleBinRestoreResult(RecycleBinRestoreOutcome Outcome, string Message,
    bool RestoredContentVerified = false)
{
    public bool Succeeded => Outcome == RecycleBinRestoreOutcome.Restored;
}

/// <summary>Restores one uniquely identified item through the Windows Shell Recycle Bin namespace.</summary>
public interface IRecycleBinRestoreService
{
    Task<RecycleBinRestoreResult> RestoreAsync(string originalPath, string itemIdentity, bool isDirectory,
        CancellationToken cancellationToken = default);
}

