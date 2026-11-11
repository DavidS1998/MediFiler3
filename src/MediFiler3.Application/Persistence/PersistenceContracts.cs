namespace MediFiler3.Application.Persistence;

public sealed record ApplicationSettings(bool Muted = false, double Volume = 0.5,
    int CarouselIconCount = 11, bool FolderTreePinned = false, bool CarouselEnabled = true,
    int GalleryThumbnailWidth = 150, int GalleryThumbnailHeight = 150)
{
    public static int SnapGalleryDimension(double value) =>
        (int)(Math.Round(Math.Clamp(value, 100, 400) / 50, MidpointRounding.AwayFromZero) * 50);

    // Accept the previous slider's valid values without discarding unrelated settings.
    // Normalization is in-memory until the user makes a real settings change.
    public ApplicationSettings NormalizeGalleryDimensions() => this with
    {
        GalleryThumbnailWidth = GalleryThumbnailWidth is >= 64 and <= 400 ? SnapGalleryDimension(GalleryThumbnailWidth) : GalleryThumbnailWidth,
        GalleryThumbnailHeight = GalleryThumbnailHeight is >= 64 and <= 400 ? SnapGalleryDimension(GalleryThumbnailHeight) : GalleryThumbnailHeight
    };

    public void Validate()
    {
        if (!double.IsFinite(Volume) || Volume is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Volume));
        if (CarouselIconCount is < 1 or > 25 || CarouselIconCount % 2 == 0)
            throw new ArgumentOutOfRangeException(nameof(CarouselIconCount));
        if (GalleryThumbnailWidth is < 100 or > 400 || GalleryThumbnailHeight is < 100 or > 400 ||
            GalleryThumbnailWidth % 50 != 0 || GalleryThumbnailHeight % 50 != 0)
            throw new ArgumentOutOfRangeException(nameof(GalleryThumbnailWidth));
    }
}

// Bounds are normal restore bounds in logical units; minimized state is never persisted.
public sealed record WindowLayout(double Left, double Top, double Width, double Height,
    bool Maximized, double FolderTreeWidth, string? MonitorDeviceName = null,
    double? MonitorOffsetX = null, double? MonitorOffsetY = null)
{
    public void Validate()
    {
        if (!double.IsFinite(Left) || !double.IsFinite(Top) ||
            !double.IsFinite(Width) || Width <= 0 || !double.IsFinite(Height) || Height <= 0 ||
            !double.IsFinite(FolderTreeWidth) || FolderTreeWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(Width), "Layout must contain finite bounds and positive sizes.");
        if (MonitorOffsetX.HasValue != MonitorOffsetY.HasValue ||
            MonitorOffsetX is { } x && !double.IsFinite(x) || MonitorOffsetY is { } y && !double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(MonitorOffsetX), "Monitor offsets must be a finite pair.");
    }
}

// Records describe explicitly opened roots only. Subfolder navigation must not create records.
// A missing/renamed root remains saved until the user ignores, locates, or removes it.
public sealed record RootFolderHistory(string Path, long OpenCount, DateTimeOffset? LastOpenedUtc,
    bool ShowInRecent, bool Favorite);

// CacheFileName is a leaf name inside the existing owned thumbnail directory, never a source path.
public sealed record ThumbnailCacheEntry(string Key, string CacheFileName, long SizeBytes,
    DateTimeOffset LastAccessUtc, string? SourcePath = null, bool Invalidated = false);

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
