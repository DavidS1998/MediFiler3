namespace MediFiler3.Application.Operations;

public sealed record FileCollisionPreview(bool IsDirectory, long? Size, byte[]? ThumbnailPng);

public interface IFileCollisionPreviewService
{
    Task<FileCollisionPreview> LoadAsync(string path, CancellationToken cancellationToken);
}
