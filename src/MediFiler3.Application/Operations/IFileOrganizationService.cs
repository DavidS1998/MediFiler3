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
    string? AffectedPath = null,
    string? PreviousPath = null)
{
    public bool Succeeded => Outcome == FileOrganizationOutcome.Succeeded;
}

public sealed record FileOrganizationBatchResult(Guid? BatchId, IReadOnlyList<FileOrganizationResult> Results)
{
    public int SucceededCount => Results.Count(result => result.Succeeded);
    public bool Succeeded => Results.Count > 0 && SucceededCount == Results.Count;
    public string Message => Results.Count == 0 ? "No file operations were performed."
        : Results.Count == 1 ? Results[0].Message
        : $"{SucceededCount} of {Results.Count} items succeeded. " +
          string.Join(" ", Results.Where(result => !result.Succeeded).Select(result => result.Message));
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
    Task<FileOrganizationBatchResult> MoveFilesAsync(IReadOnlyList<string> sourceFilePaths, string destinationFolderPath, CancellationToken cancellationToken = default);
    Task<FileOrganizationBatchResult> UndoAsync(CancellationToken cancellationToken = default);
    Task<FileOrganizationBatchResult> RedoAsync(CancellationToken cancellationToken = default);
}
