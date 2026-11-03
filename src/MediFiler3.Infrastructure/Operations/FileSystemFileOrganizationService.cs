using System.Security.Cryptography;
using System.Text.Json;
using MediFiler3.Application.Operations;
using MediFiler3.Domain.Operations;
using Microsoft.VisualBasic.FileIO;
using System.IO;
using System.Runtime.InteropServices;

namespace MediFiler3.Infrastructure.Operations;

public sealed class FileSystemFileOrganizationService(
    IFileOperationJournal journal,
    IFileIdentityProvider identities) : IFileOrganizationService
{
    private const int PayloadVersion = 1;

    public async Task<FileOrganizationResult> MoveFileAsync(
        string sourceFilePath,
        string destinationFolderPath,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourceFilePath);
        var folder = Path.GetFullPath(destinationFolderPath);
        EnsureFile(source);
        EnsureFolder(folder);
        RejectReparsePoint(source);
        var destination = Path.Combine(folder, Path.GetFileName(source));
        if (PathsEqual(source, destination)) return Success("The file is already in that folder.", null, source);
        EnsureDestinationAvailable(destination);
        var sourceIdentity = RequireIdentity(source);
        var folderIdentity = RequireIdentity(folder);
        var crossVolume = GetVolume(sourceIdentity) != GetVolume(folderIdentity);
        var operation = CreateOperation(FileOperationKind.Move, source, destination,
            new PathOperationPayload(false, crossVolume, sourceIdentity));
        await journal.AppendPreparedAsync(operation, cancellationToken).ConfigureAwait(false);

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

    public async Task<FileOrganizationResult> RenameFileAsync(
        string sourceFilePath,
        string newName,
        CancellationToken cancellationToken = default)
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
            new PathOperationPayload(false, false, sourceIdentity));
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

    public async Task<FileOrganizationResult> CreateFolderAsync(
        string parentFolderPath,
        string name,
        CancellationToken cancellationToken = default)
    {
        var parent = Path.GetFullPath(parentFolderPath);
        EnsureFolder(parent);
        ValidateLeafName(name);
        var destination = Path.Combine(parent, name);
        EnsureDestinationAvailable(destination);
        var parentIdentity = RequireIdentity(parent);
        var operation = CreateOperation(FileOperationKind.CreateFolder, null, destination,
            new CreateFolderPayload(ParentIdentity: parentIdentity));
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

    public async Task<FileOrganizationResult> RenameFolderAsync(
        string sourceFolderPath,
        string newName,
        CancellationToken cancellationToken = default)
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
            new PathOperationPayload(true, false, sourceIdentity));
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

    public async Task<FileOrganizationResult> MoveFolderAsync(
        string sourceFolderPath,
        string destinationParentPath,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourceFolderPath);
        var destinationParent = Path.GetFullPath(destinationParentPath);
        EnsureFolder(source);
        EnsureNotVolumeRoot(source);
        EnsureFolder(destinationParent);
        RejectReparsePoint(source);
        EnsureNoReparsePoints(source);
        var destination = Path.Combine(destinationParent, Path.GetFileName(Path.TrimEndingDirectorySeparator(source)));
        if (PathsEqual(source, destination)) return Success("The folder is already in that location.", null, source);
        if (IsSameOrChildPath(destinationParent, source))
            return Failure("A folder cannot be moved into itself or one of its descendants.");
        EnsureDestinationAvailable(destination);

        var sourceIdentity = RequireIdentity(source);
        var parentIdentity = RequireIdentity(destinationParent);
        if (GetVolume(sourceIdentity) != GetVolume(parentIdentity))
            return Failure("Moving folders between volumes is not supported yet. Choose a destination on the same volume.");

        var operation = CreateOperation(FileOperationKind.Move, source, destination,
            new PathOperationPayload(true, false, sourceIdentity));
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

    public async Task<FileOrganizationResult> SendToRecycleBinAsync(
        string path,
        bool isDirectory,
        bool confirmNonEmptyDirectory = false,
        CancellationToken cancellationToken = default)
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
            new RecycleBinPayload(OriginalVolume: GetVolume(sourceIdentity), OriginalItemId: sourceIdentity));
        await journal.AppendPreparedAsync(operation, cancellationToken).ConfigureAwait(false);

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
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            var remaining = identities.GetIdentity(source);
            if (remaining is { Exists: true, Accessible: true, Identity: not null } && remaining.Identity == sourceIdentity)
            {
                await AppendEventAsync(operation, FileOperationEventKind.Failed, exception.Message, cancellationToken)
                    .ConfigureAwait(false);
                return Failure($"Could not send the item to the Recycle Bin: {exception.Message}", operation.Id);
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
            return Success($"{itemName} {action}.", operation.Id, destination);
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
        FileOperationPayload payload) => new(
        Guid.NewGuid(), null, kind, source, destination, DateTimeOffset.UtcNow,
        PayloadVersion, JsonSerializer.Serialize(payload));

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
            throw new IOException($"An item already exists at '{path}'. Choose another name or destination.");
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
