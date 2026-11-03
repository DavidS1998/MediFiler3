using System.IO;
using MediFiler3.Application.Operations;

namespace MediFiler3.Infrastructure.Operations;

public enum RecycleBinMatchOutcome { Matched, NotFound, Ambiguous, IdentityMismatch }

public sealed record RecycleBinMatchResult(RecycleBinMatchOutcome Outcome, int? Index = null);

public static class RecycleBinItemMatcher
{
    public static RecycleBinMatchResult FindUnique(IReadOnlyList<RecycleBinEntryDescriptor> entries,
        string originalPath, string itemIdentity, bool isDirectory)
    {
        var pathMatches = entries.Select((entry, index) => (entry, index))
            .Where(item => item.entry.IsDirectory == isDirectory && PathsEqual(item.entry.OriginalPath, originalPath))
            .ToArray();
        if (pathMatches.Length == 0) return new RecycleBinMatchResult(RecycleBinMatchOutcome.NotFound);

        var identityMatches = pathMatches.Where(item => string.Equals(item.entry.ItemIdentity, itemIdentity,
            StringComparison.OrdinalIgnoreCase)).ToArray();
        if (identityMatches.Length == 0) return new RecycleBinMatchResult(RecycleBinMatchOutcome.IdentityMismatch);
        if (identityMatches.Length > 1) return new RecycleBinMatchResult(RecycleBinMatchOutcome.Ambiguous);
        return new RecycleBinMatchResult(RecycleBinMatchOutcome.Matched, identityMatches[0].index);
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }
}
