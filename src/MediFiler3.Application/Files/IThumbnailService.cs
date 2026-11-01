using MediFiler3.Domain.Files;

namespace MediFiler3.Application.Files;

public interface IThumbnailService
{
    Task<byte[]?> GetThumbnailPngAsync(FileEntry file, int width, int height, CancellationToken cancellationToken);
}
