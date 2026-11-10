using System.IO;
using MediFiler3.Application.Folders;

namespace MediFiler3.Infrastructure.Folders;

public sealed class FileSystemRootFolderAvailabilityService : IRootFolderAvailabilityService
{
    private readonly SemaphoreSlim _workers = new(2, 2);

    public async Task<RootFolderAvailability> CheckAsync(string path, CancellationToken cancellationToken = default)
    {
        path = RootFolderPath.Normalize(path);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await _workers.WaitAsync(timeout.Token).ConfigureAwait(false);
            // Windows filesystem calls cannot always be interrupted. Keep the slot occupied
            // until the native call actually returns, even when the caller times out.
            var probe = Task.Run(() =>
            {
                try { return Probe(path); }
                finally { _workers.Release(); }
            });
            return await probe.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RootFolderAvailability.TemporarilyUnavailable;
        }
    }

    private static RootFolderAvailability Probe(string path)
    {
        try
        {
            if (!File.GetAttributes(path).HasFlag(FileAttributes.Directory)) return RootFolderAvailability.Missing;
            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            _ = entries.MoveNext(); // Check listing permission without traversing subfolders.
            return RootFolderAvailability.Available;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        { return RootFolderAvailability.Missing; }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        { return RootFolderAvailability.AccessDenied; }
        catch (IOException) { return RootFolderAvailability.TemporarilyUnavailable; }
    }
}
