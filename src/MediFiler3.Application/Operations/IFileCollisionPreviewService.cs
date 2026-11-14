namespace MediFiler3.Application.Operations;

public sealed record FileCollisionPreview(bool IsDirectory, long? Size, byte[]? ThumbnailPng,
    long? FileCount = null, bool IsComplete = true);

public interface IFileCollisionPreviewService
{
    Task<FileCollisionPreview> LoadAsync(string path, CancellationToken cancellationToken);
}
