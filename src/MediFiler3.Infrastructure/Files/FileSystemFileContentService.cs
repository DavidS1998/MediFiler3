using System.IO;
using MediFiler3.Application.Files;

namespace MediFiler3.Infrastructure.Files;

public sealed class FileSystemFileContentService : IFileContentService
{
    public Task<Stream> OpenReadAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }
}
