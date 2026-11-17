using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MediFiler3.Interop;

public sealed partial class WinUiToolbarIsland
{
    private Grid? _galleryControls;
    private Slider? _galleryWidthSlider, _galleryHeightSlider;
    private bool _galleryControlsVisible;
    private double _galleryWidth = 200, _galleryHeight = 200;
    private bool _settingGalleryControls;
    public event Action<double, double>? GallerySizeChanged;

    public void SetGalleryControls(bool visible, double width, double height)
    {
        _galleryControlsVisible = visible;
        UpdateExternalCommandVisibility();
        _galleryWidth = width; _galleryHeight = height;
        if (_galleryControls is not null) _galleryControls.Visibility = visible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        _settingGalleryControls = true;
        try
        {
            if (_galleryWidthSlider is not null) _galleryWidthSlider.Value = width;
            if (_galleryHeightSlider is not null) _galleryHeightSlider.Value = height;
        }
        finally { _settingGalleryControls = false; }
    }

    private void UpdateExternalCommandVisibility()
    {
        var visibility = _readerControlsVisible || _galleryControlsVisible
            ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        if (_externalToolbar is not null)
        {
            _externalToolbar.OpenButton.Visibility = visibility;
            _externalToolbar.ChooseButton.Visibility = visibility;
        }
        if (_externalActionsSeparator is not null) _externalActionsSeparator.Visibility = visibility;
    }

    private Grid CreateGalleryControls()
    {
        var root = new Grid { Margin = new Thickness(8, 0, 8, 0),
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            ColumnSpacing = 8,
            Visibility = _galleryControlsVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _galleryWidthSlider = AddDimension("Width", "Gallery thumbnail width", _galleryWidth, 0);
        _galleryHeightSlider = AddDimension("Height", "Gallery thumbnail height", _galleryHeight, 2);
        _galleryControls = root;
        return root;

        Slider AddDimension(string label, string name, double value, int column)
        {
            var text = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center };
            Grid.SetColumn(text, column); root.Children.Add(text);
            var slider = new Slider { Minimum = 100, Maximum = 400, StepFrequency = 50, TickFrequency = 50,
                TickPlacement = Microsoft.UI.Xaml.Controls.Primitives.TickPlacement.None, Value = value,
                SnapsTo = Microsoft.UI.Xaml.Controls.Primitives.SliderSnapsTo.StepValues,
                Width = 150, IsThumbToolTipEnabled = false, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, name);
            ToolTipService.SetToolTip(slider, name);
            Grid.SetColumn(slider, column + 1); root.Children.Add(slider);
            slider.ValueChanged += (_, _) =>
            {
                if (_settingGalleryControls || _galleryWidthSlider is null || _galleryHeightSlider is null) return;
                _galleryWidth = _galleryWidthSlider.Value; _galleryHeight = _galleryHeightSlider.Value;
                var width = _galleryWidth; var height = _galleryHeight;
                Dispatcher.BeginInvoke(new Action(() => GallerySizeChanged?.Invoke(width, height)));
            };
            return slider;
        }
    }
}
