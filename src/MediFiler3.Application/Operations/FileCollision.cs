namespace MediFiler3.Application.Operations;

public enum FileCollisionChoice { Cancel, Rename, Overwrite }
public sealed record FileCollision(string? SourcePath, string DestinationPath, bool IsDirectory,
    bool ExistingIsDirectory, bool CanOverwrite, bool IsHistory = false, string? Explanation = null,
    string? SuggestedName = null);
public sealed record FileCollisionResolution(FileCollisionChoice Choice, string? NewName = null);
public interface IFileCollisionResolver
{
    Task<FileCollisionResolution> ResolveAsync(FileCollision collision, CancellationToken cancellationToken = default);
}
