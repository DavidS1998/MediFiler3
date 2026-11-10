using MediFiler3.Application.Viewing;

namespace MediFiler3.Infrastructure.Tests;

public sealed class MediaPlaybackTests
{
    [Theory]
    [InlineData(".mp4", false)] [InlineData(".M4V", false)] [InlineData(".wmv", false)]
    [InlineData(".webm", false)] [InlineData(".MKV", false)] [InlineData(".opus", true)]
    [InlineData(".mp3", true)] [InlineData(".wav", true)] [InlineData(".wma", true)]
    [InlineData(".m4a", true)] [InlineData(".aac", true)]
    public void InitialFormatsAreRoutedByType(string extension, bool audio)
    {
        Assert.True(MediaFileTypes.Supports(extension));
        Assert.Equal(audio, MediaFileTypes.IsAudio(extension));
    }
    [Theory]
    [InlineData(".flac")] [InlineData(".exe")]
    public void OtherFormatsRetainExternalFallback(string extension) => Assert.False(MediaFileTypes.Supports(extension));

    [Fact]
    public async Task OpensPausedAndSupportsToggleStopAndDuration()
    {
        var backend = new Session();
        using var controller = new MediaPlaybackController(backend);
        controller.Toggle();
        Assert.Equal(0, backend.PlayCalls);
        await controller.OpenAsync("file", CancellationToken.None);
        Assert.True(controller.State.Ready);
        Assert.False(controller.State.Playing);
        Assert.Equal(TimeSpan.FromSeconds(60), controller.State.Duration);
        controller.Toggle();
        Assert.True(controller.State.Playing);
        controller.Toggle();
        Assert.False(controller.State.Playing);
        controller.Seek(20);
        controller.Stop();
        Assert.Equal(TimeSpan.Zero, backend.Position);
        Assert.False(controller.State.Playing);
    }

    [Fact]
    public async Task SeeksAndSettingsClampAndRejectNonfiniteInput()
    {
        var backend = new Session();
        using var controller = new MediaPlaybackController(backend);
        await controller.OpenAsync("file", CancellationToken.None);
        controller.Seek(90);
        Assert.Equal(TimeSpan.FromSeconds(60), backend.Position);
        controller.Seek(-5);
        Assert.Equal(TimeSpan.Zero, backend.Position);
        controller.Seek(double.NaN);
        Assert.Equal(TimeSpan.Zero, backend.Position);
        controller.SetVolume(2); Assert.Equal(1, backend.Volume);
        controller.SetVolume(double.NaN); Assert.Equal(1, backend.Volume);
    }

    [Fact]
    public async Task MuteAndVolumeCarryAcrossMediaWithoutLosingUnmutedVolume()
    {
        var audio = new MediaAudioSettings();
        var firstBackend = new Session();
        using (var first = new MediaPlaybackController(firstBackend, audio))
        {
            await first.OpenAsync("first", CancellationToken.None);
            first.SetVolume(0.7);
            first.ToggleMute();
            Assert.True(first.State.Muted);
            Assert.True(firstBackend.IsMuted);
            Assert.Equal(0.7, firstBackend.Volume);
        }
        var secondBackend = new Session();
        using var second = new MediaPlaybackController(secondBackend, audio);
        Assert.True(secondBackend.IsMuted); // Applied before opening/playing another file.
        Assert.Equal(0.7, secondBackend.Volume);
        await second.OpenAsync("second", CancellationToken.None);
        second.SetVolume(0.4);
        Assert.True(second.State.Muted);
        second.ToggleMute();
        Assert.False(second.State.Muted);
        Assert.Equal(0.4, secondBackend.Volume);
    }

    [Fact]
    public async Task FiveSecondJumpsClampAtEdgesAndPreservePlaybackState()
    {
        var backend = new Session();
        using var controller = new MediaPlaybackController(backend);
        controller.Jump(5);
        Assert.Equal(TimeSpan.Zero, backend.Position);
        await controller.OpenAsync("file", CancellationToken.None);
        controller.Seek(58);
        controller.Toggle();
        controller.Jump(5);
        Assert.Equal(TimeSpan.FromSeconds(60), backend.Position);
        Assert.True(controller.State.Playing);
        controller.Jump(-5);
        Assert.Equal(TimeSpan.FromSeconds(55), backend.Position);
        controller.Seek(2);
        controller.Jump(-5);
        Assert.Equal(TimeSpan.Zero, backend.Position);
        controller.Jump(double.NaN);
        Assert.Equal(TimeSpan.Zero, backend.Position);
    }

