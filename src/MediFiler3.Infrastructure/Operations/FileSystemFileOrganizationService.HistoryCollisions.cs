using System.IO;
using System.Text.Json;
using MediFiler3.Application.Operations;
using MediFiler3.Domain.Operations;

namespace MediFiler3.Infrastructure.Operations;

public sealed partial class FileSystemFileOrganizationService
{
    private sealed record HistoryDestination(FileOrganizationResult? Failure, PreservedCollisionItem? Displaced);

    private async Task<HistoryDestination> ResolveHistoryDestinationAsync(JournaledFileOperation entry,
        string? current, string target, bool directory, CancellationToken token)
    {
        if (!File.Exists(target) && !Directory.Exists(target)) return new(null, null);
        if (collisionResolver is null)
            return new(new(FileOrganizationOutcome.Conflict, $"Cannot continue because '{target}' already exists. Nothing was overwritten.", entry.Operation.Id), null);
        var identity = RequireIdentity(target);
        var details = InspectCollision(current, target, directory, history: true);
        while (true)
        {
            var answer = await collisionResolver.ResolveAsync(details, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (answer.Choice == FileCollisionChoice.Cancel) return new(Cancelled(), null);
            string? name = null;
            if (answer.Choice == FileCollisionChoice.Rename)
            {
                ValidateLeafName(answer.NewName!);
                name = answer.NewName;
                var proposed = Path.Combine(Path.GetDirectoryName(target)!, name!);
                if (File.Exists(proposed) || Directory.Exists(proposed))
                {
                    details = details with { Explanation = $"'{name}' also exists. Choose a different name.",
                        SuggestedName = InspectCollision(current, target, directory, history: true).SuggestedName };
                    continue;
                }
            }
            else
            {
                if (!details.CanOverwrite) return new(new(FileOrganizationOutcome.Conflict, "These items cannot safely replace each other."), null);
                ValidateOverwrite(new(FileOperationKind.Move, directory, current, target), identity);
            }
            RejectReparsePoint(target);
            if (Directory.Exists(target)) { EnsureNotVolumeRoot(target); EnsureNoReparsePoints(target); }
            var attributes = (int)File.GetAttributes(target);
            var preserved = await PreserveDestinationAsync(target, identity, entry.Operation.BatchId,
                entry.Operation.Id, token, name).ConfigureAwait(false);
            return preserved.Succeeded
                ? new(null, new(target, preserved.AffectedPath!, details.ExistingIsDirectory, identity, attributes))
                : new(preserved, null);
        }
    }

    private static OperationEventDetails? CurrentHistoryDetails(JournaledFileOperation entry)
    {
        var last = entry.Events.LastOrDefault(e => e.Kind == entry.State &&
            e.Kind is FileOperationEventKind.Undone or FileOperationEventKind.Redone);
        if (last?.DetailsJson is null) return null;
        try { return JsonSerializer.Deserialize<OperationEventDetails>(last.DetailsJson); }
        catch (JsonException) { return null; }
    }

    private async Task<string?> RestoreDisplacedAsync(FileOperationRecord owner, PreservedCollisionItem? item, CancellationToken token)
    {
        try { return await RestoreDisplacedCoreAsync(owner, item, token).ConfigureAwait(false); }
        catch (Exception exception) when (IsFileSystemException(exception))
        { return $"Could not restore preserved data from '{item?.BackupPath}': {exception.Message}"; }
    }

    private async Task<string?> RestoreDisplacedCoreAsync(FileOperationRecord owner, PreservedCollisionItem? item, CancellationToken token)
    {
        if (item is null) return null;
        if (File.Exists(item.OriginalPath) || Directory.Exists(item.OriginalPath)) return $"'{item.OriginalPath}' is occupied; preserved data remains at '{item.BackupPath}'.";
        if (RequireIdentity(item.BackupPath) != item.Identity) return $"The preserved item at '{item.BackupPath}' changed.";
        var restore = CreateOperation(FileOperationKind.Rename, item.BackupPath, item.OriginalPath,
            new PathOperationPayload(item.IsDirectory, false, item.Identity), owner.BatchId, owner.Id);
        await journal.AppendPreparedAsync(restore, token).ConfigureAwait(false);
        var moved = false;
        try
        {
            if (item.IsDirectory) Directory.Move(item.BackupPath, item.OriginalPath); else File.Move(item.BackupPath, item.OriginalPath);
            moved = true;
            File.SetAttributes(item.OriginalPath, (FileAttributes)item.Attributes);
            var verified = await VerifySameVolumeMoveAsync(restore, item.BackupPath, item.OriginalPath, item.Identity, token).ConfigureAwait(false);
            return verified.Succeeded ? null : verified.Message;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            if (moved)
            {
                await AppendEventAsync(restore, FileOperationEventKind.RecoveryRequired, exception.Message, CancellationToken.None).ConfigureAwait(false);
                return $"Preserved data returned to '{item.OriginalPath}', but its attributes/result require review: {exception.Message}";
            }
            var outcome = await ClassifyMoveFailureAsync(restore, item.BackupPath, item.OriginalPath, item.Identity, exception, token).ConfigureAwait(false);
            return outcome.Succeeded ? null : outcome.Message;
        }
    }

    private async Task<FileOrganizationResult> HistoryRecoveryAsync(FileOperationRecord operation, string message)
    {
        await journal.AppendEventAsync(operation.Id, FileOperationEventKind.RecoveryRequired,
            JsonSerializer.Serialize(new OperationEventDetails(message)), CancellationToken.None).ConfigureAwait(false);
        return RecoveryRequired(message, operation.Id);
    }

    private static bool IsReplacementBackup(JournaledFileOperation entry) =>
        entry.Operation.Kind is FileOperationKind.Move or FileOperationKind.Rename &&
        JsonSerializer.Deserialize<PathOperationPayload>(entry.Operation.PayloadJson ?? "{}")?.IsReplacementBackup == true;
}
