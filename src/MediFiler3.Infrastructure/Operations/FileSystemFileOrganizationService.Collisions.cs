using System.IO;
using System.Text.Json;
using MediFiler3.Application.Operations;
using MediFiler3.Domain.Operations;

namespace MediFiler3.Infrastructure.Operations;

public sealed partial class FileSystemFileOrganizationService
{
    private sealed record PathRequest(FileOperationKind Kind, bool IsDirectory, string? Source, string Destination);
    private sealed class NameCollisionException(string path, string message) : IOException(message)
    { public string Destination { get; } = path; }

    public IFileOrganizationService WithCollisionResolver(IFileCollisionResolver resolver) =>
        new FileSystemFileOrganizationService(journal, identities, recycleBinRestoreService, resolver, _backups);

    public Task<FileOrganizationResult> MoveFileAsync(string sourceFilePath, string destinationFolderPath, CancellationToken cancellationToken = default) =>
        RunPathRequestAsync(new(FileOperationKind.Move, false, Path.GetFullPath(sourceFilePath),
            Path.Combine(Path.GetFullPath(destinationFolderPath), Path.GetFileName(sourceFilePath))), cancellationToken);
    public Task<FileOrganizationResult> RenameFileAsync(string sourceFilePath, string newName, CancellationToken cancellationToken = default)
    {
        ValidateLeafName(newName);
        var source = Path.GetFullPath(sourceFilePath);
        return RunPathRequestAsync(new(FileOperationKind.Rename, false, source, Path.Combine(Path.GetDirectoryName(source)!, newName)), cancellationToken);
    }
    public Task<FileOrganizationResult> CreateFolderAsync(string parentFolderPath, string name, CancellationToken cancellationToken = default)
    {
        ValidateLeafName(name);
        return RunPathRequestAsync(new(FileOperationKind.CreateFolder, true, null, Path.Combine(Path.GetFullPath(parentFolderPath), name)), cancellationToken);
    }
    public Task<FileOrganizationResult> RenameFolderAsync(string sourceFolderPath, string newName, CancellationToken cancellationToken = default)
    {
        ValidateLeafName(newName);
        var source = Path.GetFullPath(sourceFolderPath);
        return RunPathRequestAsync(new(FileOperationKind.Rename, true, source,
            Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(source))!, newName)), cancellationToken);
    }
    public Task<FileOrganizationResult> MoveFolderAsync(string sourceFolderPath, string destinationParentPath, CancellationToken cancellationToken = default) =>
        RunPathRequestAsync(new(FileOperationKind.Move, true, Path.GetFullPath(sourceFolderPath),
            Path.Combine(Path.GetFullPath(destinationParentPath), Path.GetFileName(Path.TrimEndingDirectorySeparator(sourceFolderPath)))), cancellationToken);

    private Task<FileOrganizationResult> RunPathRequestAsync(PathRequest request, CancellationToken token) =>
        Task.Run(() => OperationMaintenanceGate.RunAsync(() => ExecutePathRequestAsync(request, null, token), token), token);

    private Task<FileOrganizationResult> ExecuteRawAsync(PathRequest request, Guid? batch, CancellationToken token)
    {
        var parent = Path.GetDirectoryName(request.Destination)!;
        var name = Path.GetFileName(request.Destination);
        return request.Kind switch
        {
            FileOperationKind.CreateFolder => CreateFolderCoreAsync(parent, name, batch, token),
            FileOperationKind.Move when request.IsDirectory => MoveFolderCoreAsync(request.Source!, parent, batch, token, name),
            FileOperationKind.Move => MoveFileCoreAsync(request.Source!, parent, batch, token, name),
            FileOperationKind.Rename when request.IsDirectory => RenameFolderCoreAsync(request.Source!, name, batch, token),
            _ => RenameFileCoreAsync(request.Source!, name, batch, token)
        };
    }

    private async Task<FileOrganizationResult> ExecutePathRequestAsync(PathRequest request, Guid? batch, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return await ExecuteRawAsync(request, batch, token).ConfigureAwait(false); }
            catch (NameCollisionException collision)
            {
                if (collisionResolver is null) throw new IOException(collision.Message);
                var identity = RequireIdentity(request.Destination);
                var details = InspectCollision(request.Source, request.Destination, request.IsDirectory);
                var choice = await collisionResolver.ResolveAsync(details, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (choice.Choice == FileCollisionChoice.Cancel) return Cancelled();
                if (choice.Choice == FileCollisionChoice.Rename)
                {
                    ValidateLeafName(choice.NewName!);
                    request = request with { Destination = Path.Combine(Path.GetDirectoryName(request.Destination)!, choice.NewName!) };
                    continue;
                }
                if (!details.CanOverwrite) return new(FileOrganizationOutcome.Conflict, "These items cannot safely replace each other. Choose Rename or Cancel.");
                ValidateOverwrite(request, identity);
                var group = batch ?? Guid.NewGuid();
                var backup = await PreserveDestinationAsync(request.Destination, identity, group, null, token).ConfigureAwait(false);
                if (!backup.Succeeded) return backup;
                FileOrganizationResult incoming;
                try { incoming = await ExecuteRawAsync(request, group, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception exception) when (IsFileSystemException(exception) || exception is OperationCanceledException)
                { incoming = Failure(exception.Message); }
                if (incoming.Succeeded) return incoming with { Message = "Operation completed. The replaced item is preserved for Undo." };
                // Restore the displaced item if installing the incoming item failed. Never
                // overwrite anything that appeared while the user was answering the prompt.
                var saved = await journal.GetAsync(backup.OperationId!.Value, CancellationToken.None).ConfigureAwait(false);
                var plain = new FileSystemFileOrganizationService(journal, identities, recycleBinRestoreService, backupStore: _backups);
                var restored = await plain.ApplyCompensationAsync(saved!, true, CancellationToken.None).ConfigureAwait(false);
                if (restored.Succeeded)
                {
                    await journal.AppendEventAsync(saved!.Operation.Id, FileOperationEventKind.RedoInvalidated,
                        JsonSerializer.Serialize(new OperationEventDetails("Replacement failed; preserved destination restored.")), CancellationToken.None).ConfigureAwait(false);
                    return incoming with { OperationId = incoming.OperationId ?? backup.OperationId, Message = incoming.Message + " The existing destination was restored." };
                }
                return RecoveryRequired($"The replacement could not finish. Existing data was preserved; review '{request.Destination}' and '{backup.AffectedPath}'. {incoming.Message} {restored.Message}", backup.OperationId!.Value);
            }
        }
    }

    private FileCollision InspectCollision(string? source, string destination, bool directory, bool history = false)
    {
        var attributes = File.GetAttributes(destination);
        var existingDirectory = attributes.HasFlag(FileAttributes.Directory);
        var compatible = directory == existingDirectory && !attributes.HasFlag(FileAttributes.ReparsePoint);
        if (directory && source is not null && (IsSameOrChildPath(destination, source) || IsSameOrChildPath(source, destination))) compatible = false;
        if (source is not null && identities.GetIdentity(source).Identity == identities.GetIdentity(destination).Identity) compatible = false;
        var suggestedName = FileRenameName.SuggestNumberedName(Path.GetFileName(destination),
            history ? existingDirectory : directory, name =>
            {
                var path = Path.Combine(Path.GetDirectoryName(destination)!, name);
                return File.Exists(path) || Directory.Exists(path);
            });
        return new(source, destination, directory, existingDirectory, compatible, history, SuggestedName: suggestedName);
    }

    private void ValidateOverwrite(PathRequest request, string expectedIdentity)
    {
        if (RequireIdentity(request.Destination) != expectedIdentity)
            throw new IOException("The destination changed while the prompt was open. Try the operation again.");
        RejectReparsePoint(request.Destination);
        if (request.Source is not null)
        {
            RejectReparsePoint(request.Source);
            if (request.IsDirectory && GetVolume(RequireIdentity(request.Source)) != GetVolume(expectedIdentity))
                throw new IOException("Folder replacement across volumes is not supported.");
        }
        if (request.IsDirectory)
        {
            EnsureNotVolumeRoot(request.Destination);
            EnsureNoReparsePoints(request.Destination);
            if (request.Source is not null) EnsureNoReparsePoints(request.Source);
        }
    }

    private async Task<FileOrganizationResult> PreserveDestinationAsync(string destination, string expectedIdentity,
        Guid? batch, Guid? compensates, CancellationToken token, string? chosenName = null)
    {
        var backup = chosenName is null ? _backups.AllocatePath(destination) : Path.Combine(Path.GetDirectoryName(destination)!, chosenName);
        var attributes = File.GetAttributes(destination);
        var directory = attributes.HasFlag(FileAttributes.Directory);
        var operation = CreateOperation(FileOperationKind.Rename, destination, backup,
            new PathOperationPayload(directory, false, expectedIdentity, chosenName is null, (int)attributes), batch, compensates);
        await journal.AppendPreparedAsync(operation, token).ConfigureAwait(false);
        try
        {
            if (RequireIdentity(destination) != expectedIdentity) throw new IOException("The destination changed after confirmation.");
            if (directory) Directory.Move(destination, backup); else File.Move(destination, backup);
            if (chosenName is null) File.SetAttributes(backup, attributes | FileAttributes.Hidden);
            return await VerifySameVolumeMoveAsync(operation, destination, backup, expectedIdentity, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            return await ClassifyMoveFailureAsync(operation, destination, backup, expectedIdentity, exception, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static FileOrganizationResult Cancelled() => new(FileOrganizationOutcome.Cancelled, "Operation cancelled.");
}
