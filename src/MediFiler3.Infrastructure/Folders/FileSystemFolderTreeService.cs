using System.IO;
using System.Runtime.CompilerServices;
using MediFiler3.Application.Folders;

namespace MediFiler3.Infrastructure.Folders;

public sealed class FileSystemFolderTreeService : IFolderTreeService
{
    public Task<FolderTreeLoadResult> LoadRootsAsync(IEnumerable<string> folderPaths, CancellationToken cancellationToken) =>
        Task.Run(() => EnumerateRoots(folderPaths, cancellationToken), cancellationToken);

    public async IAsyncEnumerable<FolderChildrenResult> LoadHierarchyAsync(IEnumerable<FolderTreeEntry> roots,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pending = new Queue<FolderTreeEntry>(roots);
        while (pending.TryDequeue(out var parent))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await Task.Run(() => EnumerateChildren(parent, cancellationToken), cancellationToken).ConfigureAwait(false);
            foreach (var child in result.Children) pending.Enqueue(child);
            yield return result;
        }
    }

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
                roots.Add(new FolderTreeEntry(string.IsNullOrWhiteSpace(rootName) ? fullPath : rootName,
                    fullPath, isRoot: true, areChildrenLoaded: false));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                errors.Add($"{inputPath}: {exception.Message}");
            }
        }

        return new FolderTreeLoadResult(roots, errors);
    }

    private static FolderChildrenResult EnumerateChildren(FolderTreeEntry parent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = parent.Path;
        var errors = new List<string>();
        var children = new List<FolderTreeEntry>();
        try
        {
            // A directory may have been replaced by a junction since it was discovered.
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                return new FolderChildrenResult(parent, [], [$"Skipped linked folder: {path}"]);
            foreach (var childInfo in new DirectoryInfo(path).EnumerateDirectories())
            {
                var childPath = childInfo.FullName;
                cancellationToken.ThrowIfCancellationRequested();
                const string backupPrefix = ".MediFiler3-Undo-";
                if (childInfo.Name.StartsWith(backupPrefix, StringComparison.Ordinal) &&
                    Guid.TryParseExact(childInfo.Name[backupPrefix.Length..], "N", out _) &&
                    childInfo.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                const string storagePrefix = ".MediFiler3-UndoBackups-";
                if (childInfo.Name.StartsWith(storagePrefix, StringComparison.Ordinal) &&
                    childInfo.Name.Length == storagePrefix.Length + 16 &&
                    childInfo.Name[storagePrefix.Length..].All(Uri.IsHexDigit) &&
                    childInfo.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                try
                {
                    var attributes = childInfo.Attributes;
                    // Do not traverse junctions or symbolic links; they can create loops or escape the selected tree.
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    var childName = Path.GetFileName(Path.TrimEndingDirectorySeparator(childPath));
                    children.Add(new FolderTreeEntry(childName, childPath, areChildrenLoaded: false));
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
        return new FolderChildrenResult(parent, children, errors);
    }
}
