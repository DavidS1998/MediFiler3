using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MediFiler3.Interop;

/// <summary>Shared Gallery/Reader counter presentation; outline copies stay outside accessibility content.</summary>
internal static class OutlinedStatus
{
    public static Grid Create(List<TextBlock> outlines, out TextBlock text)
    {
        // HwndHost may rebuild native content after reparenting; retain only this counter's outlines.
        outlines.Clear();
        var host = new Grid { HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
            Margin = new Thickness(8, 8, 24, 8), IsHitTestVisible = false };
        for (var y = -1; y <= 1; y++)
            for (var x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0) continue;
                var outline = new TextBlock { Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform { X = x, Y = y } };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(outline, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
                outlines.Add(outline); host.Children.Add(outline);
            }
        text = new TextBlock { Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(text, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        host.Children.Add(text);
        return host;
    }

}
