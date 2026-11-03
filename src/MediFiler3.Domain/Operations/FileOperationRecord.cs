namespace MediFiler3.Domain.Operations;

public enum FileOperationKind { Move, Rename, CreateFolder, SendToRecycleBin }

/// <summary>Events are append-only. The most recent event describes the current state.</summary>
public enum FileOperationEventKind
{
    Prepared,
    CopyStarted,
    DestinationVerified,
    SourceRemoved,
    Completed,
    Failed,
    RecoveryRequired,
    Undone,
    Redone,
    RedoInvalidated
}

public sealed record FileOperationRecord(
    Guid Id,
    Guid? BatchId,
    FileOperationKind Kind,
    string? SourcePath,
    string? DestinationPath,
    DateTimeOffset CreatedUtc,
    int PayloadVersion,
    string? PayloadJson,
    Guid? CompensatesOperationId = null);

public sealed record FileOperationEvent(
    long Sequence,
    Guid OperationId,
    FileOperationEventKind Kind,
    DateTimeOffset OccurredUtc,
    string? DetailsJson);

public sealed record JournaledFileOperation(
    FileOperationRecord Operation,
    IReadOnlyList<FileOperationEvent> Events)
{
    public FileOperationEventKind State => Events[^1].Kind;
}

/// <summary>Typed, versioned payloads are serialized into the operation's JSON payload.</summary>
public abstract record FileOperationPayload;

public sealed record PathOperationPayload(
    bool IsDirectory,
    bool CrossVolume,
    string? SourceIdentity = null) : FileOperationPayload;

public sealed record CreateFolderPayload(
    string? ParentIdentity = null,
    string? CreatedFolderIdentity = null) : FileOperationPayload;

public sealed record RecycleBinPayload(
    string? RestoreToken = null,
    string? OriginalVolume = null,
    string? OriginalItemId = null) : FileOperationPayload;

public sealed record OperationEventDetails(
    string? Message = null,
    string? VerifiedDestinationIdentity = null,
    string? RestoreToken = null,
    int? PayloadVersion = null,
    string? PayloadJson = null);

public enum RecoveryConclusion { Completed, Failed, Ambiguous }

public sealed record OperationRecoveryAssessment(
    RecoveryConclusion Conclusion,
    string Explanation,
    string? EvidenceJson = null);
