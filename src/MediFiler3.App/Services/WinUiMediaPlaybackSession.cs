using System.IO;
using System.Windows.Threading;
using MediFiler3.Application.Viewing;
using MediFiler3.Interop;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace MediFiler3.Services;

public sealed class WinUiMediaPlaybackSession : IMediaPlaybackSession
{
    private readonly MediaPlayer _player = new() { AutoPlay = false, Volume = 0.5 };
    public WinUiMediaIsland Element { get; }
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private MediaSource? _source;
    private bool _disposed;
    public event Action? Changed;
    public event Action? Ended;
    public event Action<string>? Failed;
    public TimeSpan? Duration => _player.PlaybackSession.NaturalDuration > TimeSpan.Zero ? _player.PlaybackSession.NaturalDuration : null;
    public TimeSpan Position => _player.PlaybackSession.Position;
    public int? VideoHeight => _player.PlaybackSession.NaturalVideoHeight is > 0 and var height ? checked((int)height) : null;
    public double Volume { get => _player.Volume; set => _player.Volume = value; }
    public bool IsMuted { get => _player.IsMuted; set => _player.IsMuted = value; }

    public WinUiMediaPlaybackSession()
    {
        Element = new WinUiMediaIsland(_player);
        _player.CommandManager.IsEnabled = false;
        _player.MediaOpened += OnOpened; _player.MediaEnded += OnEnded; _player.MediaFailed += OnFailed;
        _timer.Tick += OnTick;
    }
    public async Task OpenAsync(string path, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        _source = MediaSource.CreateFromUri(new Uri(path, UriKind.Absolute));
        _player.Source = _source;
        _player.Pause();
        await _opened.Task.WaitAsync(TimeSpan.FromSeconds(30), linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        _timer.Start();
    }
    private void OnOpened(MediaPlayer sender, object args) => Dispatch(() => _opened.TrySetResult());
    private void OnEnded(MediaPlayer sender, object args) => Dispatch(() => Ended?.Invoke());
    private void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        var message = $"{args.Error}: {args.ErrorMessage}";
        Dispatch(() =>
        {
            if (!_opened.Task.IsCompleted) _opened.TrySetException(new InvalidDataException(message));
            else Failed?.Invoke(message);
        });
    }
    private void Dispatch(Action action) => _dispatcher.BeginInvoke(new Action(() => { if (!_disposed) action(); }));
    private void OnTick(object? sender, EventArgs args) { if (!_disposed) Changed?.Invoke(); }
    public void Play() => _player.Play();
    public void Pause() => _player.Pause();
    public void Seek(TimeSpan position) => _player.PlaybackSession.Position = position;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel();
        _timer.Stop(); _timer.Tick -= OnTick;
        _player.MediaOpened -= OnOpened; _player.MediaEnded -= OnEnded; _player.MediaFailed -= OnFailed;
        Element.DetachPlayer(); Element.Dispose();
        _player.Pause(); _player.Source = null;
        _source?.Dispose(); _source = null;
        _player.Dispose(); _lifetime.Dispose();
    }
}
