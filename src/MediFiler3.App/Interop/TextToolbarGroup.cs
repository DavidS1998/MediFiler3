using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MediFiler3.Interop;

internal sealed class TextToolbarGroup
{
    public CommandBar Control { get; } = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right, Height = 48,
        RequestedTheme = ElementTheme.Dark,
        Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
        OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Collapsed,
        DefaultLabelPosition = CommandBarDefaultLabelPosition.Collapsed, IsDynamicOverflowEnabled = false
    };
    private readonly AppBarButton _mode;
    private readonly AppBarButton _previous;
    private readonly AppBarButton _next;

    public TextToolbarGroup(Action toggle, Action previous, Action next)
    {
        _previous = Button(Symbol.Previous, "Previous text window", previous);
        _mode = Button(Symbol.Document, "Toggle text scrolling (F4)", toggle);
        _next = Button(Symbol.Next, "Next text window", next);
        Control.PrimaryCommands.Add(_previous);
        Control.PrimaryCommands.Add(_mode);
        Control.PrimaryCommands.Add(_next);
    }

    public void SetState(bool scrolling, bool canPrevious, bool canNext)
    {
        _previous.IsEnabled = canPrevious;
        _next.IsEnabled = canNext;
        var name = scrolling ? "Switch to file navigation (F4)" : "Switch to text scrolling (F4)";
        _mode.Label = name;
        ToolTipService.SetToolTip(_mode, name);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_mode, name);
    }

    private static AppBarButton Button(Symbol icon, string label, Action action)
    {
        var button = new AppBarButton { Icon = new SymbolIcon(icon), Label = label, Width = 32,
            LabelPosition = CommandBarLabelPosition.Collapsed };
        ToolTipService.SetToolTip(button, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }
}
