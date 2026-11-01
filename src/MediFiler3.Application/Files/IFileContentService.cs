namespace MediFiler3.Application.Files;

public interface IFileContentService
{
    Task<Stream> OpenReadAsync(string filePath, CancellationToken cancellationToken);
}
