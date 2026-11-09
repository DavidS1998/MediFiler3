using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MediFiler3.Interop;

/// <summary>The image handler's contextual commands, independent of the shell toolbar layout.</summary>
internal sealed class ImageToolbarGroup
{
    public CommandBar Control { get; } = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
        OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Collapsed,
        DefaultLabelPosition = CommandBarDefaultLabelPosition.Collapsed,
        IsDynamicOverflowEnabled = false,
        Height = 48
    };
    private readonly TextBlock _percentage = new() { Width = 32, VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), FontSize = 12, Margin = new Thickness(-5, 0, 0, 0) };
    private readonly AppBarButton _fit;

    public ImageToolbarGroup(Action zoomOut, Action fit, Action zoomIn)
    {
        Control.PrimaryCommands.Add(Button(Symbol.ZoomOut, "Zoom out", zoomOut));
        _fit = Button(Symbol.Zoom, "Reset zoom to fit", fit);
        Control.PrimaryCommands.Add(_fit);
        Control.PrimaryCommands.Add(new AppBarElementContainer
        {
            Content = _percentage,
            Width = 32,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        });
        Control.PrimaryCommands.Add(Button(Symbol.ZoomIn, "Zoom in", zoomIn));
    }

    public void SetZoom(double zoom)
    {
        _percentage.Text = $"{zoom:P0}";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_fit, $"Reset zoom to fit, {zoom:P0}");
    }

    private static AppBarButton Button(Symbol symbol, string label, Action action)
    {
        var button = new AppBarButton { Icon = new SymbolIcon(symbol), Label = label, Width = 32,
            LabelPosition = CommandBarLabelPosition.Collapsed };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        button.Click += (_, _) => action();
        return button;
    }
}
