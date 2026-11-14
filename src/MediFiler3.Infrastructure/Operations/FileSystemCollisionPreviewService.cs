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
        if (metadata.Directory)
            return await Task.Run(() => ScanFolder(path, cancellationToken), cancellationToken).ConfigureAwait(false);
        var file = metadata.File!;
        byte[]? png = null;
        try { png = await thumbnails.GetThumbnailPngAsync(file, 240, 160, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            NotSupportedException or System.Runtime.InteropServices.COMException) { /* Retain size with a preview placeholder. */ }
        cancellationToken.ThrowIfCancellationRequested();
        return new(false, file.Size, png);
    }

    private static FileCollisionPreview ScanFolder(string path, CancellationToken token)
    {
        long count = 0, bytes = 0;
        var complete = true;
        var pending = new Stack<string>(); pending.Push(path);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) { complete = false; continue; }
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if (attributes.HasFlag(FileAttributes.ReparsePoint)) { complete = false; continue; }
                        if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(entry);
                        else
                        {
                            var length = new FileInfo(entry).Length;
                            bytes = checked(bytes + length); count++;
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { complete = false; }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { complete = false; }
        }
        token.ThrowIfCancellationRequested();
        return new(true, bytes, null, count, complete);
    }
}
