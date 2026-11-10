using System.Windows;
using System.Windows.Media.Imaging;
using MediFiler3.Application.Files;
using MediFiler3.Application.Viewing;
using MediFiler3.Domain.Files;
using MediFiler3.Services;

namespace MediFiler3.Views;

public partial class ViewerHostView
{
    private MediaPresentation? _media;
    private CancellationTokenSource? _mediaLoad;
    private readonly MediaAudioSettings _mediaAudio = new();
    private IThumbnailService? _viewerThumbnails;
    public MediaPlaybackState PlaybackState => _media?.Controller.State ?? MediaPlaybackState.Empty with { Volume = _mediaAudio.Volume, Muted = _mediaAudio.Muted };
    public event Action? PlaybackStateChanged;

    private async Task OpenMediaAsync(FileEntry file, MediaPresentation media, BitmapSource? preview, long request)
    {
        _media = media;
        _mediaLoad = new CancellationTokenSource();
        var token = _mediaLoad.Token;
        media.Controller.Changed += OnPlaybackChanged;
        media.Controller.Failed += OnPlaybackFailed;
        media.Backend.Element.WheelRequested += OnNativeMediaWheel;
        media.Backend.Element.ToggleRequested += TogglePlayback;
        media.Backend.Element.KeyRequested += OnNativeMediaKey;
        MediaHost.Content = media.AudioOnly ? null : media.Backend.Element;
        MediaHost.Visibility = media.AudioOnly ? Visibility.Collapsed : Visibility.Visible;
        AudioPanel.Visibility = media.AudioOnly ? Visibility.Visible : Visibility.Collapsed;
        AudioTitle.Text = file.Name;
        AudioArtwork.Source = media.AudioOnly ? preview : null;
        AudioArtwork.Visibility = media.AudioOnly && preview is not null ? Visibility.Visible : Visibility.Collapsed;
        if (media.AudioOnly && preview is null) _ = LoadAudioArtworkAsync(file, media, token);
        ImageViewer.Source = null;
        _resizeTimer.Stop();
        ImageViewer.Visibility = TextPanel.Visibility = ThumbnailFallbackBadge.Visibility = Visibility.Collapsed;
        ImageMessagePanel.Visibility = Visibility.Collapsed;
        PresentationChanged?.Invoke();
        try
        {
            await media.Controller.OpenAsync(file.FullPath, token);
            if (request != _requestVersion || !ReferenceEquals(_media, media) || !_active) return;
            _metadata = new(file.Size, Duration: media.Controller.State.Duration);
            PresentationChanged?.Invoke();
            OnPlaybackChanged();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (request == _requestVersion && ReferenceEquals(_media, media) && _active) OnPlaybackFailed(exception.Message);
        }
    }

