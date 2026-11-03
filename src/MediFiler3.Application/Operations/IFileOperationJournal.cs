using MediFiler3.Domain.Operations;

namespace MediFiler3.Application.Operations;

/// <summary>Append-only persistence boundary for filesystem operation history.</summary>
public interface IFileOperationJournal
{
    Task AppendPreparedAsync(FileOperationRecord operation, CancellationToken cancellationToken = default);

    Task AppendEventAsync(
        Guid operationId,
        FileOperationEventKind kind,
        string? detailsJson = null,
        CancellationToken cancellationToken = default);

    Task<JournaledFileOperation?> GetAsync(Guid operationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JournaledFileOperation>> GetIncompleteAsync(CancellationToken cancellationToken = default);

    Task<JournaledFileOperation?> GetNextUndoAsync(CancellationToken cancellationToken = default);

    Task<JournaledFileOperation?> GetNextRedoAsync(CancellationToken cancellationToken = default);
}

public interface IFileOperationRecoveryInspector
{
    Task<OperationRecoveryAssessment> AssessAsync(
        JournaledFileOperation operation,
        CancellationToken cancellationToken = default);
}

public sealed record FileIdentityLookup(bool Exists, bool Accessible, string? Identity);

public interface IFileIdentityProvider
{
    FileIdentityLookup GetIdentity(string path);
}
