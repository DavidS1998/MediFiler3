using MediFiler3.Application.Files;
using System.Windows;
using System.Windows.Controls;

namespace MediFiler3.Views;

public partial class SettingsView : UserControl
{
    private long _cacheRequestRevision;
    public Func<Task<ThumbnailCacheUsage>>? CacheUsageRequested { get; set; }
    public Func<Task<ThumbnailCacheUsage>>? ClearThumbnailCacheRequested { get; set; }

    private async void OnCacheLoaded(object sender, RoutedEventArgs args) => await UpdateCacheUsageAsync();
    private async void OnClearThumbnailCache()
    {
        if (ClearThumbnailCacheRequested is null) { CarouselSliderIsland.SetCacheState(false); return; }
        _cacheRequestRevision++;
        CarouselSliderIsland.SetCacheState(true);
        try
        {
            var usage = await ClearThumbnailCacheRequested();
            CarouselSliderIsland.SetCacheState(true, $"{usage.EntryCount} entries retained ({usage.SizeBytes / 1048576d:F1} MiB).");
        }
        catch (Exception ex) { CarouselSliderIsland.SetCacheState(true, $"Could not clear cache: {ex.Message}"); }
        finally { CarouselSliderIsland.SetCacheState(false); }
    }
    private async Task UpdateCacheUsageAsync()
    {
        if (CacheUsageRequested is null) return;
        var revision = ++_cacheRequestRevision;
        try
        {
            var usage = await CacheUsageRequested();
            if (revision != _cacheRequestRevision) return;
            CarouselSliderIsland.SetCacheState(false, $"{usage.SizeBytes / 1048576d:F1} MiB in {usage.EntryCount} entries.");
        }
        catch (Exception ex) { if (revision == _cacheRequestRevision) CarouselSliderIsland.SetCacheState(false, $"Cache usage unavailable: {ex.Message}"); }
    }

    public event Action<int>? CarouselNeighborCountChanged;
    public event Action<bool>? CarouselEnabledChanged;
    public Func<Task>? ClearBackupsRequested { get; set; }

    public void SetBackupCleanupStatus(string message) => CarouselSliderIsland.SetBackupState(true, message);

    private async void OnClearBackupsClick()
    {
        if (ClearBackupsRequested is null) { CarouselSliderIsland.SetBackupState(false); return; }
        CarouselSliderIsland.SetBackupState(true);
        try { await ClearBackupsRequested(); }
        finally { CarouselSliderIsland.SetBackupState(false); }
    }

    public SettingsView(int initialCarouselNeighborCount, bool carouselEnabled = true)
    {
        InitializeComponent();
        CarouselSliderIsland.SetCarouselEnabled(carouselEnabled);
        CarouselSliderIsland.CarouselEnabledChanged += value => CarouselEnabledChanged?.Invoke(value);
        CarouselSliderIsland.ClearBackupsRequested += OnClearBackupsClick;
        CarouselSliderIsland.ClearCacheRequested += OnClearThumbnailCache;
        Loaded += OnCacheLoaded;
        CarouselSliderIsland.SetNeighborCount(initialCarouselNeighborCount);
        CarouselSliderIsland.NeighborCountChanged += OnCarouselNeighborCountChanged;

    }

    private void OnCarouselNeighborCountChanged(int neighborCount)
    {
        CarouselNeighborCountChanged?.Invoke(neighborCount);

    }

}
