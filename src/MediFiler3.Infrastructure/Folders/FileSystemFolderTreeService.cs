using MediFiler3.Application.Folders;

namespace MediFiler3.Infrastructure.Folders;

public sealed class FileSystemFolderTreeService : IFolderTreeService
{
    public Task<FolderTreeLoadResult> LoadRootsAsync(IEnumerable<string> folderPaths, CancellationToken cancellationToken) =>
        Task.Run(() => EnumerateRoots(folderPaths, cancellationToken), cancellationToken);

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
