using System.IO;
using System.Runtime.CompilerServices;
using MediFiler3.Application.Files;
using MediFiler3.Domain.Files;

namespace MediFiler3.Infrastructure.Files;

public sealed class FileSystemFileListingService : IFileListingService
{
    public Task<FileListingResult> GetFilesAsync(string folderPath, CancellationToken cancellationToken) =>
        Task.Run(() => EnumerateFiles(folderPath, cancellationToken), cancellationToken);

    public async IAsyncEnumerable<FileListingResult> GetSubtreeFilesAsync(string folderPath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(folderPath);
        while (pending.TryPop(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (files, children) = await Task.Run(() => ReadSubtreeFolder(folder, cancellationToken), cancellationToken).ConfigureAwait(false);
            for (var index = children.Length - 1; index >= 0; index--) pending.Push(children[index]);
            yield return files;
        }
    }

    private static (FileListingResult, string[]) ReadSubtreeFolder(string folder, CancellationToken token)
    {
        var errors = new List<string>();
        var children = new List<string>();
        var files = (IReadOnlyList<FileEntry>)[];
        try
        {
            var info = new DirectoryInfo(folder);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || IsManagedBackup(info))
                return (new([], [$"Skipped inaccessible scope: {folder}"]), []);

            // Reject a changed ancestor before opening a pending descendant.
            for (var parent = info.Parent; parent is not null; parent = parent.Parent)
            {
                token.ThrowIfCancellationRequested();
                if ((parent.Attributes & FileAttributes.ReparsePoint) != 0 || IsManagedBackup(parent))
                    return (new([], [$"Skipped inaccessible scope: {folder}"]), []);
            }

            var listing = EnumerateFiles(folder, token, skipLinks: true);
            files = listing.Files;
            errors.AddRange(listing.Errors);
            foreach (var child in info.EnumerateDirectories())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if ((child.Attributes & FileAttributes.ReparsePoint) == 0 && !IsManagedBackup(child))
                        children.Add(child.FullName);
                }
                catch (Exception exception) when (IsListingError(exception))
                {
                    errors.Add($"{child.FullName}: {exception.Message}");
                }
            }
        }
        catch (Exception exception) when (IsListingError(exception))
        {
            errors.Add($"{folder}: {exception.Message}");
        }
        children.Sort(WindowsFileNameComparer.Instance);
        return (new(files, errors), children.ToArray());
    }

    private static bool IsManagedBackup(DirectoryInfo directory)
    {
        if (string.Equals(directory.FullName, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MediFiler3", "UndoBackups"), StringComparison.OrdinalIgnoreCase)) return true;
        if ((directory.Attributes & FileAttributes.Hidden) == 0) return false;
        const string legacy = ".MediFiler3-Undo-";
        const string storage = ".MediFiler3-UndoBackups-";
        var name = directory.Name;
        return name.StartsWith(legacy, StringComparison.Ordinal) && Guid.TryParseExact(name[legacy.Length..], "N", out _) ||
            name.StartsWith(storage, StringComparison.Ordinal) && name.Length == storage.Length + 16 && name[storage.Length..].All(Uri.IsHexDigit);
    }

    private static bool IsListingError(Exception exception) => exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private static FileListingResult EnumerateFiles(string folderPath, CancellationToken cancellationToken, bool skipLinks = false)
    {
        var files = new List<FileEntry>();
        var errors = new List<string>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 ||
                        skipLinks && (attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    var info = new FileInfo(path);
                    files.Add(new FileEntry(path, info.Name, info.Length, info.LastWriteTimeUtc) { CreatedUtc = info.CreationTimeUtc });
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                                   System.Security.SecurityException)
                {
                    errors.Add($"{path}: {exception.Message}");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           System.Security.SecurityException)
        {
            errors.Add($"{folderPath}: {exception.Message}");
        }

        files.Sort((left, right) =>
        {
            var natural = WindowsFileNameComparer.Instance.Compare(left.Name, right.Name);
            return natural != 0 ? natural : StringComparer.Ordinal.Compare(left.FullPath, right.FullPath);
        });
        return new FileListingResult(files, errors);
    }

}
