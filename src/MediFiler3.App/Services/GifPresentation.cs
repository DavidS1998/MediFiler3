using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XamlAnimatedGif;

namespace MediFiler3.Services;

/// <summary>One animation surface per load isolates late library callbacks from subsequent selections.</summary>
public sealed record GifPresentation : ViewerPresentation, IDisposable
{
    private readonly MemoryStream _source;
    private bool _disposed;
    public int Width { get; }
    public int Height { get; }
    public TimeSpan Duration { get; }
    public Image Surface { get; } = new() { Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.5), IsHitTestVisible = false };
    public event Action? Ready;
    public event Action<string>? Failed;

    public GifPresentation(MemoryStream source, int width, int height, TimeSpan duration)
    {
        _source = source; Width = width; Height = height; Duration = duration;
        AnimationBehavior.AddLoadedHandler(Surface, OnReady);
        AnimationBehavior.AddErrorHandler(Surface, OnError);
    }
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AnimationBehavior.SetCacheFramesInMemory(Surface, false);
        // Default repeat behavior respects the GIF's own loop count; no extension means play once.
        AnimationBehavior.SetSourceStream(Surface, _source);
    }
    private void OnReady(object sender, RoutedEventArgs args) { if (!_disposed) Ready?.Invoke(); }
    private void OnError(object sender, AnimationErrorEventArgs args)
    {
        args.Handled = true;
        if (!_disposed) Failed?.Invoke(args.Exception.Message);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AnimationBehavior.RemoveLoadedHandler(Surface, OnReady);
        AnimationBehavior.RemoveErrorHandler(Surface, OnError);
        AnimationBehavior.SetSourceStream(Surface, null);
        Surface.Source = null;
        _source.Dispose();
        Ready = null; Failed = null;
    }
}
