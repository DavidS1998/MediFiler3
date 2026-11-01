using MediFiler3.Domain.Files;

namespace MediFiler3.Application.Files;

public sealed record FileListingResult(IReadOnlyList<FileEntry> Files, IReadOnlyList<string> Errors);

public interface IFileListingService
{
    Task<FileListingResult> GetFilesAsync(string folderPath, CancellationToken cancellationToken);
}
