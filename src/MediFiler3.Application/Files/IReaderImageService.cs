using MediFiler3.Domain.Files;

namespace MediFiler3.Application.Files;

public sealed record ReaderImage(byte[] Pixels, int Width, int Height, int OriginalWidth, int OriginalHeight);

public interface IReaderImageService
{
    Task<ReaderImage> DecodeAsync(FileEntry file, int displayPixelWidth, CancellationToken token);
}
