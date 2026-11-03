using System.Text.Json;
using MediFiler3.Application.Operations;
using MediFiler3.Domain.Operations;

namespace MediFiler3.Infrastructure.Persistence;

/// <summary>
/// Uses stable Windows file IDs for same-volume moves. Cases without enough durable
/// identity evidence remain ambiguous; existence alone is never treated as proof.
/// </summary>
public sealed class FileSystemOperationRecoveryInspector(IFileIdentityProvider identities) : IFileOperationRecoveryInspector
{
    public Task<OperationRecoveryAssessment> AssessAsync(
        JournaledFileOperation operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = operation.Operation;
        var explanation = "Filesystem state does not contain enough durable evidence to determine the outcome safely.";

        if (record.Kind is FileOperationKind.Move or FileOperationKind.Rename &&
            !string.IsNullOrWhiteSpace(record.SourcePath) &&
            !string.IsNullOrWhiteSpace(record.DestinationPath) &&
            TryDeserialize<PathOperationPayload>(record.PayloadJson, out var pathPayload) &&
            pathPayload is { CrossVolume: false, SourceIdentity: not null })
        {
            var source = identities.GetIdentity(record.SourcePath);
            var destination = identities.GetIdentity(record.DestinationPath);
            if (source is { Exists: false } && destination is { Exists: true, Accessible: true } &&
                destination.Identity == pathPayload.SourceIdentity)
            {
                return Task.FromResult(new OperationRecoveryAssessment(
                    RecoveryConclusion.Completed,
                    "The original source is absent and the destination has the recorded source file identity."));
            }

            if (source is { Exists: true, Accessible: true } && source.Identity == pathPayload.SourceIdentity &&
                destination is { Exists: false })
            {
                return Task.FromResult(new OperationRecoveryAssessment(
                    RecoveryConclusion.Failed,
                    "The original source still has its recorded identity and the destination is absent."));
            }
        }

        if (record.Kind == FileOperationKind.CreateFolder &&
            !string.IsNullOrWhiteSpace(record.DestinationPath))
        {
            var expectedFolderIdentity = TryDeserialize<CreateFolderPayload>(record.PayloadJson, out var folderPayload)
                ? folderPayload?.CreatedFolderIdentity
                : null;
            var verificationEvent = operation.Events.LastOrDefault(x => x.Kind == FileOperationEventKind.DestinationVerified);
            if (expectedFolderIdentity is null && verificationEvent is not null &&
                TryDeserialize<OperationEventDetails>(verificationEvent.DetailsJson, out var creationDetails))
                expectedFolderIdentity = creationDetails?.VerifiedDestinationIdentity;

            if (expectedFolderIdentity is not null &&
                identities.GetIdentity(record.DestinationPath) is { Exists: true, Accessible: true } folder &&
                folder.Identity == expectedFolderIdentity)
            {
                return Task.FromResult(new OperationRecoveryAssessment(
                    RecoveryConclusion.Completed,
                    "The destination folder has the identity recorded after creation."));
            }
        }

        if (record.Kind == FileOperationKind.Move &&
            TryDeserialize<PathOperationPayload>(record.PayloadJson, out var crossVolumePayload) &&
            crossVolumePayload is { CrossVolume: true } &&
            operation.Events.Any(x => x.Kind == FileOperationEventKind.DestinationVerified))
        {
            var verification = operation.Events.Last(x => x.Kind == FileOperationEventKind.DestinationVerified);
            var details = TryDeserialize<OperationEventDetails>(verification.DetailsJson, out var parsed) ? parsed : null;
            var destinationIdentity = record.DestinationPath is null ? null : identities.GetIdentity(record.DestinationPath);
            var sourceIdentity = record.SourcePath is null ? null : identities.GetIdentity(record.SourcePath);
            if (destinationIdentity is { Exists: true, Accessible: true } &&
                destinationIdentity.Identity == details?.VerifiedDestinationIdentity &&
                sourceIdentity is { Exists: false })
            {
                return Task.FromResult(new OperationRecoveryAssessment(
                    RecoveryConclusion.Completed,
                    "The verified destination identity remains present and the original source is absent."));
            }
        }

        return Task.FromResult(new OperationRecoveryAssessment(RecoveryConclusion.Ambiguous, explanation));
    }

    private static bool TryDeserialize<T>(string? json, out T? value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            value = JsonSerializer.Deserialize<T>(json);
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

}