    [Fact]
    public async Task UnknownDurationDisablesSeeking()
    {
        var backend = new Session { Duration = null };
        using var controller = new MediaPlaybackController(backend);
        await controller.OpenAsync("file", CancellationToken.None);
        Assert.False(controller.State.CanSeek);
        controller.Seek(10);
        Assert.Equal(TimeSpan.Zero, backend.Position);
    }

    [Fact]
    public async Task EndedPlaybackCanRestartFromBeginning()
    {
        var backend = new Session();
        using var controller = new MediaPlaybackController(backend);
        await controller.OpenAsync("file", CancellationToken.None);
        controller.Toggle();
        backend.Position = backend.Duration!.Value;
        backend.RaiseEnded();
        Assert.False(controller.State.Playing);
        controller.Toggle();
        Assert.True(controller.State.Playing);
        Assert.Equal(TimeSpan.Zero, backend.Position);
    }

    [Fact]
    public async Task FailedAndDisposedSessionsCannotKeepPlayingOrPublishLateEvents()
    {
        var backend = new Session();
        var controller = new MediaPlaybackController(backend);
        var failures = new List<string>();
        controller.Failed += failures.Add;
        await controller.OpenAsync("file", CancellationToken.None);
        controller.Toggle();
        backend.RaiseFailed();
        Assert.False(controller.State.Ready);
        Assert.False(controller.State.Playing);
        Assert.Single(failures);
        var calls = backend.PlayCalls;
        controller.Toggle(); Assert.Equal(calls, backend.PlayCalls);
        controller.Dispose(); controller.Dispose();
        backend.RaiseFailed(); backend.RaiseChanged();
        Assert.Single(failures);
        Assert.Equal(1, backend.Disposals);
        Assert.Equal(MediaPlaybackState.Empty, controller.State);
    }

    [Fact]
    public async Task DisposingPendingOpenRejectsLateReadyState()
    {
        var completion = new TaskCompletionSource();
        var backend = new Session { Open = completion.Task };
        var controller = new MediaPlaybackController(backend);
        var pending = controller.OpenAsync("file", CancellationToken.None);
        controller.Dispose();
        completion.SetResult();
        await pending;
        Assert.False(controller.State.Ready);
        Assert.Equal(0, backend.PauseCalls);
        Assert.Equal(1, backend.Disposals);
    }

    [Fact]
    public async Task OpenFailureIsPropagatedForFallbackPresentation()
    {
        using var controller = new MediaPlaybackController(new Session { Open = Task.FromException(new IOException("codec unavailable")) });
        await Assert.ThrowsAsync<IOException>(() => controller.OpenAsync("file", CancellationToken.None));
        Assert.False(controller.State.Ready);
    }

    private sealed class Session : IMediaPlaybackSession
    {
        public event Action? Changed;
        public event Action? Ended;
        public event Action<string>? Failed;
        public Task Open { get; init; } = Task.CompletedTask;
        public TimeSpan? Duration { get; init; } = TimeSpan.FromSeconds(60);
        public TimeSpan Position { get; set; }
        public double Volume { get; set; } = 0.5;
        public bool IsMuted { get; set; }
        public int PlayCalls { get; private set; }
        public int PauseCalls { get; private set; }
        public int Disposals { get; private set; }
        public Task OpenAsync(string path, CancellationToken token) => Open;
        public void Play() => PlayCalls++;
        public void Pause() => PauseCalls++;
        public void Seek(TimeSpan position) => Position = position;
        public void Dispose() => Disposals++;
        public void RaiseEnded() => Ended?.Invoke();
        public void RaiseFailed() => Failed?.Invoke("codec unavailable");
        public void RaiseChanged() => Changed?.Invoke();
    }
}
