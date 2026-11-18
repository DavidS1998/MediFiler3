using MediFiler3.Application.Files;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace MediFiler3.Interop;

public sealed partial class WinUiTitleBarIsland
{
    private void ToggleFileFilter(Guid id) => FileFilterSelected?.Invoke(id == _activeFilterId ? null : id);

    private static double MeasureFilterTag(string name, double maximum, TextBlock measure)
    {
        measure.Text = name;
        measure.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Clamp(Math.Ceiling(measure.DesiredSize.Width) + 26, 34, maximum);
    }

    private static readonly string[] TagInteractionStates = ["PointerOver", "Pressed"];


    private static void RoundFilterChip(Button button)
    {
        button.SizeChanged += (_, args) => button.CornerRadius = new CornerRadius(args.NewSize.Height * .45);
    }

    private void StyleFilterTag(Button button, SavedFileFilter filter, bool inCaption = false)
    {
        var selected = filter.Id == _activeFilterId;
        var wasSelected = button.Resources.ContainsKey("ButtonForegroundPointerOver");
        if (selected)
        {
            var accent = new UISettings().GetColorValue(UIColorType.Accent);
            var fill = new SolidColorBrush(accent);
            var ink = new SolidColorBrush(
                .2126 * accent.R + .7152 * accent.G + .0722 * accent.B >= 145 ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White);
            if (button.Background is not SolidColorBrush previous ||
                previous.Color.R != accent.R || previous.Color.G != accent.G || previous.Color.B != accent.B || !wasSelected)
            {
                if (inCaption) ApplyCaptionBackground(button, "Button", accent);
                else button.Background = fill;
                button.Foreground = ink;
                foreach (var state in TagInteractionStates)
                {
                    if (!inCaption) button.Resources[$"ButtonBackground{state}"] = fill;
                    button.Resources[$"ButtonForeground{state}"] = ink;
                    button.Resources[$"ButtonBorderBrush{state}"] = button.Background;
                }
            }
        }
        else if (wasSelected || inCaption && button.Background != CaptionControlFill)
        {
            if (inCaption) ApplyCaptionBackground(button, "Button");
            else button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.ForegroundProperty);
            foreach (var state in TagInteractionStates)
            {
                if (!inCaption) button.Resources.Remove($"ButtonBackground{state}");
                button.Resources.Remove($"ButtonForeground{state}");
                button.Resources.Remove($"ButtonBorderBrush{state}");
            }
        }
        if (wasSelected != selected && button.IsPointerOver)
        {
            VisualStateManager.GoToState(button, "Normal", false);
            VisualStateManager.GoToState(button, button.IsPressed ? "Pressed" : "PointerOver", false);
        }
        ToolTipService.SetToolTip(button, filter.Name);
        AutomationProperties.SetName(button, selected ? $"Clear {filter.Name}, selected filter" : $"Apply {filter.Name}");
    }

    private ScrollViewer CreateSavedFilterTags(Action<Guid> selected)
    {
        const double rowWidth = 414;
        const double spacing = 6;
        var rows = new List<List<(SavedFileFilter Filter, double Width)>>();
        var measure = new TextBlock { FontSize = 14 };
        var row = new List<(SavedFileFilter, double)>();
        var remaining = rowWidth;
        foreach (var filter in _savedFileFilters.OrderBy(filter => filter.Name, StringComparer.OrdinalIgnoreCase))
        {
            var width = MeasureFilterTag(filter.Name, rowWidth, measure);
            if (width > remaining && row.Count > 0)
            {
                rows.Add(row);
                row = [];
                remaining = rowWidth;
            }
            row.Add((filter, width));
            remaining -= width + spacing;
        }
        if (row.Count > 0) rows.Add(row);

        var tags = new ItemsRepeater
        {
            ItemsSource = rows,
            Layout = new StackLayout { Spacing = 4 },
            ItemTemplate = (DataTemplate)XamlReader.Load(
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                "<StackPanel Orientation='Horizontal' Spacing='6'/></DataTemplate>")
        };
        var realized = new Dictionary<Button, SavedFileFilter>();
        tags.ElementPrepared += (_, args) =>
        {
            if (args.Element is not StackPanel panel) return;
            foreach (var (filter, width) in rows[args.Index])
            {
                var button = new Button
                {
                    Content = new TextBlock { Text = filter.Name, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis },
                    Width = width, Padding = new Thickness(11, 4, 11, 4)
                };
                RoundFilterChip(button);
                realized[button] = filter;
                StyleFilterTag(button, filter);
                button.Click += TagClicked;
                panel.Children.Add(button);
            }
        };
        tags.ElementClearing += (_, args) =>
        {
            if (args.Element is not StackPanel panel) return;
            foreach (var button in panel.Children.OfType<Button>())
            {
                button.Click -= TagClicked;
                realized.Remove(button);
            }
            panel.Children.Clear();
        };

        var scroll = new ScrollViewer
        {
            Content = tags, MaxHeight = 144,
            Padding = rows.Count > 4 ? new Thickness(8, 0, 8, 0) : new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        AutomationProperties.SetName(scroll, "Saved filters");
        return scroll;

        void TagClicked(object sender, RoutedEventArgs args)
        {
            if (sender is not Button button || !realized.TryGetValue(button, out var filter)) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                selected(filter.Id);
                foreach (var (tag, saved) in realized) StyleFilterTag(tag, saved);
            }));
        }
    }
}
