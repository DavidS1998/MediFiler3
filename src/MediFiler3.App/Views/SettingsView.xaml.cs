using System.Windows;
using System.Windows.Controls;

namespace MediFiler3.Views;

public partial class SettingsView : UserControl
{
    public event Action<int>? CarouselNeighborCountChanged;
    public Func<Task>? ClearBackupsRequested { get; set; }

    public void SetBackupCleanupStatus(string message) => BackupCleanupStatus.Text = message;

    private async void OnClearBackupsClick(object sender, RoutedEventArgs args)
    {
        if (ClearBackupsRequested is null) return;
        ClearBackupsButton.IsEnabled = false;
        try { await ClearBackupsRequested(); }
        finally { ClearBackupsButton.IsEnabled = true; }
    }

    public SettingsView(int initialCarouselNeighborCount)
    {
        InitializeComponent();
        CarouselSliderIsland.SetNeighborCount(initialCarouselNeighborCount);
        CarouselSliderIsland.NeighborCountChanged += OnCarouselNeighborCountChanged;
        UpdateCarouselCountText();
    }

    private void OnCarouselNeighborCountChanged(int neighborCount)
    {
        CarouselNeighborCountChanged?.Invoke(neighborCount);
        UpdateCarouselCountText();
    }

    private void UpdateCarouselCountText()
    {
        var totalIconCount = (CarouselSliderIsland.NeighborCount * 2) + 1;
        CarouselCountText.Text = $"{totalIconCount} icons";
    }
}
