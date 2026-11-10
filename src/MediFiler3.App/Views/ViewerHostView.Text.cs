using System.Windows;
using System.Windows.Input;
using MediFiler3.Application.Files;
using MediFiler3.Application.Viewing;

namespace MediFiler3.Views;

public partial class ViewerHostView
{
    public static readonly DependencyProperty TextSideClearanceProperty = DependencyProperty.Register(
        nameof(TextSideClearance), typeof(double), typeof(ViewerHostView),
        new PropertyMetadata(300d, OnTextSideClearanceChanged));

    public double TextSideClearance
    {
        get => (double)GetValue(TextSideClearanceProperty);
        set => SetValue(TextSideClearanceProperty, value);
    }

    private static void OnTextSideClearanceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var host = (ViewerHostView)sender;
        var inset = Math.Max(0, (double)args.NewValue) + 24;
        if (host.TextPanel is not null) host.TextPanel.Margin = new Thickness(inset, 12, inset, 24);
    }

    private TextWindow? _textWindow;
    private long _textOffset;
    private double _textScrollOffset;
    private bool _textInteraction;
    private bool _textLoading;
    private readonly Stack<long> _previousTextOffsets = new();
    public bool IsTextInteraction => _textInteraction;
    public bool CanReadPreviousTextWindow => !_textLoading && _previousTextOffsets.Count > 0;
    public bool CanReadNextTextWindow => !_textLoading && _textWindow?.HasNext == true;

    public void ToggleTextInteraction()
    {
        if (!Capabilities.HasFlag(ViewerCapabilities.DocumentScroll)) return;
        _textInteraction = !_textInteraction;
        _wheelDeltaRemainder = 0;
        UpdateTextMode();
        TextViewer.Focus();
        PresentationChanged?.Invoke();
    }

    private void UpdateTextMode()
    {
        var mode = _textInteraction ? "Text scrolling" : "File navigation";
        var window = _textWindow is { } text
            ? $"{text.EncodingName} · bytes {text.StartOffset}–{text.NextOffset} of {text.FileLength}. " : "";
        TextModeNotice.Text = $"{window}{mode}. Right-click or press F4 to switch.";
    }

    private void OnTextPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Capabilities.HasFlag(ViewerCapabilities.DocumentScroll)) return;
        if (!_textInteraction) OnImageViewportMouseWheel(sender, e);
    }

    public async Task ReadTextWindowAsync(bool next)
    {
        if (!Capabilities.HasFlag(ViewerCapabilities.DocumentScroll) || _textLoading) return;
        if (next)
        {
            if (_textWindow?.HasNext != true) return;
            _previousTextOffsets.Push(_textOffset);
            _textOffset = _textWindow.NextOffset;
        }
        else
        {
            if (!_previousTextOffsets.TryPop(out var previous)) return;
            _textOffset = previous;
        }
        _textScrollOffset = 0;
        _metadata = _currentFile is { } file ? new(file.Size) : null;
        _textWindow = null;
        _textLoading = true;
        TextViewer.Text = string.Empty;
        TextPanel.Visibility = Visibility.Collapsed;
        PresentationChanged?.Invoke();
        try { await LoadCurrentImageAsync(true); }
        finally { _textLoading = false; PresentationChanged?.Invoke(); }
    }
}
