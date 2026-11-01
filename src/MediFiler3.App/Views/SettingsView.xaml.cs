using System.Windows;
using System.Windows.Controls;

namespace MediFiler3.Views;

public partial class SettingsView : UserControl
{
    public event Action<int>? CarouselNeighborCountChanged;

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
