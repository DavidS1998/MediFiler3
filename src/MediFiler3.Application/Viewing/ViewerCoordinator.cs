using MediFiler3.Domain.Files;

namespace MediFiler3.Application.Viewing;

[Flags]
public enum ViewerCapabilities { None = 0, Zoom = 1, Pan = 2, DocumentScroll = 4, Playback = 8 }
public enum ViewerInteraction { Navigation, Content }
public enum ViewerInputAction { None, Navigate, Zoom, ScrollDocument }

public static class ViewerInputPolicy
{
    public static ViewerInputAction Wheel(ViewerCapabilities capabilities, ViewerInteraction interaction,
        bool overControl = false, bool overCarousel = false)
    {
        if (overControl) return ViewerInputAction.None;
        if (overCarousel || interaction == ViewerInteraction.Navigation) return ViewerInputAction.Navigate;
        if (capabilities.HasFlag(ViewerCapabilities.Zoom)) return ViewerInputAction.Zoom;
        if (capabilities.HasFlag(ViewerCapabilities.DocumentScroll)) return ViewerInputAction.ScrollDocument;
        return ViewerInputAction.Navigate;
    }

    public static bool CanNavigateWithArrow(bool controlOwnsInput, ViewerCapabilities capabilities,
        ViewerInteraction interaction) => !controlOwnsInput &&
        !(interaction == ViewerInteraction.Content && capabilities.HasFlag(ViewerCapabilities.DocumentScroll));
}

public sealed record ViewerMetadata(long FileSize, int? PixelWidth = null, int? PixelHeight = null, TimeSpan? Duration = null);
public sealed record ViewerContent<T>(T? Presentation, ViewerMetadata Metadata, bool IsThumbnailFallback = false) : IDisposable where T : class
{
    private int _disposed;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && Presentation is IDisposable resource) resource.Dispose();
    }
}

public interface IFileViewerHandler<T> where T : class
{
    bool CanHandle(FileEntry file);
    ViewerCapabilities Capabilities { get; }
    Task<ViewerContent<T>> LoadAsync(FileEntry file, int width, int height, CancellationToken cancellationToken);
}

/// <summary>Owns one active load; callers use it on their presentation synchronization context.</summary>
public sealed class ViewerCoordinator<T>(IEnumerable<IFileViewerHandler<T>> handlers) : IDisposable where T : class
{
    private readonly IFileViewerHandler<T>[] _handlers = handlers.ToArray();
    private CancellationTokenSource? _load;
    private long _version;
    public FileEntry? CurrentFile { get; private set; }
    public ViewerContent<T>? Content { get; private set; }
    public ViewerCapabilities Capabilities { get; private set; }
    public event Action? Changed;
    public ViewerCapabilities GetCapabilities(FileEntry? file) => file is null ? ViewerCapabilities.None
        : _handlers.First(handler => handler.CanHandle(file)).Capabilities;

    public async Task<ViewerContent<T>?> LoadAsync(FileEntry file, int width, int height)
    {
        Cancel();
        var version = _version;
        var handler = _handlers.First(candidate => candidate.CanHandle(file));
        CurrentFile = file;
        Capabilities = handler.Capabilities;
        var source = new CancellationTokenSource();
        _load = source;
        var token = source.Token;
        Changed?.Invoke();
        try
        {
            var content = await handler.LoadAsync(file, width, height, token);
            if (token.IsCancellationRequested || version != _version) { content.Dispose(); return null; }
            Content = content;
            Changed?.Invoke();
            return content;
        }
        catch (Exception) when (token.IsCancellationRequested || version != _version)
        {
            return null;
        }
        finally
        {
            if (ReferenceEquals(_load, source)) _load = null;
            source.Dispose();
        }
    }

    public void Cancel()
    {
        _version++;
        _load?.Cancel();
        _load?.Dispose();
        _load = null;
        Content?.Dispose();
        Content = null;
        CurrentFile = null;
        Capabilities = ViewerCapabilities.None;
    }

    public void Dispose() => Cancel();
}
