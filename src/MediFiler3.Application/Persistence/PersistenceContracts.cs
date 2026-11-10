namespace MediFiler3.Application.Persistence;

public sealed record ApplicationSettings(bool Muted = false, double Volume = 0.5,
    int CarouselIconCount = 11, bool FolderTreePinned = false)
{
    public void Validate()
    {
        if (!double.IsFinite(Volume) || Volume is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Volume));
        if (CarouselIconCount is < 1 or > 25 || CarouselIconCount % 2 == 0)
            throw new ArgumentOutOfRangeException(nameof(CarouselIconCount));
    }
}

// Bounds are normal restore bounds in logical units; minimized state is never persisted.
public sealed record WindowLayout(double Left, double Top, double Width, double Height,
    bool Maximized, double FolderTreeWidth)
{
    public void Validate()
    {
        if (!double.IsFinite(Left) || !double.IsFinite(Top) ||
            !double.IsFinite(Width) || Width <= 0 || !double.IsFinite(Height) || Height <= 0 ||
            !double.IsFinite(FolderTreeWidth) || FolderTreeWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(Width), "Layout must contain finite bounds and positive sizes.");
    }
}

// Records describe explicitly opened roots only. Subfolder navigation must not create records.
// A missing/renamed root remains saved until the user ignores, locates, or removes it.
public sealed record RootFolderHistory(string Path, long OpenCount, DateTimeOffset? LastOpenedUtc,
    bool ShowInRecent, bool Favorite);

// CacheFileName is a leaf name inside the existing owned thumbnail directory, never a source path.
public sealed record ThumbnailCacheEntry(string Key, string CacheFileName, long SizeBytes,
    DateTimeOffset LastAccessUtc);

public interface IApplicationSettingsStore
{
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}

public interface IWindowLayoutStore
{
    Task<WindowLayout?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(WindowLayout layout, CancellationToken cancellationToken = default);
}

// These are storage primitives. Visit counting, ordering and user-directed relocation belong
// to the root history service introduced in 4.3; loading never probes the filesystem.
public interface IRootFolderHistoryStore
{
    Task<IReadOnlyList<RootFolderHistory>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(RootFolderHistory root, CancellationToken cancellationToken = default);
    Task RemoveAsync(string path, CancellationToken cancellationToken = default);
    // Atomically replace the path key; reject an existing destination rather than merging records.
    Task RelocateAsync(string oldPath, RootFolderHistory replacement, CancellationToken cancellationToken = default);
}

public interface IThumbnailCacheIndexStore
{
    Task<IReadOnlyList<ThumbnailCacheEntry>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ThumbnailCacheEntry entry, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
