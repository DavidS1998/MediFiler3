namespace MediFiler3.Application.Files;

public sealed record ThumbnailCacheUsage(long SizeBytes, int EntryCount, int RetainedCount = 0);

public interface IThumbnailCacheMaintenance
{
    Task InvalidateAsync(string path, bool descendants, CancellationToken token = default);
    Task<ThumbnailCacheUsage> MaintainAsync(bool clear = false, CancellationToken token = default);
    Task<ThumbnailCacheUsage> GetUsageAsync(CancellationToken token = default);
    Task FlushAsync(CancellationToken token = default);
}
