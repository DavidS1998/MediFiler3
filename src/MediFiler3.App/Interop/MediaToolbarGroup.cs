using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MediFiler3.Application.Viewing;

namespace MediFiler3.Interop;

internal sealed class MediaToolbarGroup
{
    public CommandBar Control { get; } = new() { Height = 48, HorizontalAlignment = HorizontalAlignment.Right,
        DefaultLabelPosition = CommandBarDefaultLabelPosition.Collapsed, IsDynamicOverflowEnabled = false,
        OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Collapsed, RequestedTheme = ElementTheme.Dark };
    private readonly AppBarButton _play;
    private readonly AppBarButton _stop;
    private readonly AppBarButton _options;
    private readonly AppBarButton _speedButton;
    private readonly Slider _seek = new() { Width = 72, Minimum = 0, Maximum = 1, StepFrequency = 0.1 };
    private readonly TextBlock _time = new() { Width = 72, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
    private readonly Slider _volume = new() { Width = 180, Minimum = 0, Maximum = 1, StepFrequency = 0.05, Value = 0.5 };
    private readonly ComboBox _speed = new() { Width = 180 };
    private bool _updating;
    private bool _dragging;
    private bool? _playing;

    public MediaToolbarGroup(Action toggle, Action stop, Action<double> seek, Action<double> volume, Action<double> speed)
    {
        _play = Button(Symbol.Play, "Play/pause (Space)", toggle);
        _stop = Button(Symbol.Stop, "Stop playback", stop);
        _options = Button(Symbol.Volume, "Volume", () => { });
        _speedButton = Button(Symbol.Setting, "Playback speed", () => { });
        Control.PrimaryCommands.Add(_play);
        Control.PrimaryCommands.Add(_stop);
        Control.PrimaryCommands.Add(new AppBarElementContainer { Content = _seek, Width = 72, VerticalAlignment = VerticalAlignment.Center });
        Control.PrimaryCommands.Add(new AppBarElementContainer { Content = _time, Width = 72, VerticalAlignment = VerticalAlignment.Center });
        Control.PrimaryCommands.Add(_options);
        Control.PrimaryCommands.Add(_speedButton);
        var settings = new StackPanel { Spacing = 8 };
        settings.Children.Add(new TextBlock { Text = "Volume" }); settings.Children.Add(_volume);
        _options.Flyout = new Flyout { Content = settings, ShouldConstrainToRootBounds = false };
        var speedSettings = new StackPanel { Spacing = 8 };
        speedSettings.Children.Add(new TextBlock { Text = "Playback speed" }); speedSettings.Children.Add(_speed);
        _speedButton.Flyout = new Flyout { Content = speedSettings, ShouldConstrainToRootBounds = false };
        foreach (var rate in new[] { 0.5, 1, 1.25, 1.5, 2 }) _speed.Items.Add(new ComboBoxItem { Content = $"{rate:0.##}×", Tag = rate });
        _seek.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _dragging = true), true);
        _seek.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _dragging = false), true);
        _seek.PointerCaptureLost += (_, _) => _dragging = false;
        _seek.ValueChanged += (_, args) => { if (!_updating) seek(args.NewValue); };
        _volume.ValueChanged += (_, args) => { if (!_updating) volume(args.NewValue); };
        _speed.SelectionChanged += (_, _) => { if (!_updating && _speed.SelectedItem is ComboBoxItem { Tag: double rate }) speed(rate); };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_seek, "Playback position");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_volume, "Volume");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_speed, "Playback speed");
        SetState(MediaPlaybackState.Empty);
    }
    public void SetState(MediaPlaybackState state)
    {
        _updating = true;
        try
        {
            _play.IsEnabled = _stop.IsEnabled = _options.IsEnabled = _speedButton.IsEnabled = state.Ready;
            if (_playing != state.Playing)
            {
                _playing = state.Playing;
                _play.Icon = new SymbolIcon(state.Playing ? Symbol.Pause : Symbol.Play);
                var label = state.Playing ? "Pause (Space)" : "Play (Space)";
                ToolTipService.SetToolTip(_play, label);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_play, label);
            }
            _seek.IsEnabled = state.CanSeek;
            _seek.Maximum = Math.Max(0.1, state.Duration?.TotalSeconds ?? 1);
            if (!_dragging) _seek.Value = Math.Clamp(state.Position.TotalSeconds, 0, _seek.Maximum);
            _time.Text = $"{Clock(state.Position)} / {(state.Duration is { } duration ? Clock(duration) : "--:--")}";
            ToolTipService.SetToolTip(_time, _time.Text);
            _volume.Value = state.Volume;
            _speed.SelectedItem = _speed.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (double)item.Tag == state.Speed);
        }
        finally { _updating = false; }
    }
    private static string Clock(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}" : $"{value.Minutes:00}:{value.Seconds:00}";
    private static AppBarButton Button(Symbol icon, string name, Action action)
    {
        var button = new AppBarButton { Icon = new SymbolIcon(icon), Label = name, Width = 32,
            LabelPosition = CommandBarLabelPosition.Collapsed };
        ToolTipService.SetToolTip(button, name);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }
}
