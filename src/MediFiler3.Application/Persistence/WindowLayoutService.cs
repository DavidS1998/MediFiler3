namespace MediFiler3.Application.Persistence;

/// <summary>Debounced persistence of captured values; never waits for a UI dispatcher.</summary>
public sealed class WindowLayoutService(IWindowLayoutStore store)
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _writer = new(1, 1);
    private WindowLayout? _current;
    private CancellationTokenSource? _debounce;
    private Task _pending = Task.CompletedTask;
    private long _revision, _savedRevision;
    private bool _loadFailed;
    public WindowLayout? Current { get { lock (_sync) return _current; } }
    public bool LoadFailed { get { lock (_sync) return _loadFailed; } }
    public event Action<Exception>? PersistenceFailed;

    public async Task InitializeAsync(CancellationToken token = default)
    {
        try
        {
            var value = await store.LoadAsync(token).ConfigureAwait(false);
            value?.Validate();
            lock (_sync) _current = value;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            lock (_sync) _loadFailed = true;
            PersistenceFailed?.Invoke(exception);
        } // Safe defaults; retain unreadable data until a real change.
    }

    public void Update(WindowLayout layout)
    {
        layout.Validate();
        lock (_sync)
        {
            if (layout == _current) return;
            _current = layout;
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
        await WriteCurrentAsync().ConfigureAwait(false);
    }

    private async Task WriteCurrentAsync()
    {
        await _writer.WaitAsync().ConfigureAwait(false);
        try
        {
            WindowLayout? value;
            long revision;
            lock (_sync)
            {
                revision = _revision;
                if (revision == _savedRevision) return;
                value = _current;
            }
            if (value is not null) await store.SaveAsync(value).ConfigureAwait(false);
            lock (_sync) _savedRevision = revision;
        }
        finally { _writer.Release(); }
    }
}
