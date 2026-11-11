using MediFiler3.Domain.Files;

namespace MediFiler3.Application.Files;

public interface IThumbnailService
{
    Task<byte[]?> GetThumbnailPngAsync(FileEntry file, int width, int height, CancellationToken cancellationToken);

    /// <summary>Use the provider's default preview sizing. Explicit-size callers retain their existing contract.</summary>
    Task<byte[]?> GetDefaultThumbnailPngAsync(FileEntry file, CancellationToken cancellationToken) =>
        GetThumbnailPngAsync(file, 256, 256, cancellationToken);
}
