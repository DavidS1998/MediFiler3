namespace MediFiler3.Application.Persistence;

/// <summary>Owns captured settings; background writes never depend on a UI dispatcher.</summary>
public sealed class ApplicationSettingsService(IApplicationSettingsStore store)
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _writer = new(1, 1);
    private CancellationTokenSource? _debounce;
    private Task _pending = Task.CompletedTask;
    private ApplicationSettings _current = new();
    private long _revision;
    private long _savedRevision;
    public ApplicationSettings Current { get { lock (_sync) return _current; } }
    public event Action<Exception>? PersistenceFailed;

    public async Task InitializeAsync(CancellationToken token = default)
    {
        try
        {
            var settings = await store.LoadAsync(token).ConfigureAwait(false);
            settings.Validate();
            lock (_sync) _current = settings;
        }
        catch (Exception exception) when (exception is System.IO.InvalidDataException or
            System.Text.Json.JsonException or ArgumentException)
        {
            // Invalid state uses safe defaults; retain the stored data until an actual user change.
            PersistenceFailed?.Invoke(exception);
        }
    }

    public void Update(Func<ApplicationSettings, ApplicationSettings> change)
    {
        lock (_sync)
        {
            var next = change(_current);
            next.Validate();
            if (next == _current) return;
            _current = next;
            _revision++;
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = new CancellationTokenSource();
            var token = _debounce.Token;
            _pending = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(250, token).ConfigureAwait(false);
                    await WriteCurrentAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception exception) { PersistenceFailed?.Invoke(exception); }
            });
        }
    }

    public async Task FlushAsync()
    {
        Task pending;
        lock (_sync) { _debounce?.Cancel(); pending = _pending; }
        await pending.ConfigureAwait(false);
        // Also retries a failed background save; serialized writes cannot overwrite newer state.
        await WriteCurrentAsync().ConfigureAwait(false);
    }

    private async Task WriteCurrentAsync()
    {
        await _writer.WaitAsync().ConfigureAwait(false);
        try
        {
            ApplicationSettings settings;
            long revision;
            lock (_sync)
            {
                revision = _revision;
                if (revision == _savedRevision) return;
                settings = _current;
            }
            await store.SaveAsync(settings).ConfigureAwait(false);
            lock (_sync) _savedRevision = revision;
        }
        finally { _writer.Release(); }
    }
}
