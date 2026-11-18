using System.IO;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Viewing;
using MediFiler3.Domain.Files;

namespace MediFiler3.Application.Files;

[Flags]
public enum FileCategories { All = 0, Images = 1, Video = 2, Audio = 4, Text = 8, Other = 16 }

public enum FileSort { Name, Size, Created, Modified }

public sealed record FileQuery(string Search = "", string Includes = "", string Excludes = "",
    FileCategories Categories = FileCategories.All, FileSort Sort = FileSort.Name, bool Descending = false, bool IncludeSubfolders = false, bool CombineFolders = false)
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

        if (!IncludeSubfolders)
            return Order(matches, global: false).ToArray();
        if (CombineFolders)
            return Order(matches, global: true).ToArray();
        return matches.GroupBy(file => Path.GetDirectoryName(file.FullPath), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => Order(group, global: false)).ToArray();

        IEnumerable<FileEntry> Order(IEnumerable<FileEntry> files, bool global)
        {
            var byName = WindowsFileNameComparer.Instance;
            IEnumerable<FileEntry> ordered = Sort switch
            {
                FileSort.Size => SortBy(file => file.Size),
                FileSort.Created => SortBy(file => file.CreatedUtc),
                FileSort.Modified => SortBy(file => file.LastModifiedUtc),
                _ when global => (Descending ? files.OrderByDescending(file => file.Name, byName) : files.OrderBy(file => file.Name, byName))
                    .ThenBy(file => file.FullPath, byName).ThenBy(file => file.FullPath, StringComparer.Ordinal),
                _ => Descending ? files.Reverse() : files
            };
            return ordered.Select(file => { token.ThrowIfCancellationRequested(); return file; });

            IOrderedEnumerable<FileEntry> SortBy<TKey>(Func<FileEntry, TKey> key)
            {
                var values = Descending ? files.OrderByDescending(key) : files.OrderBy(key);
                return global ? values.ThenBy(file => file.Name, byName).ThenBy(file => file.FullPath, byName)
                    .ThenBy(file => file.FullPath, StringComparer.Ordinal) : values;
            }
        }
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
