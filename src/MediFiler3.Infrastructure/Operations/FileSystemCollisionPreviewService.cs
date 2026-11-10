using System.IO;
using MediFiler3.Application.Files;
using MediFiler3.Application.Operations;
using MediFiler3.Domain.Files;

namespace MediFiler3.Infrastructure.Operations;

public sealed class FileSystemCollisionPreviewService(IThumbnailService thumbnails) : IFileCollisionPreviewService
{
    public async Task<FileCollisionPreview> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var metadata = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.Directory)) return (Directory: true, File: (FileEntry?)null);
            var info = new FileInfo(path);
            return (Directory: false, File: new FileEntry(path, info.Name, info.Length, info.LastWriteTimeUtc));
        }, cancellationToken).ConfigureAwait(false);
        // Folder totals are intentionally not scanned just to display a prompt.
        if (metadata.Directory) return new(true, null, null);
        var file = metadata.File!;
        byte[]? png = null;
        try { png = await thumbnails.GetThumbnailPngAsync(file, 240, 160, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            NotSupportedException or System.Runtime.InteropServices.COMException) { /* Retain size with a preview placeholder. */ }
        cancellationToken.ThrowIfCancellationRequested();
        return new(false, file.Size, png);
    }
}
