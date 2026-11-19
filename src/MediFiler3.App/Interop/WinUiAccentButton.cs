using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace MediFiler3.Interop;

internal static class WinUiAccentButton
{
    private static readonly string[] InteractionStates = ["PointerOver", "Pressed"];

    public static void SetSelected(Button button, bool selected, Action<Button, Windows.UI.Color?>? applyBackground = null)
    {
        var wasSelected = button.Resources.ContainsKey("ButtonForegroundPointerOver");
        if (selected)
        {
            var accent = new UISettings().GetColorValue(UIColorType.Accent);
            if (button.Background is not SolidColorBrush previous ||
                previous.Color.R != accent.R || previous.Color.G != accent.G || previous.Color.B != accent.B || !wasSelected)
            {
                if (applyBackground is null) button.Background = new SolidColorBrush(accent);
                else applyBackground(button, accent);
                var ink = new SolidColorBrush(
                    .2126 * accent.R + .7152 * accent.G + .0722 * accent.B >= 145 ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White);
                button.Foreground = ink;
                foreach (var state in InteractionStates)
                {
                    if (applyBackground is null) button.Resources[$"ButtonBackground{state}"] = button.Background;
                    button.Resources[$"ButtonForeground{state}"] = ink;
                    button.Resources[$"ButtonBorderBrush{state}"] = button.Background;
                }
            }
        }
        else if (wasSelected || applyBackground is not null && button.Background is null)
        {
            if (applyBackground is null) button.ClearValue(Control.BackgroundProperty);
            else applyBackground(button, null);
            button.ClearValue(Control.ForegroundProperty);
            foreach (var state in InteractionStates)
            {
                if (applyBackground is null) button.Resources.Remove($"ButtonBackground{state}");
                button.Resources.Remove($"ButtonForeground{state}");
                button.Resources.Remove($"ButtonBorderBrush{state}");
            }
        }

        if (wasSelected != selected && button.IsPointerOver)
        {
            VisualStateManager.GoToState(button, "Normal", false);
            VisualStateManager.GoToState(button, button.IsPressed ? "Pressed" : "PointerOver", false);
        }
    }
}
