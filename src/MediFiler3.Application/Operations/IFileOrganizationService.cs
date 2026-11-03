namespace MediFiler3.Application.Operations;

public enum FileOrganizationOutcome
{
    Succeeded,
    Failed,
    RecoveryRequired,
    ConfirmationRequired,
    Conflict
}

public sealed record FileOrganizationResult(
    FileOrganizationOutcome Outcome,
    string Message,
    Guid? OperationId = null,
    string? AffectedPath = null)
{
    public bool Succeeded => Outcome == FileOrganizationOutcome.Succeeded;
}

/// <summary>Application-owned use cases for journaled file and folder changes.</summary>
public interface IFileOrganizationService
{
    Task<FileOrganizationResult> MoveFileAsync(string sourceFilePath, string destinationFolderPath, CancellationToken cancellationToken = default);
    Task<FileOrganizationResult> RenameFileAsync(string sourceFilePath, string newName, CancellationToken cancellationToken = default);
    Task<FileOrganizationResult> CreateFolderAsync(string parentFolderPath, string name, CancellationToken cancellationToken = default);
    Task<FileOrganizationResult> RenameFolderAsync(string sourceFolderPath, string newName, CancellationToken cancellationToken = default);
    Task<FileOrganizationResult> MoveFolderAsync(string sourceFolderPath, string destinationParentPath, CancellationToken cancellationToken = default);
    Task<FileOrganizationResult> SendToRecycleBinAsync(string path, bool isDirectory, bool confirmNonEmptyDirectory = false, CancellationToken cancellationToken = default);
}
