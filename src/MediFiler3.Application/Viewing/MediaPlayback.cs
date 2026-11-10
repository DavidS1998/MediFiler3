namespace MediFiler3.Application.Viewing;

public static class MediaFileTypes
{
    private static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".wmv", ".webm", ".mkv" };
    private static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".wma", ".m4a", ".aac", ".opus" };
    public static bool Supports(string extension) => Video.Contains(extension) || Audio.Contains(extension);
    public static bool IsAudio(string extension) => Audio.Contains(extension);
}

public interface IMediaPlaybackSession : IDisposable
{
    event Action? Changed;
    event Action? Ended;
    event Action<string>? Failed;
    TimeSpan? Duration { get; }
    TimeSpan Position { get; }
    double Volume { get; set; }
    bool IsMuted { get; set; }
    Task OpenAsync(string path, CancellationToken cancellationToken);
    void Play();
    void Pause();
    void Seek(TimeSpan position);
}

public sealed record MediaPlaybackState(bool Ready, bool Playing, TimeSpan Position, TimeSpan? Duration, double Volume, bool Muted)
{
    public bool CanSeek => Ready && Duration is { } duration && duration > TimeSpan.Zero;
    public static MediaPlaybackState Empty { get; } = new(false, false, TimeSpan.Zero, null, 0.5, false);
}

public sealed class MediaAudioSettings
{
    public MediaAudioSettings(double volume = 0.5, bool muted = false)
    {
        if (!double.IsFinite(volume) || volume is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(volume));
        Volume = volume;
        Muted = muted;
    }
    public double Volume { get; internal set; } = 0.5;
    public bool Muted { get; internal set; }
}

public sealed class MediaPlaybackController : IDisposable
{
    private readonly IMediaPlaybackSession _session;
    private readonly MediaAudioSettings _audio;
    private bool _disposed;
    private bool _ready;
    private bool _playing;
    public event Action? Changed;
    public event Action<string>? Failed;
    public MediaPlaybackState State => _disposed ? MediaPlaybackState.Empty
        : new(_ready, _playing, _session.Position, _session.Duration, _session.Volume, _session.IsMuted);

    public MediaPlaybackController(IMediaPlaybackSession session, MediaAudioSettings? audio = null)
    {
        _session = session;
        _audio = audio ?? new MediaAudioSettings();
        _session.Volume = _audio.Volume;
        _session.IsMuted = _audio.Muted;
        session.Changed += OnChanged;
        session.Ended += OnEnded;
        session.Failed += OnFailed;
    }

    public async Task OpenAsync(string path, CancellationToken token)
    {
        await _session.OpenAsync(path, token);
        if (_disposed || token.IsCancellationRequested) return;
        _session.Pause();
        _ready = true;
        Changed?.Invoke();
    }

    public void Toggle()
    {
        if (!_ready || _disposed) return;
        if (_playing) _session.Pause();
        else
        {
            if (_session.Duration is { } duration && _session.Position >= duration) _session.Seek(TimeSpan.Zero);
            _session.Play();
        }
        _playing = !_playing;
        Changed?.Invoke();
    }

    public void Stop()
    {
        if (!_ready || _disposed) return;
        _session.Pause(); _session.Seek(TimeSpan.Zero); _playing = false;
        Changed?.Invoke();
    }

    public void Seek(double seconds)
    {
        if (!State.CanSeek || !double.IsFinite(seconds)) return;
        _session.Seek(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, _session.Duration!.Value.TotalSeconds)));
        Changed?.Invoke();
    }
    public void SetVolume(double volume)
    {
        if (_disposed || !double.IsFinite(volume)) return;
        _audio.Volume = Math.Clamp(volume, 0, 1);
        _session.Volume = _audio.Volume; Changed?.Invoke();
    }
    public void ToggleMute()
    {
        if (_disposed) return;
        _audio.Muted = !_audio.Muted;
        _session.IsMuted = _audio.Muted;
        Changed?.Invoke();
    }
    public void Jump(double seconds)
    {
        if (double.IsFinite(seconds) && State.CanSeek) Seek(_session.Position.TotalSeconds + seconds);
    }
    private void OnChanged() { if (!_disposed) Changed?.Invoke(); }
    private void OnEnded() { _playing = false; OnChanged(); }
    private void OnFailed(string message) { if (_disposed) return; _ready = _playing = false; Failed?.Invoke(message); OnChanged(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _ready = _playing = false;
        _session.Changed -= OnChanged; _session.Ended -= OnEnded; _session.Failed -= OnFailed;
        _session.Dispose();
    }
}
