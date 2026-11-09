using System.IO;
using System.Runtime.CompilerServices;
using MediFiler3.Application.Folders;

namespace MediFiler3.Infrastructure.Folders;

public sealed class FileSystemFolderTreeService : IFolderTreeService
{
    public Task<FolderTreeLoadResult> LoadRootsAsync(IEnumerable<string> folderPaths, CancellationToken cancellationToken) =>
        Task.Run(() => EnumerateRoots(folderPaths, cancellationToken), cancellationToken);

    public async IAsyncEnumerable<FolderFileCountResult> CountFilesAsync(IEnumerable<string> folderPaths,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var path in folderPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await Task.Run(() => CountFiles(path, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
    }

    private static FolderFileCountResult CountFiles(string path, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        long count = 0;
        try
        {
            // Enumeration supplies cached attributes, avoiding a separate stat for every file.
            foreach (var file in new DirectoryInfo(path).EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0) count++;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           System.Security.SecurityException)
        {
            errors.Add($"{path}: {exception.Message}");
        }

        return new FolderFileCountResult(path, errors.Count == 0 ? count : null, errors);
    }

    private static FolderTreeLoadResult EnumerateRoots(IEnumerable<string> folderPaths, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var roots = new List<FolderTreeEntry>();
        foreach (var inputPath in folderPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var fullPath = Path.GetFullPath(inputPath);
                if (!Directory.Exists(fullPath))
                {
                    errors.Add($"Not an accessible folder: {inputPath}");
                    continue;
                }

                var rootName = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
                roots.Add(EnumerateFolder(fullPath, string.IsNullOrWhiteSpace(rootName) ? fullPath : rootName, true, errors, cancellationToken));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                errors.Add($"{inputPath}: {exception.Message}");
            }
        }

        return new FolderTreeLoadResult(roots, errors);
    }

    private static FolderTreeEntry EnumerateFolder(string path, string name, bool isRoot, List<string> errors, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var children = new List<FolderTreeEntry>();
        try
        {
            foreach (var childPath in Directory.EnumerateDirectories(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var attributes = File.GetAttributes(childPath);
                    // Do not traverse junctions or symbolic links; they can create loops or escape the selected tree.
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    var childName = Path.GetFileName(Path.TrimEndingDirectorySeparator(childPath));
                    children.Add(EnumerateFolder(childPath, childName, false, errors, cancellationToken));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                                   System.Security.SecurityException)
                {
                    errors.Add($"{childPath}: {exception.Message}");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           System.Security.SecurityException)
        {
            errors.Add($"{path}: {exception.Message}");
        }

        children.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name));
        return new FolderTreeEntry(name, path, children, isRoot);
    }
}
