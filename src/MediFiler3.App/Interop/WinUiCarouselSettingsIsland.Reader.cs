using MediFiler3.Application.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MediFiler3.Interop;

public sealed partial class WinUiCarouselSettingsIsland
{
    private ReaderOptions _readerOptions = new();
    private bool _readerSync;
    private Slider? _readerSpacing, _readerPreload;
    private ToggleSwitch? _readerIndicator;
    public event Action<ReaderOptions>? ReaderOptionsChanged;

    public void SetReaderOptions(ReaderOptions options)
    {
        options.Validate(); _readerOptions = options;
        _readerSync = true;
        try
        {
            if (_readerSpacing is not null) { _readerSpacing.Value = options.Spacing; _readerSpacing.Header = $"Page spacing: {options.Spacing} px"; }
            if (_readerPreload is not null) { _readerPreload.Value = options.Preload; _readerPreload.Header = $"Upcoming pages to preload: {options.Preload}"; }
            if (_readerIndicator is not null) _readerIndicator.IsOn = options.ShowPageIndicator;
        }
        finally { _readerSync = false; }
    }
    private void ChangeReaderOptions(ReaderOptions options)
    {
        if (_readerSync || options == _readerOptions) return;
        SetReaderOptions(options); ReaderOptionsChanged?.Invoke(options);
    }
    private void AddReaderSettings(StackPanel page)
    {
        _readerSpacing = ReaderSlider(0, 64, 4, "Reader page spacing");
        _readerSpacing.TickPlacement = TickPlacement.None;
        _readerSpacing.ValueChanged += (_, args) => ChangeReaderOptions(_readerOptions with { Spacing = (int)Math.Round(args.NewValue) });
        page.Children.Add(_readerSpacing);
        _readerPreload = ReaderSlider(0, 6, 1, "Reader upcoming pages to preload");
        _readerPreload.ValueChanged += (_, args) => ChangeReaderOptions(_readerOptions with { Preload = (int)Math.Round(args.NewValue) });
        page.Children.Add(_readerPreload);
        _readerIndicator = new ToggleSwitch { Header = "Show page counter" };
        _readerIndicator.Toggled += (_, _) => ChangeReaderOptions(_readerOptions with { ShowPageIndicator = _readerIndicator.IsOn });
        page.Children.Add(_readerIndicator);
        SetReaderOptions(_readerOptions);
    }
    private static Slider ReaderSlider(int minimum, int maximum, int step, string name)
    {
        var slider = new Slider { Minimum = minimum, Maximum = maximum, StepFrequency = step,
            SnapsTo = SliderSnapsTo.StepValues, TickFrequency = step, TickPlacement = TickPlacement.BottomRight,
            IsThumbToolTipEnabled = false, Width = 320, HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, name);
        return slider;
    }
}
