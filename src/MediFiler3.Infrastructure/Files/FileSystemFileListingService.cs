using System.IO;
using System.Runtime.InteropServices;
using MediFiler3.Application.Files;
using MediFiler3.Domain.Files;

namespace MediFiler3.Infrastructure.Files;

public sealed class FileSystemFileListingService : IFileListingService
{
    public Task<FileListingResult> GetFilesAsync(string folderPath, CancellationToken cancellationToken) =>
        Task.Run(() => EnumerateFiles(folderPath, cancellationToken), cancellationToken);

    private static FileListingResult EnumerateFiles(string folderPath, CancellationToken cancellationToken)
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
                    if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
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

    private sealed class WindowsFileNameComparer : IComparer<string>
    {
        public static WindowsFileNameComparer Instance { get; } = new();

        public int Compare(string? left, string? right) =>
            left is null ? (right is null ? 0 : -1) : right is null ? 1 : StrCmpLogicalW(left, right);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int StrCmpLogicalW(string left, string right);
    }
}