    public bool HasVideoSurface => _media is { AudioOnly: false };
    private async Task LoadAudioArtworkAsync(FileEntry file, MediaPresentation media, CancellationToken token)
    {
        try
        {
            if (_viewerThumbnails is null) return;
            var bytes = await _viewerThumbnails.GetThumbnailPngAsync(file, 220, 220, token);
            if (bytes is null) return;
            var bitmap = await Task.Run(() => ThumbnailViewerHandler.DecodePng(bytes), token);
            if (token.IsCancellationRequested || !_active || !ReferenceEquals(file, _currentFile) || !ReferenceEquals(media, _media)) return;
            AudioArtwork.Source = bitmap;
            AudioArtwork.Visibility = Visibility.Visible;
        }
        catch (Exception) { /* Optional artwork failure must not interrupt playback or reserve an empty image slot. */ }
    }
    public void SetVideoExclusions(IEnumerable<Rect> exclusions)
    {
        if (_media is not { AudioOnly: false } media || !media.Backend.Element.IsLoaded) return;
        var origin = media.Backend.Element.TransformToAncestor(this).Transform(new Point(0, 0));
        var regions = exclusions.Select(rect => new Rect(rect.X - origin.X, rect.Y - origin.Y, rect.Width, rect.Height)).ToList();
        if (ExternalActionNotice.Visibility == Visibility.Visible)
        {
            var notice = ExternalActionNotice.TransformToAncestor(this).TransformBounds(new Rect(ExternalActionNotice.RenderSize));
            regions.Add(new Rect(notice.X - origin.X, notice.Y - origin.Y, notice.Width, notice.Height));
        }
        media.Backend.Element.SetExclusions(regions);
    }
    private void OnNativeMediaWheel(int delta)
    {
        _wheelDeltaRemainder += delta;
        var notches = _wheelDeltaRemainder / 120;
        _wheelDeltaRemainder %= 120;
        for (var index = 0; index < Math.Abs(notches); index++) NavigationRequested?.Invoke(notches < 0 ? 1 : -1);
    }
    private void OnNativeMediaKey(int key, bool control, bool shift)
    {
        if (key == 13 && control)
            _ = OpenExternallyAsync(shift ? MediFiler3.Application.Files.ExternalFileAction.ChooseApplication
                : MediFiler3.Application.Files.ExternalFileAction.DefaultApplication);
        else if (key == 32 && !control && !shift) TogglePlayback();
        else if (!control && !shift && key is 37 or 38 or 39 or 40) NavigationRequested?.Invoke(key is 37 or 38 ? -1 : 1);
    }

    private void OnPlaybackChanged() => PlaybackStateChanged?.Invoke();
    private void OnPlaybackFailed(string message)
    {
        var file = _currentFile;
        ReleaseMedia();
        if (!_active || file is null) return;
        _metadata = new(file.Size);
        ShowImageMessage("This media could not be played.", $"The format/codec may be unavailable in Windows, or the file may be unreadable.\n{message}");
        ShowExternalFallback(file, previewFailed: true);
    }

    private void ReleaseMedia()
    {
        _mediaLoad?.Cancel(); _mediaLoad?.Dispose(); _mediaLoad = null;
        if (_media is { } media)
        {
            _media = null;
            media.Controller.Changed -= OnPlaybackChanged;
            media.Controller.Failed -= OnPlaybackFailed;
            media.Backend.Element.WheelRequested -= OnNativeMediaWheel;
            media.Backend.Element.ToggleRequested -= TogglePlayback;
            media.Backend.Element.KeyRequested -= OnNativeMediaKey;
            MediaHost.Content = null;
            media.Dispose();
        }
        MediaHost.Content = null;
        MediaHost.Visibility = AudioPanel.Visibility = Visibility.Collapsed;
        AudioArtwork.Source = null;
        AudioArtwork.Visibility = Visibility.Collapsed;
        OnPlaybackChanged();
    }

    public void TogglePlayback() => RunPlayback(controller => controller.Toggle());
    public void StopPlayback() => RunPlayback(controller => controller.Stop());
    public void SeekPlayback(double seconds) => RunPlayback(controller => controller.Seek(seconds));
    public void SetPlaybackVolume(double volume) => RunPlayback(controller => controller.SetVolume(volume));
    public void TogglePlaybackMute() => RunPlayback(controller => controller.ToggleMute());
    public void JumpPlayback(double seconds) => RunPlayback(controller => controller.Jump(seconds));
    private void RunPlayback(Action<MediaPlaybackController> action)
    {
        if (_media is null) return;
        try { action(_media.Controller); }
        catch (Exception exception) { OnPlaybackFailed(exception.Message); }
    }

    public IDisposable SuspendMediaForFileOperation()
    {
        var file = _currentFile;
        var hadMedia = _media is not null || Capabilities.HasFlag(ViewerCapabilities.Playback);
        if (hadMedia)
        {
            _requestVersion++;
            ReleaseMedia();
            _coordinator?.Cancel();
        }
        return new ResumeMedia(() =>
        {
            if (hadMedia && _active && ReferenceEquals(file, _currentFile) && _media is null)
                _ = LoadCurrentImageAsync(true);
        });
    }
    private sealed class ResumeMedia(Action resume) : IDisposable { public void Dispose() => resume(); }
}
