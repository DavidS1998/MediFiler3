using System.IO;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Viewing;
using MediFiler3.Domain.Files;

namespace MediFiler3.Application.Files;

[Flags]
public enum FileCategories { All = 0, Images = 1, Video = 2, Audio = 4, Text = 8, Other = 16 }

public enum FileSort { Name, Size, Created, Modified }

public sealed record FileQuery(string Search = "", string Includes = "", string Excludes = "",
    FileCategories Categories = FileCategories.All, FileSort Sort = FileSort.Name, bool Descending = false)
{
    public const int MaximumTextLength = 256;
    public static FileQuery AllFiles { get; } = new();

    public void Validate()
    {
        if (new[] { Search, Includes, Excludes }.Any(value => value is null || value.Length > MaximumTextLength) ||
            (Categories & ~(FileCategories.Images | FileCategories.Video | FileCategories.Audio | FileCategories.Text | FileCategories.Other)) != 0 ||
            !Enum.IsDefined(Sort))
            throw new InvalidDataException("Invalid file filter criteria.");
    }

    public bool Matches(FileEntry file) =>
        file.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) &&
        file.Name.Contains(Includes, StringComparison.OrdinalIgnoreCase) &&
        (Excludes.Length == 0 || !file.Name.Contains(Excludes, StringComparison.OrdinalIgnoreCase)) &&
        (Categories == FileCategories.All || (Categories & Category(file)) != 0);

    public IReadOnlyList<FileEntry> Apply(IReadOnlyList<FileEntry> inventory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (this == AllFiles) return inventory;
        var matches = new List<FileEntry>();
        foreach (var file in inventory)
        {
            token.ThrowIfCancellationRequested();
            if (Matches(file)) matches.Add(file);
        }

        // The listing already supplies natural order. Stable ordering retains it for equal sort keys.
        IEnumerable<FileEntry> ordered = Sort switch
        {
            FileSort.Size => OrderBy(file => file.Size),
            FileSort.Created => OrderBy(file => file.CreatedUtc),
            FileSort.Modified => OrderBy(file => file.LastModifiedUtc),
            _ => Descending ? matches.AsEnumerable().Reverse() : matches
        };
        return ordered.Select(file => { token.ThrowIfCancellationRequested(); return file; }).ToArray();

        IEnumerable<FileEntry> OrderBy<TKey>(Func<FileEntry, TKey> key) =>
            Descending ? matches.OrderByDescending(key) : matches.OrderBy(key);
    }

    private static FileCategories Category(FileEntry file)
    {
        if (ReaderSessionState.Supports(file)) return FileCategories.Images;
        if (MediaFileTypes.IsAudio(file.Extension)) return FileCategories.Audio;
        if (MediaFileTypes.Supports(file.Extension)) return FileCategories.Video;
        return TextFileTypes.Supports(file) ? FileCategories.Text : FileCategories.Other;
    }
}

public sealed record SavedFileFilter(Guid Id, string Name, FileQuery Query)
{
    public const int RecentLimit = 10;
    public const int MaximumNameLength = 64;

    public static void Validate(IReadOnlyList<SavedFileFilter>? saved, IReadOnlyList<Guid>? recent)
    {
        saved ??= [];
        recent ??= [];
        if (recent.Count > RecentLimit ||
            saved.Any(filter => filter is null || filter.Id == Guid.Empty || string.IsNullOrWhiteSpace(filter.Name) ||
                filter.Name.Length > MaximumNameLength || filter.Query is null) ||
            saved.Select(filter => filter.Id).Distinct().Count() != saved.Count ||
            saved.Select(filter => filter.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != saved.Count ||
            recent.Distinct().Count() != recent.Count || recent.Any(id => !saved.Any(filter => filter.Id == id)))
            throw new InvalidDataException("Invalid saved file filters or recent history.");

        foreach (var filter in saved) filter.Query.Validate();
    }
}
