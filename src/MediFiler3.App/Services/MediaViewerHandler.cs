using MediFiler3.Application.Viewing;
using MediFiler3.Domain.Files;

namespace MediFiler3.Services;

public sealed record MediaPresentation(WinUiMediaPlaybackSession Backend, MediaPlaybackController Controller, bool AudioOnly)
    : ViewerPresentation, IDisposable
{
    public void Dispose() => Controller.Dispose();
}

public sealed class MediaViewerHandler(MediaAudioSettings audio) : IFileViewerHandler<ViewerPresentation>
{
    public bool CanHandle(FileEntry file) => MediaFileTypes.Supports(file.Extension);
    public ViewerCapabilities Capabilities => ViewerCapabilities.Playback;
    public Task<ViewerContent<ViewerPresentation>> LoadAsync(FileEntry file, int width, int height, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var backend = new WinUiMediaPlaybackSession();
        return Task.FromResult(new ViewerContent<ViewerPresentation>(
            new MediaPresentation(backend, new(backend, audio), MediaFileTypes.IsAudio(file.Extension)), new(file.Size)));
    }
}
