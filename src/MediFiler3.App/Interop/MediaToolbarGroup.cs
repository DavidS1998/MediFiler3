using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MediFiler3.Application.Viewing;

namespace MediFiler3.Interop;

internal sealed class MediaToolbarGroup
{
    public Grid Control { get; } = new() { Height = 48, HorizontalAlignment = HorizontalAlignment.Stretch,
        RequestedTheme = ElementTheme.Dark };
    private readonly AppBarButton _play;
    private readonly AppBarButton _stop;
    private readonly AppBarButton _back;
    private readonly AppBarButton _forward;
    private readonly AppBarButton _options;
    private readonly Slider _seek = new() { MinWidth = 16, Minimum = 0, Maximum = 1, StepFrequency = 0.1,
        HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
    private readonly TextBlock _time = new() { Width = 72, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
    private readonly Slider _volume = new() { Width = 160, Height = 32, MinHeight = 0,
        Minimum = 0, Maximum = 100, StepFrequency = 1, Value = 50,
        VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _mute = new() { Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new Thickness(6),
        VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _volumePercent = new() { Width = 24, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Right };
    private bool _updating;
    private bool _dragging;
    private bool? _playing;
    private bool? _muted;

    public MediaToolbarGroup(Action toggle, Action stop, Action<double> seek, Action<double> volume,
        Action mute, Action<double> jump)
    {
        _back = Button(Symbol.Back, "Jump back 5 seconds", () => jump(-5));
        _back.Icon = JumpIcon("\uEB9E");
        _play = Button(Symbol.Play, "Play/pause (Space)", toggle);
        _stop = Button(Symbol.Stop, "Stop playback", stop);
        _forward = Button(Symbol.Forward, "Jump forward 5 seconds", () => jump(5));
        _forward.Icon = JumpIcon("\uEB9D");
        _options = Button(Symbol.Volume, "Volume (right-click to mute)", () => { });
        FrameworkElement[] controls = [_back, _play, _stop, _forward, _seek, _time, _options];
        for (var index = 0; index < controls.Length; index++)
        {
            Control.ColumnDefinitions.Add(new ColumnDefinition { Width = index == 4
                ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Grid.SetColumn(controls[index], index);
            Control.Children.Add(controls[index]);
        }
        var settings = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        settings.Children.Add(_mute); settings.Children.Add(_volume); settings.Children.Add(_volumePercent);
        var presenterStyle = new Style { TargetType = typeof(FlyoutPresenter) };
        presenterStyle.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(8)));
        presenterStyle.Setters.Add(new Setter(FlyoutPresenter.MinWidthProperty, 0d));
        presenterStyle.Setters.Add(new Setter(FlyoutPresenter.MinHeightProperty, 0d));
        _options.Flyout = new Flyout { Content = settings, ShouldConstrainToRootBounds = false,
            FlyoutPresenterStyle = presenterStyle };
        _options.AddHandler(UIElement.RightTappedEvent, new Microsoft.UI.Xaml.Input.RightTappedEventHandler((_, args) =>
        { args.Handled = true; mute(); }), true);
        _mute.Click += (_, _) => mute();
        _seek.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _dragging = true), true);
        _seek.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _dragging = false), true);
        _seek.PointerCaptureLost += (_, _) => _dragging = false;
        _seek.ValueChanged += (_, args) => { if (!_updating) seek(args.NewValue); };
        _volume.ValueChanged += (_, args) => { if (!_updating) volume(args.NewValue / 100d); };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_seek, "Playback position");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_volume, "Volume");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_mute, "Mute audio");
        SetState(MediaPlaybackState.Empty);
    }
    public void SetState(MediaPlaybackState state)
    {
        _updating = true;
        try
        {
            _play.IsEnabled = _stop.IsEnabled = _options.IsEnabled = state.Ready;
            _back.IsEnabled = _forward.IsEnabled = state.CanSeek;
            if (_playing != state.Playing)
            {
                _playing = state.Playing;
                _play.Icon = new SymbolIcon(state.Playing ? Symbol.Pause : Symbol.Play);
                var label = state.Playing ? "Pause (Space)" : "Play (Space)";
                ToolTipService.SetToolTip(_play, label);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_play, label);
            }
            if (_muted != state.Muted)
            {
                _muted = state.Muted;
                _options.Icon = new SymbolIcon(state.Muted ? Symbol.Mute : Symbol.Volume);
                _mute.Content = new FontIcon { Glyph = state.Muted ? "\uE74F" : "\uE767", FontSize = 16,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons") };
                ToolTipService.SetToolTip(_mute, state.Muted ? "Unmute" : "Mute");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_mute, state.Muted ? "Unmute audio" : "Mute audio");
                var label = state.Muted ? "Volume (right-click to unmute)" : "Volume (right-click to mute)";
                ToolTipService.SetToolTip(_options, label);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_options, state.Muted ? "Volume, muted" : "Volume");
            }
            _seek.IsEnabled = state.CanSeek;
            _seek.Maximum = Math.Max(0.1, state.Duration?.TotalSeconds ?? 1);
            if (!_dragging) _seek.Value = Math.Clamp(state.Position.TotalSeconds, 0, _seek.Maximum);
            _time.Text = $"{Clock(state.Position)} / {(state.Duration is { } duration ? Clock(duration) : "--:--")}";
            ToolTipService.SetToolTip(_time, _time.Text);
            _volume.Value = Math.Round(state.Volume * 100);
            _volumePercent.Text = Math.Round(state.Volume * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
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
    private static FontIcon JumpIcon(string glyph) => new()
    {
        Glyph = glyph,
        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons")
    };
}
