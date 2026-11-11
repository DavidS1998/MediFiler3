using System.Security.Cryptography;
using System.Text.Json;
using MediFiler3.Application.Operations;
using MediFiler3.Domain.Operations;
using Microsoft.VisualBasic.FileIO;
using System.IO;
using System.Runtime.InteropServices;

namespace MediFiler3.Infrastructure.Operations;

public sealed partial class FileSystemFileOrganizationService(
    IFileOperationJournal journal,
    IFileIdentityProvider identities,
    IRecycleBinRestoreService? recycleBinRestoreService = null,
    IFileCollisionResolver? collisionResolver = null,
    IFileOperationBackupStore? backupStore = null) : IFileOrganizationService
{
    private const int PayloadVersion = 1;
    private readonly IFileOperationBackupStore _backups = backupStore ?? new FileSystemOperationBackupStore(journal, identities);

    public Task<bool> MatchesFileVersionAsync(string path, string identity, long size, DateTime modifiedUtc, CancellationToken token = default) =>
        Task.Run(() =>
        {
            try
            {
                var current = identities.GetIdentity(path);
                if (!current.Exists) return current.Accessible;
                if (!current.Accessible || current.Identity != identity) return false;
                var info = new FileInfo(path);
                return info.Exists && !info.Attributes.HasFlag(FileAttributes.ReparsePoint) && info.Length == size && info.LastWriteTimeUtc == modifiedUtc &&
                    identities.GetIdentity(path).Identity == identity;
            }
            catch (Exception exception) when (IsFileSystemException(exception)) { return false; }
        }, token);

    public async Task<IFileOrganizationService> ForRootAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(rootPath);
        var identity = await Task.Run(() => { EnsureFolder(path); return identities.GetIdentity(path); }, cancellationToken).ConfigureAwait(false);
        if (!identity.Exists || !identity.Accessible || identity.Identity is null)
            throw new IOException("The root folder identity could not be verified.");
        return new FileSystemFileOrganizationService(journal.ForRoot(identity.Identity), identities, recycleBinRestoreService, collisionResolver, _backups);
    }

    private async Task<FileOrganizationResult> MoveFileCoreAsync(
        string sourceFilePath,
        string destinationFolderPath,
        Guid? batchId,
        CancellationToken cancellationToken,
        string? destinationName = null)
    {
        var source = Path.GetFullPath(sourceFilePath);
        var folder = Path.GetFullPath(destinationFolderPath);
        EnsureFile(source);
        EnsureFolder(folder);
        RejectReparsePoint(source);
        var destination = Path.Combine(folder, destinationName ?? Path.GetFileName(source));
        if (PathsEqual(source, destination)) return Success("The file is already in that folder.", null, source);
        EnsureDestinationAvailable(destination);
        var sourceIdentity = RequireIdentity(source);
        var folderIdentity = RequireIdentity(folder);
        var crossVolume = GetVolume(sourceIdentity) != GetVolume(folderIdentity);
        var operation = CreateOperation(FileOperationKind.Move, source, destination,
            new PathOperationPayload(false, crossVolume, sourceIdentity), batchId);
        await journal.AppendPreparedAsync(operation, cancellationToken).ConfigureAwait(false);
        cancellationToken = CancellationToken.None;

        if (!crossVolume)
        {
            try
            {
                File.Move(source, destination);
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                return await ClassifyMoveFailureAsync(operation, source, destination, sourceIdentity, exception,
                    cancellationToken).ConfigureAwait(false);
            }

            return await VerifySameVolumeMoveAsync(operation, source, destination, sourceIdentity,
                cancellationToken).ConfigureAwait(false);
        }

        return await MoveFileAcrossVolumesAsync(operation, source, destination, sourceIdentity, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<FileOrganizationResult> RenameFileCoreAsync(
        string sourceFilePath,
        string newName,
        Guid? batchId, CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(sourceFilePath);
        EnsureFile(source);
        RejectReparsePoint(source);
        ValidateLeafName(newName);
        var destination = Path.Combine(Path.GetDirectoryName(source)!, newName);
        if (PathsEqual(source, destination))
        {
            return Success("The file name is unchanged.", null, source);
        }

        EnsureDestinationAvailable(destination);
        var sourceIdentity = RequireIdentity(source);
        var operation = CreateOperation(FileOperationKind.Rename, source, destination,
            new PathOperationPayload(false, false, sourceIdentity), batchId);
        await journal.AppendPreparedAsync(operation, cancellationToken).ConfigureAwait(false);
        try
        {
            File.Move(source, destination);
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            return await ClassifyMoveFailureAsync(operation, source, destination, sourceIdentity, exception,
                cancellationToken).ConfigureAwait(false);
        }

        return await VerifySameVolumeMoveAsync(operation, source, destination, sourceIdentity, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<FileOrganizationResult> CreateFolderCoreAsync(
        string parentFolderPath,
        string name,
        Guid? batchId, CancellationToken cancellationToken)
    {
        var parent = Path.GetFullPath(parentFolderPath);
        EnsureFolder(parent);
        ValidateLeafName(name);
        var destination = Path.Combine(parent, name);
        EnsureDestinationAvailable(destination);
        var parentIdentity = RequireIdentity(parent);
        var operation = CreateOperation(FileOperationKind.CreateFolder, null, destination,
            new CreateFolderPayload(ParentIdentity: parentIdentity), batchId);
        await journal.AppendPreparedAsync(operation, cancellationToken).ConfigureAwait(false);

        try
        {
            CreateDirectoryExclusively(destination);
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            await AppendEventAsync(operation, FileOperationEventKind.Failed, exception.Message, cancellationToken)
                .ConfigureAwait(false);
            return new FileOrganizationResult(FileOrganizationOutcome.Conflict,
                $"Could not create the folder: {exception.Message}", operation.Id);
        }

        var createdIdentity = identities.GetIdentity(destination);
        if (!createdIdentity.Exists || !createdIdentity.Accessible || createdIdentity.Identity is null)
        {
            await AppendEventAsync(operation, FileOperationEventKind.RecoveryRequired,
                "The created folder identity could not be read.", cancellationToken).ConfigureAwait(false);
            return RecoveryRequired("The folder was created, but its result could not be verified.", operation.Id);
        }

        await AppendEventAsync(operation, FileOperationEventKind.DestinationVerified,
            JsonSerializer.Serialize(new OperationEventDetails(
                "Folder creation verified.", VerifiedDestinationIdentity: createdIdentity.Identity)), cancellationToken)
            .ConfigureAwait(false);
        await AppendEventAsync(operation, FileOperationEventKind.Completed, "{}", cancellationToken).ConfigureAwait(false);
        return Success("Folder created.", operation.Id, destination);
    }

    private async Task<FileOrganizationResult> RenameFolderCoreAsync(
        string sourceFolderPath,
        string newName,
        Guid? batchId, CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(sourceFolderPath);
        EnsureFolder(source);
        EnsureNotVolumeRoot(source);
        RejectReparsePoint(source);
        ValidateLeafName(newName);
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(source))
            ?? throw new IOException("A drive root cannot be renamed.");
        var destination = Path.Combine(parent, newName);
        if (PathsEqual(source, destination)) return Success("The folder name is unchanged.", null, source);
        EnsureDestinationAvailable(destination);
        EnsureNoReparsePoints(source);
        var sourceIdentity = RequireIdentity(source);
        var operation = CreateOperation(FileOperationKind.Rename, source, destination,
            new PathOperationPayload(true, false, sourceIdentity), batchId);
        await journal.AppendPreparedAsync(operation, cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.Move(source, destination);
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            return await ClassifyMoveFailureAsync(operation, source, destination, sourceIdentity, exception,
                cancellationToken).ConfigureAwait(false);
        }

        return await VerifySameVolumeMoveAsync(operation, source, destination, sourceIdentity, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<FileOrganizationResult> MoveFolderCoreAsync(
        string sourceFolderPath,
        string destinationParentPath,
        Guid? batchId, CancellationToken cancellationToken, string? destinationName = null)
    {
        var source = Path.GetFullPath(sourceFolderPath);
        var destinationParent = Path.GetFullPath(destinationParentPath);
        EnsureFolder(source);
        EnsureNotVolumeRoot(source);
        EnsureFolder(destinationParent);
        RejectReparsePoint(source);
        EnsureNoReparsePoints(source);
        var destination = Path.Combine(destinationParent, destinationName ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(source)));
        if (PathsEqual(source, destination)) return Success("The folder is already in that location.", null, source);
        if (IsSameOrChildPath(destinationParent, source))
            return Failure("A folder cannot be moved into itself or one of its descendants.");
        EnsureDestinationAvailable(destination);

        var sourceIdentity = RequireIdentity(source);
        var parentIdentity = RequireIdentity(destinationParent);
        if (GetVolume(sourceIdentity) != GetVolume(parentIdentity))
            return Failure("Moving folders between volumes is not supported yet. Choose a destination on the same volume.");

        var operation = CreateOperation(FileOperationKind.Move, source, destination,
            new PathOperationPayload(true, false, sourceIdentity), batchId);
        await journal.AppendPreparedAsync(operation, cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.Move(source, destination);
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            return await ClassifyMoveFailureAsync(operation, source, destination, sourceIdentity, exception,
                cancellationToken).ConfigureAwait(false);
        }

        return await VerifySameVolumeMoveAsync(operation, source, destination, sourceIdentity, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<FileOrganizationResult> SendToRecycleBinAsync(string path, bool isDirectory,
        bool confirmNonEmptyDirectory = false, CancellationToken cancellationToken = default) =>
        Task.Run(() => OperationMaintenanceGate.RunAsync(() => SendToRecycleBinCoreAsync(path, isDirectory,
            confirmNonEmptyDirectory, cancellationToken), cancellationToken), cancellationToken);

    private async Task<FileOrganizationResult> SendToRecycleBinCoreAsync(
        string path,
        bool isDirectory,
        bool confirmNonEmptyDirectory = false,
        CancellationToken cancellationToken = default, Guid? batchId = null)
    {
        var source = Path.GetFullPath(path);
        if (isDirectory) EnsureFolder(source); else EnsureFile(source);
        if (isDirectory) EnsureNotVolumeRoot(source);
        RejectReparsePoint(source);
        if (isDirectory) EnsureNoReparsePoints(source);
        var nonEmpty = isDirectory && Directory.EnumerateFileSystemEntries(source).Any();
        if (nonEmpty && !confirmNonEmptyDirectory)
            return new FileOrganizationResult(FileOrganizationOutcome.ConfirmationRequired,
                "This folder contains files or subfolders. Confirm sending it and its contents to the Recycle Bin.");

        var sourceIdentity = RequireIdentity(source);
        var operation = CreateOperation(FileOperationKind.SendToRecycleBin, source, null,
            new RecycleBinPayload(OriginalVolume: GetVolume(sourceIdentity), OriginalItemId: sourceIdentity,
                IsDirectory: isDirectory), batchId);
        await journal.AppendPreparedAsync(operation, cancellationToken).ConfigureAwait(false);
        cancellationToken = CancellationToken.None;

        try
        {
            if (isDirectory)
            {
                FileSystem.DeleteDirectory(source, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin,
                    UICancelOption.ThrowException);
            }
            else
            {
                FileSystem.DeleteFile(source, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin,
                    UICancelOption.ThrowException);
            }
        }
        catch (Exception exception) when (IsFileSystemException(exception) || exception is OperationCanceledException)
        {
            var remaining = identities.GetIdentity(source);
            if (remaining is { Exists: true, Accessible: true, Identity: not null } && remaining.Identity == sourceIdentity)
            {
                await AppendEventAsync(operation, FileOperationEventKind.Failed, exception.Message, cancellationToken)
                    .ConfigureAwait(false);
                return exception is OperationCanceledException
                    ? new(FileOrganizationOutcome.Cancelled, "Recycle Bin request cancelled.", operation.Id)
                    : Failure($"Could not send the item to the Recycle Bin: {exception.Message}", operation.Id);
            }

            await AppendEventAsync(operation, FileOperationEventKind.RecoveryRequired,
                "Recycle Bin request failed after the original path changed; restore outcome is unknown.", cancellationToken)
                .ConfigureAwait(false);
            return RecoveryRequired("The item may have moved, but its Recycle Bin state could not be verified.", operation.Id);
        }

        var after = identities.GetIdentity(source);
        if (!after.Exists)
        {
            await AppendEventAsync(operation, FileOperationEventKind.Completed, "{}", cancellationToken).ConfigureAwait(false);
            return Success("Item sent to the Recycle Bin.", operation.Id);
        }

        await AppendEventAsync(operation, FileOperationEventKind.RecoveryRequired,
            "Recycle Bin API returned without removing the original path.", cancellationToken).ConfigureAwait(false);
        return RecoveryRequired("The Recycle Bin operation did not remove the item. It was not permanently deleted.", operation.Id);
    }

    public Task<FileOrganizationBatchResult> MoveFilesAsync(IReadOnlyList<string> sourceFilePaths,
        string destinationFolderPath, CancellationToken cancellationToken = default) =>
        RunFileBatchAsync(sourceFilePaths, (path, batch, token) => ExecutePathRequestAsync(
            new PathRequest(FileOperationKind.Move, false, path,
                Path.Combine(Path.GetFullPath(destinationFolderPath), Path.GetFileName(path))), batch, token), cancellationToken);

    public Task<FileOrganizationBatchResult> SendFilesToRecycleBinAsync(IReadOnlyList<string> sourceFilePaths,
        CancellationToken cancellationToken = default) =>
        RunFileBatchAsync(sourceFilePaths, (path, batch, token) =>
            SendToRecycleBinCoreAsync(path, false, false, token, batch), cancellationToken);

    private Task<FileOrganizationBatchResult> RunFileBatchAsync(IReadOnlyList<string> paths,
        Func<string, Guid, CancellationToken, Task<FileOrganizationResult>> execute, CancellationToken token)
    {
        var snapshot = paths.ToArray();
        return Task.Run(() => OperationMaintenanceGate.RunAsync(async () =>
        {
            if (snapshot.Length == 0) return new FileOrganizationBatchResult(null, []);
            var batch = Guid.NewGuid();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<FileOrganizationResult>();
            foreach (var path in snapshot)
            {
                if (token.IsCancellationRequested)
                    return new FileOrganizationBatchResult(batch, results, snapshot.Length - results.Count, token.IsCancellationRequested || results.Any(item => item.Outcome == FileOrganizationOutcome.Cancelled));
                try
                {
                    var fullPath = Path.GetFullPath(path);
                    results.Add(!seen.Add(fullPath)
                        ? new FileOrganizationResult(FileOrganizationOutcome.Conflict, $"'{fullPath}' appears more than once.")
                        : await execute(fullPath, batch, token).ConfigureAwait(false));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                { results.Add(new(FileOrganizationOutcome.Cancelled, "Operation cancelled.")); }
                catch (Exception exception) when (IsFileSystemException(exception))
                { results.Add(Failure($"Could not process '{path}': {exception.Message}")); }
                if (results[^1].Outcome is FileOrganizationOutcome.Cancelled or FileOrganizationOutcome.RecoveryRequired)
                    break;
            }
            return new FileOrganizationBatchResult(batch, results, snapshot.Length - results.Count, token.IsCancellationRequested || results.Any(item => item.Outcome == FileOrganizationOutcome.Cancelled));
        }, CancellationToken.None));
    }

    public Task<FileOrganizationBatchResult> UndoAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => OperationMaintenanceGate.RunAsync(() => ApplyHistoryAsync(undo: true, cancellationToken), cancellationToken), cancellationToken);

    public Task<FileOrganizationBatchResult> RedoAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => OperationMaintenanceGate.RunAsync(() => ApplyHistoryAsync(undo: false, cancellationToken), cancellationToken), cancellationToken);

    public async Task<FileOrganizationHistoryPreview?> GetHistoryPreviewAsync(
        FileOrganizationHistoryDirection direction, CancellationToken cancellationToken = default)
    {
        var undo = direction == FileOrganizationHistoryDirection.Undo;
        var plan = await GetHistoryPlanAsync(undo, cancellationToken).ConfigureAwait(false);
        if (plan is null) return null;

        var items = plan.Operations.Select(operation => CreateHistoryPreviewItem(operation, undo)).ToArray();
        return new FileOrganizationHistoryPreview(direction, plan.BatchId, items);
    }

    private async Task<FileOrganizationBatchResult> ApplyHistoryAsync(bool undo, CancellationToken cancellationToken)
    {
        var plan = await GetHistoryPlanAsync(undo, cancellationToken).ConfigureAwait(false);
        if (plan is null)
            return new FileOrganizationBatchResult(null, [Failure(undo ? "There is nothing to undo." : "There is nothing to redo.")]);

        var results = new List<FileOrganizationResult>();
        var replacement = plan.Operations.Any(IsReplacementBackup);
        foreach (var entry in plan.Operations)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                var result = await ApplyCompensationAsync(entry, undo, CancellationToken.None).ConfigureAwait(false);
                results.Add(result);
                if (result.Outcome is FileOrganizationOutcome.Cancelled or FileOrganizationOutcome.RecoveryRequired || (replacement && !result.Succeeded)) break;
            }
            catch (Exception exception) when (IsFileSystemException(exception) || exception is JsonException)
            {
                results.Add(Failure($"Could not {(undo ? "undo" : "redo")} the operation: {exception.Message}", entry.Operation.Id));
                if (replacement) break;
            }
        }

        return new FileOrganizationBatchResult(plan.BatchId, results, plan.Operations.Count - results.Count, cancellationToken.IsCancellationRequested || results.Any(item => item.Outcome == FileOrganizationOutcome.Cancelled));
    }

    private async Task<HistoryPlan?> GetHistoryPlanAsync(bool undo, CancellationToken cancellationToken)
    {
        var candidate = undo
            ? await journal.GetNextUndoAsync(cancellationToken).ConfigureAwait(false)
            : await journal.GetNextRedoAsync(cancellationToken).ConfigureAwait(false);
        if (candidate is null) return null;

        var entries = candidate.Operation.BatchId is { } batchId
            ? await journal.GetBatchAsync(batchId, cancellationToken).ConfigureAwait(false)
            : new[] { candidate };
        var applicableStates = undo
            ? new[] { FileOperationEventKind.Completed, FileOperationEventKind.Redone }
            : new[] { FileOperationEventKind.Undone };
        IEnumerable<JournaledFileOperation> ordered = entries.Where(entry => entry.Operation.CompensatesOperationId is null &&
                applicableStates.Contains(entry.State))
            .OrderBy(entry => entry.Events[0].Sequence);
        if (undo) ordered = ordered.Reverse();

        var operations = ordered.ToArray();
        return operations.Length == 0 ? null : new HistoryPlan(candidate.Operation.BatchId, operations);
    }

    private static FileOrganizationHistoryPreviewItem CreateHistoryPreviewItem(JournaledFileOperation entry, bool undo)
    {
        var operation = entry.Operation;
        switch (operation.Kind)
        {
            case FileOperationKind.Move:
            case FileOperationKind.Rename:
            {
                var payload = JsonSerializer.Deserialize<PathOperationPayload>(operation.PayloadJson ?? "{}")
                    ?? new PathOperationPayload(false, false);
                var source = undo ? operation.DestinationPath : operation.SourcePath;
                var destination = undo ? operation.SourcePath : operation.DestinationPath;
                var action = operation.Kind == FileOperationKind.Rename
                    ? $"Rename {(payload.IsDirectory ? "folder" : "file")}"
                    : $"Move {(payload.IsDirectory ? "folder" : "file")}";
                return new FileOrganizationHistoryPreviewItem(action, source, destination);
            }
            case FileOperationKind.CreateFolder:
                return undo
                    ? new FileOrganizationHistoryPreviewItem("Send folder to Recycle Bin", operation.DestinationPath, "Recycle Bin")
                    : new FileOrganizationHistoryPreviewItem("Recreate folder", "New folder", operation.DestinationPath);
            case FileOperationKind.SendToRecycleBin:
            {
                var payload = JsonSerializer.Deserialize<RecycleBinPayload>(operation.PayloadJson ?? "{}")
                    ?? new RecycleBinPayload();
                var action = payload.IsDirectory ? "folder" : "file";
                return undo
                    ? new FileOrganizationHistoryPreviewItem($"Restore {action} from Recycle Bin", "Recycle Bin", operation.SourcePath)
                    : new FileOrganizationHistoryPreviewItem($"Send {action} to Recycle Bin", operation.SourcePath, "Recycle Bin");
            }
            default:
                return new FileOrganizationHistoryPreviewItem(operation.Kind.ToString(), operation.SourcePath,
                    operation.DestinationPath);
        }
    }

    private sealed record HistoryPlan(Guid? BatchId, IReadOnlyList<JournaledFileOperation> Operations);

    private async Task<FileOrganizationResult> ApplyCompensationAsync(JournaledFileOperation entry, bool undo,
        CancellationToken cancellationToken)
    {
        var operation = entry.Operation;
        if (operation.Kind == FileOperationKind.CreateFolder)
            return await ApplyFolderCreationCompensationAsync(entry, undo, cancellationToken).ConfigureAwait(false);
        if (operation.Kind == FileOperationKind.SendToRecycleBin)
            return await ApplyRecycleBinCompensationAsync(entry, undo, cancellationToken).ConfigureAwait(false);
        if (operation.Kind is not (FileOperationKind.Move or FileOperationKind.Rename) ||
            operation.SourcePath is null || operation.DestinationPath is null)
            return Failure($"Undo and redo are not available for {operation.Kind} operations yet.", operation.Id);

        var payload = JsonSerializer.Deserialize<PathOperationPayload>(operation.PayloadJson ?? "{}")
            ?? throw new InvalidDataException("The operation payload is missing.");
        var current = Path.GetFullPath(undo ? operation.DestinationPath : operation.SourcePath);
        var target = Path.GetFullPath(undo ? operation.SourcePath : operation.DestinationPath);
        if (!PathExists(current, payload.IsDirectory))
            return new FileOrganizationResult(FileOrganizationOutcome.Conflict,
                $"Cannot {(undo ? "undo" : "redo")} because '{current}' is missing or has changed.", operation.Id);
        var expectedIdentity = GetExpectedCurrentIdentity(entry, undo, payload);
        var currentIdentity = RequireIdentity(current);
        if (expectedIdentity is null || currentIdentity != expectedIdentity)
            return new FileOrganizationResult(FileOrganizationOutcome.Conflict,
                $"Cannot {(undo ? "undo" : "redo")} because '{current}' no longer has its recorded identity.", operation.Id);
        var targetParent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(target));
        if (targetParent is null || !Directory.Exists(targetParent))
            return new FileOrganizationResult(FileOrganizationOutcome.Conflict,
                $"Cannot {(undo ? "undo" : "redo")} because the original parent folder is unavailable.", operation.Id);
        RejectReparsePoint(current);
        if (payload.IsDirectory) EnsureNoReparsePoints(current);

        var crossVolume = GetVolume(currentIdentity) != GetVolume(RequireIdentity(targetParent));
        if (payload.IsDirectory && crossVolume)
            return Failure("Folder undo/redo across volumes is not supported.", operation.Id);
        var resolution = await ResolveHistoryDestinationAsync(entry, current, target, payload.IsDirectory, cancellationToken).ConfigureAwait(false);
        if (resolution.Failure is not null) return resolution.Failure;
        var afterPrompt = identities.GetIdentity(current);
        if (!afterPrompt.Exists || !afterPrompt.Accessible || afterPrompt.Identity != currentIdentity)
        {
            var restoreError = await RestoreDisplacedAsync(operation, resolution.Displaced, CancellationToken.None).ConfigureAwait(false);
            return restoreError is null ? Failure("The source changed while resolving the collision.", operation.Id)
                : await HistoryRecoveryAsync(operation, restoreError).ConfigureAwait(false);
        }
        var compensation = CreateOperation(operation.Kind, current, target,
            new PathOperationPayload(payload.IsDirectory, crossVolume, currentIdentity), operation.BatchId,
            operation.Id);
        await journal.AppendPreparedAsync(compensation, resolution.Displaced is null ? cancellationToken : CancellationToken.None).ConfigureAwait(false);

        FileOrganizationResult mutation;
        if (crossVolume)
        {
            mutation = await MoveFileAcrossVolumesAsync(compensation, current, target, currentIdentity, CancellationToken.None)
                .ConfigureAwait(false);
        }
        else
        {
            try
            {
                if (payload.IsDirectory) Directory.Move(current, target); else File.Move(current, target);
                mutation = await VerifySameVolumeMoveAsync(compensation, current, target, currentIdentity, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                mutation = await ClassifyMoveFailureAsync(compensation, current, target, currentIdentity, exception,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (!mutation.Succeeded)
        {
            var error = await RestoreDisplacedAsync(operation, resolution.Displaced, CancellationToken.None).ConfigureAwait(false);
            return error is null ? mutation with { OperationId = operation.Id }
                : await HistoryRecoveryAsync(operation, error).ConfigureAwait(false);
        }
        if (payload.IsReplacementBackup && payload.OriginalAttributes is { } attributes)
        {
            try { File.SetAttributes(target, (FileAttributes)attributes | (undo ? 0 : FileAttributes.Hidden)); }
            catch (Exception exception) when (IsFileSystemException(exception))
            { return await HistoryRecoveryAsync(operation, exception.Message).ConfigureAwait(false); }
        }
        var restorePrevious = await RestoreDisplacedAsync(operation, CurrentHistoryDetails(entry)?.DisplacedItem, CancellationToken.None).ConfigureAwait(false);
        if (restorePrevious is not null) return await HistoryRecoveryAsync(operation, restorePrevious).ConfigureAwait(false);
        var resultingIdentity = RequireIdentity(target);
        await journal.AppendEventAsync(operation.Id,
            undo ? FileOperationEventKind.Undone : FileOperationEventKind.Redone,
            JsonSerializer.Serialize(new OperationEventDetails(
                undo ? "Verified undo completed." : "Verified redo completed.",
                VerifiedDestinationIdentity: resultingIdentity, ResolvedPath: target, DisplacedItem: resolution.Displaced)), CancellationToken.None).ConfigureAwait(false);
        return Success(undo ? "Operation undone and verified." : "Operation redone and verified.",
            operation.Id, target) with { PreviousPath = current, VerifiedFileIdentity = payload.IsDirectory ? null : resultingIdentity };
    }

    private async Task<FileOrganizationResult> ApplyFolderCreationCompensationAsync(JournaledFileOperation entry,
        bool undo, CancellationToken cancellationToken)
    {
        var operation = entry.Operation;
        var payload = JsonSerializer.Deserialize<CreateFolderPayload>(operation.PayloadJson ?? "{}");
        if (payload is null || operation.DestinationPath is null)
            return Failure("The folder creation record is incomplete and cannot be safely reversed.", operation.Id);

        var folderPath = Path.GetFullPath(operation.DestinationPath);
        if (undo)
        {
            if (!Directory.Exists(folderPath))
                return new FileOrganizationResult(FileOrganizationOutcome.Conflict,
                    $"Cannot undo because '{folderPath}' is missing.", operation.Id);
            var expectedIdentity = entry.State == FileOperationEventKind.Redone
                ? ReadVerifiedIdentity(entry.Events.LastOrDefault(item => item.Kind == FileOperationEventKind.Redone))
                : ReadVerifiedIdentity(entry.Events.LastOrDefault(item => item.Kind == FileOperationEventKind.DestinationVerified))
                  ?? payload.CreatedFolderIdentity;
            var currentIdentity = RequireIdentity(folderPath);
            if (expectedIdentity is null || currentIdentity != expectedIdentity)
                return new FileOrganizationResult(FileOrganizationOutcome.Conflict,
                    $"Cannot undo because '{folderPath}' no longer has its recorded identity.", operation.Id);
            if (Directory.EnumerateFileSystemEntries(folderPath).Any())
                return new FileOrganizationResult(FileOrganizationOutcome.Conflict,
                    $"Cannot undo because '{folderPath}' is no longer empty. Its contents were left untouched.", operation.Id);

            var compensation = CreateOperation(FileOperationKind.SendToRecycleBin, folderPath, null,
                new RecycleBinPayload(OriginalVolume: GetVolume(currentIdentity), OriginalItemId: currentIdentity,
                    IsDirectory: true),
                operation.BatchId, operation.Id);
            await journal.AppendPreparedAsync(compensation, cancellationToken).ConfigureAwait(false);
            try
            {
                FileSystem.DeleteDirectory(folderPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin,
                    UICancelOption.ThrowException);
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                if (Directory.Exists(folderPath) && identities.GetIdentity(folderPath).Identity == currentIdentity)
                {
                    await AppendEventAsync(compensation, FileOperationEventKind.Failed, exception.Message, cancellationToken)
                        .ConfigureAwait(false);
                    return Failure($"Could not move the empty folder to the Recycle Bin: {exception.Message}", operation.Id);
                }

                await AppendEventAsync(compensation, FileOperationEventKind.RecoveryRequired,
                    "The folder path changed during its Recycle Bin operation.", CancellationToken.None).ConfigureAwait(false);
                return RecoveryRequired("The folder may have moved to the Recycle Bin, but its state could not be verified.", operation.Id);
            }

            if (Directory.Exists(folderPath))
            {
                await AppendEventAsync(compensation, FileOperationEventKind.RecoveryRequired,
                    "The folder remains at its original path after the Recycle Bin request.", cancellationToken).ConfigureAwait(false);
                return RecoveryRequired("The empty folder was not removed. It was not permanently deleted.", operation.Id);
            }
            await AppendEventAsync(compensation, FileOperationEventKind.Completed, "{}", cancellationToken).ConfigureAwait(false);
            var restorePrevious = await RestoreDisplacedAsync(operation, CurrentHistoryDetails(entry)?.DisplacedItem, CancellationToken.None).ConfigureAwait(false);
            if (restorePrevious is not null) return await HistoryRecoveryAsync(operation, restorePrevious).ConfigureAwait(false);
            await journal.AppendEventAsync(operation.Id, FileOperationEventKind.Undone,
                JsonSerializer.Serialize(new OperationEventDetails("Empty folder sent to the Recycle Bin.")), cancellationToken)
                .ConfigureAwait(false);
            return Success("Empty folder sent to the Recycle Bin; creation undone.", operation.Id, folderPath);
        }

        var parentPath = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folderPath));
        if (parentPath is null || !Directory.Exists(parentPath))
            return new FileOrganizationResult(FileOrganizationOutcome.Conflict,
                "Cannot redo because the original parent is unavailable or the folder path is occupied.", operation.Id);
        if (payload.ParentIdentity is null || RequireIdentity(parentPath) != payload.ParentIdentity)
            return new FileOrganizationResult(FileOrganizationOutcome.Conflict,
                "Cannot redo because the original parent folder has changed.", operation.Id);

        var resolution = await ResolveHistoryDestinationAsync(entry, null, folderPath, true, cancellationToken).ConfigureAwait(false);
        if (resolution.Failure is not null) return resolution.Failure;
        if (resolution.Displaced is not null) cancellationToken = CancellationToken.None;
        var createCompensation = CreateOperation(FileOperationKind.CreateFolder, null, folderPath,
            new CreateFolderPayload(payload.ParentIdentity), operation.BatchId, operation.Id);
        await journal.AppendPreparedAsync(createCompensation, cancellationToken).ConfigureAwait(false);
        try
        {
            CreateDirectoryExclusively(folderPath);
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            await AppendEventAsync(createCompensation, FileOperationEventKind.Failed, exception.Message, cancellationToken)
                .ConfigureAwait(false);
            var restoreError = await RestoreDisplacedAsync(operation, resolution.Displaced, CancellationToken.None).ConfigureAwait(false);
            return restoreError is null ? Failure($"Could not recreate the folder: {exception.Message}", operation.Id)
                : await HistoryRecoveryAsync(operation, restoreError).ConfigureAwait(false);
        }

        var recreatedIdentity = identities.GetIdentity(folderPath);
        if (!recreatedIdentity.Exists || !recreatedIdentity.Accessible || recreatedIdentity.Identity is null)
        {
            await AppendEventAsync(createCompensation, FileOperationEventKind.RecoveryRequired,
                "Recreated folder identity could not be read.", cancellationToken).ConfigureAwait(false);
            return RecoveryRequired("The folder was recreated, but its identity could not be verified.", operation.Id);
        }
        await AppendEventAsync(createCompensation, FileOperationEventKind.DestinationVerified,
            JsonSerializer.Serialize(new OperationEventDetails("Folder recreation verified.",
                VerifiedDestinationIdentity: recreatedIdentity.Identity)), cancellationToken).ConfigureAwait(false);
        await AppendEventAsync(createCompensation, FileOperationEventKind.Completed, "{}", cancellationToken).ConfigureAwait(false);
        await journal.AppendEventAsync(operation.Id, FileOperationEventKind.Redone,
            JsonSerializer.Serialize(new OperationEventDetails("Folder creation redone and verified.",
                VerifiedDestinationIdentity: recreatedIdentity.Identity, DisplacedItem: resolution.Displaced)), cancellationToken).ConfigureAwait(false);
        return Success("Folder creation redone and verified.", operation.Id, folderPath);
    }

    private async Task<FileOrganizationResult> ApplyRecycleBinCompensationAsync(JournaledFileOperation entry,
        bool undo, CancellationToken cancellationToken)
    {
        var operation = entry.Operation;
        if (operation.SourcePath is null ||
            JsonSerializer.Deserialize<RecycleBinPayload>(operation.PayloadJson ?? "{}") is not { OriginalItemId: not null } payload)
            return Failure("The Recycle Bin operation has no recorded source identity and cannot be safely reversed.", operation.Id);
        var originalPath = Path.GetFullPath(operation.SourcePath);

        if (undo)
        {
            if (recycleBinRestoreService is null)
                return Failure("Recycle Bin restoration is unavailable on this system.", operation.Id);
            var resolution = await ResolveHistoryDestinationAsync(entry, null, originalPath, payload.IsDirectory, cancellationToken).ConfigureAwait(false);
            if (resolution.Failure is not null) return resolution.Failure;
            if (resolution.Displaced is not null) cancellationToken = CancellationToken.None;

            var compensation = CreateOperation(FileOperationKind.RestoreFromRecycleBin, null, originalPath,
                new RecycleBinPayload(payload.RestoreToken, payload.OriginalVolume, payload.OriginalItemId, payload.IsDirectory),
                operation.BatchId, operation.Id);
            await journal.AppendPreparedAsync(compensation, cancellationToken).ConfigureAwait(false);
            var recycledIdentity = entry.State == FileOperationEventKind.Redone
                ? ReadVerifiedIdentity(entry.Events.LastOrDefault(item => item.Kind == FileOperationEventKind.Redone))
                  ?? payload.OriginalItemId
                : payload.OriginalItemId;
            var restore = await recycleBinRestoreService.RestoreAsync(originalPath, recycledIdentity,
                isDirectory: payload.IsDirectory, cancellationToken).ConfigureAwait(false);
            if (!restore.Succeeded)
            {
                await AppendEventAsync(compensation, FileOperationEventKind.Failed, restore.Message, cancellationToken)
                    .ConfigureAwait(false);
                var outcome = restore.Outcome is RecycleBinRestoreOutcome.Failed
                    ? FileOrganizationOutcome.Failed
                    : FileOrganizationOutcome.Conflict;
                var restoreError = await RestoreDisplacedAsync(operation, resolution.Displaced, CancellationToken.None).ConfigureAwait(false);
                return restoreError is null ? new FileOrganizationResult(outcome, restore.Message, operation.Id)
                    : await HistoryRecoveryAsync(operation, restoreError).ConfigureAwait(false);
            }

            var restoredIdentity = identities.GetIdentity(originalPath);
            var identityVerified = restoredIdentity is { Exists: true, Accessible: true } &&
                                   restoredIdentity.Identity == recycledIdentity;
            var contentVerified = !payload.IsDirectory && restore.RestoredContentVerified;
            if ((!restoredIdentity.Exists || !restoredIdentity.Accessible || restoredIdentity.Identity is null) && !contentVerified ||
                restoredIdentity.Identity != recycledIdentity && !contentVerified)
            {
                await AppendEventAsync(compensation, FileOperationEventKind.RecoveryRequired,
                    "The Shell restore completed, but the original path did not verify against its recorded identity.",
                    CancellationToken.None).ConfigureAwait(false);
                return RecoveryRequired("Windows reported a restore, but neither file identity nor content could verify the item. Review the original path.", operation.Id);
            }

            await AppendEventAsync(compensation, FileOperationEventKind.DestinationVerified,
                JsonSerializer.Serialize(new OperationEventDetails(contentVerified && !identityVerified
                        ? "Recycle Bin restore content verified; the Shell-assigned file identity changed."
                        : "Recycle Bin restore identity verified.",
                    VerifiedDestinationIdentity: restoredIdentity.Identity)), cancellationToken).ConfigureAwait(false);
            await AppendEventAsync(compensation, FileOperationEventKind.Completed, "{}", cancellationToken).ConfigureAwait(false);
            await journal.AppendEventAsync(operation.Id, FileOperationEventKind.Undone,
                JsonSerializer.Serialize(new OperationEventDetails(contentVerified && !identityVerified
                        ? "Recycle Bin item restored and its content fingerprint verified."
                        : "Recycle Bin item restored and identity verified.",
                    VerifiedDestinationIdentity: restoredIdentity.Identity, DisplacedItem: resolution.Displaced)), cancellationToken).ConfigureAwait(false);
            return Success(contentVerified && !identityVerified
                ? "Recycle Bin item restored and its content verified; Windows assigned a new file identity."
                : "Recycle Bin item restored and verified.", operation.Id, originalPath);
        }

        var current = identities.GetIdentity(originalPath);
        var expectedRestoredIdentity = ReadVerifiedIdentity(entry.Events.LastOrDefault(item => item.Kind == FileOperationEventKind.Undone))
                                       ?? payload.OriginalItemId;
        if (!current.Exists || !current.Accessible || current.Identity != expectedRestoredIdentity)
            return new FileOrganizationResult(FileOrganizationOutcome.Conflict,
                "Cannot redo because the restored item is missing or its identity changed.", operation.Id);
        var isDirectory = payload.IsDirectory;
        RejectReparsePoint(originalPath);
        if (isDirectory) EnsureNoReparsePoints(originalPath);
        var redoCompensation = CreateOperation(FileOperationKind.SendToRecycleBin, originalPath, null,
            new RecycleBinPayload(OriginalVolume: payload.OriginalVolume, OriginalItemId: current.Identity,
                IsDirectory: isDirectory),
            operation.BatchId, operation.Id);
        await journal.AppendPreparedAsync(redoCompensation, cancellationToken).ConfigureAwait(false);
        try
        {
            if (isDirectory)
                FileSystem.DeleteDirectory(originalPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
            else
                FileSystem.DeleteFile(originalPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        }
        catch (Exception exception) when (IsFileSystemException(exception) || exception is OperationCanceledException)
        {
            var afterFailure = identities.GetIdentity(originalPath);
            if (afterFailure is { Exists: true, Accessible: true } && afterFailure.Identity == current.Identity)
            {
                await AppendEventAsync(redoCompensation, FileOperationEventKind.Failed, exception.Message, cancellationToken)
                    .ConfigureAwait(false);
                return exception is OperationCanceledException
                    ? new(FileOrganizationOutcome.Cancelled, "Recycle Bin redo cancelled.", operation.Id)
                    : Failure($"Could not redo the Recycle Bin operation: {exception.Message}", operation.Id);
            }

            await AppendEventAsync(redoCompensation, FileOperationEventKind.RecoveryRequired,
                $"Recycle Bin redo failed after the source path changed: {exception.Message}", CancellationToken.None)
                .ConfigureAwait(false);
            return RecoveryRequired("The item may have moved to the Recycle Bin, but its result could not be verified.", operation.Id);
        }

        if (identities.GetIdentity(originalPath).Exists)
        {
            await AppendEventAsync(redoCompensation, FileOperationEventKind.RecoveryRequired,
                "Recycle Bin redo returned while the original path remained.", cancellationToken).ConfigureAwait(false);
            return RecoveryRequired("The item remains at its original path. It was not permanently deleted.", operation.Id);
        }
        await AppendEventAsync(redoCompensation, FileOperationEventKind.Completed, "{}", cancellationToken).ConfigureAwait(false);
        var previousRestoreError = await RestoreDisplacedAsync(operation, CurrentHistoryDetails(entry)?.DisplacedItem, CancellationToken.None).ConfigureAwait(false);
        if (previousRestoreError is not null) return await HistoryRecoveryAsync(operation, previousRestoreError).ConfigureAwait(false);
        await journal.AppendEventAsync(operation.Id, FileOperationEventKind.Redone,
            JsonSerializer.Serialize(new OperationEventDetails("Item sent back to the Recycle Bin.",
                VerifiedDestinationIdentity: current.Identity)), cancellationToken)
            .ConfigureAwait(false);
        return Success("Item sent back to the Recycle Bin.", operation.Id, originalPath);
    }

    private static string? GetExpectedCurrentIdentity(JournaledFileOperation entry, bool undo,
        PathOperationPayload payload)
    {
        if (!undo)
        {
            var undoneEvent = entry.Events.LastOrDefault(item => item.Kind == FileOperationEventKind.Undone);
            return ReadVerifiedIdentity(undoneEvent) ?? payload.SourceIdentity;
        }

        if (entry.State == FileOperationEventKind.Redone)
        {
            var redoneEvent = entry.Events.LastOrDefault(item => item.Kind == FileOperationEventKind.Redone);
            return ReadVerifiedIdentity(redoneEvent) ?? payload.SourceIdentity;
        }

        var verifiedEvent = entry.Events.LastOrDefault(item => item.Kind == FileOperationEventKind.DestinationVerified);
        return ReadVerifiedIdentity(verifiedEvent) ?? payload.SourceIdentity;
    }

    private static string? ReadVerifiedIdentity(FileOperationEvent? operationEvent)
    {
        if (operationEvent?.DetailsJson is null) return null;
        try
        {
            return JsonSerializer.Deserialize<OperationEventDetails>(operationEvent.DetailsJson)?.VerifiedDestinationIdentity;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool PathExists(string path, bool isDirectory) => isDirectory ? Directory.Exists(path) : File.Exists(path);

    private async Task<FileOrganizationResult> MoveFileAcrossVolumesAsync(
        FileOperationRecord operation,
        string source,
        string destination,
        string sourceIdentity,
        CancellationToken cancellationToken)
    {
        var temporaryPath = destination + ".medi-filer-" + operation.Id.ToString("N") + ".partial";
        await AppendEventAsync(operation, FileOperationEventKind.CopyStarted,
            JsonSerializer.Serialize(new OperationEventDetails($"Copying to temporary path: {temporaryPath}")), cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await CopyAndVerifyAsync(source, temporaryPath, cancellationToken).ConfigureAwait(false);
            if (RequireIdentity(source) != sourceIdentity)
                throw new IOException("The source file identity changed during the copy.");
            File.Move(temporaryPath, destination);
        }
        catch (Exception exception) when (IsFileSystemException(exception) || exception is OperationCanceledException)
        {
            TryDeleteTemporaryFile(temporaryPath);
            var original = identities.GetIdentity(source);
            var temporary = identities.GetIdentity(temporaryPath);
            if (original is { Exists: true, Accessible: true } && original.Identity == sourceIdentity && !temporary.Exists)
            {
                await AppendEventAsync(operation, FileOperationEventKind.Failed, exception.Message, CancellationToken.None)
                    .ConfigureAwait(false);
                if (exception is OperationCanceledException) throw;
                return Failure($"File move failed: {exception.Message}", operation.Id);
            }

            await AppendEventAsync(operation, FileOperationEventKind.RecoveryRequired,
                "Cross-volume copy did not finish in a state that can be safely classified.", CancellationToken.None)
                .ConfigureAwait(false);
            return RecoveryRequired("The copy did not finish cleanly. Check both paths before trying again.", operation.Id);
        }

        var copiedIdentity = identities.GetIdentity(destination);
        if (!copiedIdentity.Exists || !copiedIdentity.Accessible || copiedIdentity.Identity is null)
        {
            await AppendEventAsync(operation, FileOperationEventKind.RecoveryRequired,
                "Copied destination identity could not be read.", CancellationToken.None).ConfigureAwait(false);
            return RecoveryRequired("The destination copy exists, but its identity could not be verified.", operation.Id);
        }

        await AppendEventAsync(operation, FileOperationEventKind.DestinationVerified,
            JsonSerializer.Serialize(new OperationEventDetails(
                "Copy length and SHA-256 verified.", VerifiedDestinationIdentity: copiedIdentity.Identity)), cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await VerifyAndDeleteSourceAsync(source, destination, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsFileSystemException(exception) || exception is OperationCanceledException)
        {
            await AppendEventAsync(operation, FileOperationEventKind.RecoveryRequired,
                $"Destination is verified but source removal failed: {exception.Message}", CancellationToken.None)
                .ConfigureAwait(false);
            return RecoveryRequired("The destination copy is verified, but the source remains. Review both files.", operation.Id);
        }

        if (identities.GetIdentity(source).Exists)
        {
            await AppendEventAsync(operation, FileOperationEventKind.RecoveryRequired,
                "Source path remains after removal.", CancellationToken.None).ConfigureAwait(false);
            return RecoveryRequired("The destination copy is verified, but the source remains. Review both files.", operation.Id);
        }

        await AppendEventAsync(operation, FileOperationEventKind.SourceRemoved, "{}", cancellationToken).ConfigureAwait(false);
        await AppendEventAsync(operation, FileOperationEventKind.Completed, "{}", cancellationToken).ConfigureAwait(false);
        return Success("File moved and verified.", operation.Id, destination);
    }

    private async Task<FileOrganizationResult> VerifySameVolumeMoveAsync(
        FileOperationRecord operation,
        string source,
        string destination,
        string expectedIdentity,
        CancellationToken cancellationToken)
    {
        var sourceAfter = identities.GetIdentity(source);
        var destinationAfter = identities.GetIdentity(destination);
        if (!sourceAfter.Exists && destinationAfter is { Exists: true, Accessible: true } &&
            destinationAfter.Identity == expectedIdentity)
        {
            await AppendEventAsync(operation, FileOperationEventKind.Completed, "{}", cancellationToken).ConfigureAwait(false);
            var payload = JsonSerializer.Deserialize<PathOperationPayload>(operation.PayloadJson ?? "{}");
            var itemName = payload?.IsDirectory == true ? "Folder" : "File";
            var action = operation.Kind == FileOperationKind.Rename ? "renamed" : "moved";
            return Success($"{itemName} {action}.", operation.Id, destination) with { PreviousPath = source, VerifiedFileIdentity = payload?.IsDirectory == false ? expectedIdentity : null };
        }

        await AppendEventAsync(operation, FileOperationEventKind.RecoveryRequired,
            "Filesystem state after move does not match the recorded source identity.", cancellationToken)
            .ConfigureAwait(false);
        return RecoveryRequired("The move may have completed, but its result could not be verified. Check both folders.", operation.Id);
    }

    private async Task<FileOrganizationResult> ClassifyMoveFailureAsync(
        FileOperationRecord operation,
        string source,
        string destination,
        string expectedIdentity,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var sourceState = identities.GetIdentity(source);
        if (sourceState is { Exists: true, Accessible: true } && sourceState.Identity == expectedIdentity)
        {
            await AppendEventAsync(operation, FileOperationEventKind.Failed, exception.Message, cancellationToken)
                .ConfigureAwait(false);
            return Failure($"Move failed: {exception.Message}", operation.Id);
        }

        await AppendEventAsync(operation, FileOperationEventKind.RecoveryRequired,
            $"Move failed and its result is ambiguous: {exception.Message}", cancellationToken).ConfigureAwait(false);
        return RecoveryRequired("The move failed after the paths changed. Check both locations before retrying.", operation.Id);
    }

    private async Task CopyAndVerifyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        byte[] originalHash;
        await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                         1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            originalHash = await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false);
            source.Position = 0;
            await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
            await source.CopyToAsync(destination, 1024 * 1024, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
        }

        await VerifySourceHashUnchangedAsync(sourcePath, destinationPath, cancellationToken, originalHash)
            .ConfigureAwait(false);
    }

    private static async Task VerifySourceHashUnchangedAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken,
        byte[]? expectedSourceHash = null)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(destinationPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (source.Length != destination.Length)
            throw new IOException("The copied file length does not match the source.");
        var sourceHash = await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false);
        var destinationHash = await SHA256.HashDataAsync(destination, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(sourceHash, destinationHash) ||
            (expectedSourceHash is not null && !CryptographicOperations.FixedTimeEquals(sourceHash, expectedSourceHash)))
            throw new IOException("The copied file content does not match the source.");
    }

    private static async Task VerifyAndDeleteSourceAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        // Deny new write handles while comparing, then delete through a handle that permits
        // delete sharing. This closes the verify-then-delete race for ordinary concurrent writers.
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(destinationPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (source.Length != destination.Length)
            throw new IOException("The source changed after the destination copy was verified.");
        var sourceHash = await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false);
        var destinationHash = await SHA256.HashDataAsync(destination, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(sourceHash, destinationHash))
            throw new IOException("The source changed after the destination copy was verified.");

        File.Delete(sourcePath);
    }

    private async Task AppendEventAsync(
        FileOperationRecord operation,
        FileOperationEventKind kind,
        string details,
        CancellationToken cancellationToken) =>
        await journal.AppendEventAsync(operation.Id, kind,
            details.StartsWith('{') ? details : JsonSerializer.Serialize(new OperationEventDetails(details)),
            cancellationToken).ConfigureAwait(false);

    private FileOperationRecord CreateOperation(
        FileOperationKind kind,
        string? source,
        string? destination,
        FileOperationPayload payload,
        Guid? batchId = null,
        Guid? compensatesOperationId = null) => new(
        Guid.NewGuid(), batchId, kind, source, destination, DateTimeOffset.UtcNow,
        PayloadVersion, JsonSerializer.Serialize(payload, payload.GetType()), compensatesOperationId);

    private string RequireIdentity(string path)
    {
        var result = identities.GetIdentity(path);
        if (!result.Exists || !result.Accessible || string.IsNullOrWhiteSpace(result.Identity))
            throw new IOException($"Could not safely inspect '{path}'.");
        return result.Identity;
    }

    private static void EnsureFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("File was not found.", path);
    }

    private static void EnsureFolder(string path)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"Folder was not found: {path}");
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Symbolic links and junctions cannot be changed by this operation.");
    }

    private static void EnsureNoReparsePoints(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Folder operation would traverse a symbolic link or junction: {path}");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
            }
        }
    }

    private static void EnsureNotVolumeRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        if (root is not null && PathsEqual(path, root))
            throw new IOException("A drive root cannot be renamed, moved, or sent to the Recycle Bin.");
    }

    private static void EnsureDestinationAvailable(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            throw new NameCollisionException(path, $"An item already exists at '{path}'. Choose another name or destination.");
    }

    private static void ValidateLeafName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal))
            throw new ArgumentException("Enter a valid file or folder name without a path.", nameof(name));
    }

    private static bool IsSameOrChildPath(string candidate, string root)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        return PathsEqual(normalizedCandidate, Path.TrimEndingDirectorySeparator(root)) ||
               normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    private static string GetVolume(string identity) => identity.Split(':', 2)[0];

    private static bool IsFileSystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or
            System.Security.SecurityException or ExternalException;

    private static void CreateDirectoryExclusively(string path)
    {
        if (NativeMethods.CreateDirectory(path, IntPtr.Zero)) return;
        var error = Marshal.GetLastWin32Error();
        if (error == 183) throw new IOException($"A folder already exists at '{path}'.");
        throw new IOException(new System.ComponentModel.Win32Exception(error).Message);
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            // Its path is recorded in the CopyStarted event for later recovery review.
        }
    }

    private static FileOrganizationResult Success(string message, Guid? operationId, string? affectedPath = null) =>
        new(FileOrganizationOutcome.Succeeded, message, operationId, affectedPath);
    private static FileOrganizationResult Failure(string message, Guid? operationId = null) =>
        new(FileOrganizationOutcome.Failed, message, operationId);
    private static FileOrganizationResult RecoveryRequired(string message, Guid operationId) =>
        new(FileOrganizationOutcome.RecoveryRequired, message, operationId);

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateDirectory(string path, IntPtr securityAttributes);
    }
}
