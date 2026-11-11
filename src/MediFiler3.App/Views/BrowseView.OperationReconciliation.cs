using System.IO;
using MediFiler3.Application.Operations;

namespace MediFiler3.Views;

public partial class BrowseView
{
    private sealed record KnownFileMove(string Identity, long Size, DateTime ModifiedUtc, DateTimeOffset Expires);
    private readonly Dictionary<string, KnownFileMove> _knownFileMoves = new(StringComparer.OrdinalIgnoreCase);

    private void RememberKnownFileMoves(IEnumerable<FileOrganizationResult> results)
    {
        foreach (var path in _knownFileMoves.Where(pair => pair.Value.Expires < DateTimeOffset.UtcNow).Select(pair => pair.Key).ToArray()) _knownFileMoves.Remove(path);
        foreach (var result in results.Where(result => result.Succeeded && result.VerifiedFileIdentity is not null))
        {
            var file = _navigationState.Files.FirstOrDefault(file => PathsEqual(file.FullPath, result.PreviousPath ?? ""));
            // Undo may restore a source absent from this listing; retain its earlier verified metadata.
            var previous = new[] { result.PreviousPath, result.AffectedPath }.Where(path => path is not null)
                .Select(path => _knownFileMoves.GetValueOrDefault(path!)).FirstOrDefault(value => value?.Identity == result.VerifiedFileIdentity);
            if (file is null && previous is null) continue;
            var version = new KnownFileMove(result.VerifiedFileIdentity!, file?.Size ?? previous!.Size,
                file?.LastModifiedUtc ?? previous!.ModifiedUtc, DateTimeOffset.UtcNow.AddSeconds(30));
            foreach (var path in new[] { result.PreviousPath, result.AffectedPath }.Where(path => path is not null))
                if (file is not null && PathsEqual(path!, file.FullPath) || _knownFileMoves.GetValueOrDefault(path!)?.Identity == version.Identity)
                    _knownFileMoves[path!] = version;
        }
        while (_knownFileMoves.Count > 512) _knownFileMoves.Remove(_knownFileMoves.MinBy(pair => pair.Value.Expires).Key);
    }

    private async Task RefreshOperationCountsAsync(IEnumerable<FileOrganizationResult> results)
    {
        var roots = _navigationState.Roots.ToArray();
        var paths = results.Where(result => result.Succeeded).SelectMany(result => new[] { result.PreviousPath, result.AffectedPath })
            .Where(path => path is not null).Select(path => Path.GetDirectoryName(path!)).Where(path => path is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(path => FindFolder(roots, path) is not null).Cast<string>().ToArray();
        await foreach (var result in _folderTreeService.CountFilesAsync(paths, CancellationToken.None))
            if (result.Count is { } count && roots.SequenceEqual(_navigationState.Roots)) UpdateFolderFileCounts(result.Path, (int)count);
    }
}
