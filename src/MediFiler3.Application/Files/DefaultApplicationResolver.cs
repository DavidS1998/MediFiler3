namespace MediFiler3.Application.Files;

public sealed class DefaultApplicationResolver(IDefaultApplicationService service) : IDisposable
{
    private CancellationTokenSource? _pending;
    private long _version;
    public DefaultApplicationInfo? Application { get; private set; }
    public event Action? Changed;

    public async Task ResolveAsync(string path)
    {
        Clear();
        var version = _version;
        var source = new CancellationTokenSource();
        _pending = source;
        var token = source.Token;
        try
        {
            var result = await service.LookupAsync(path, token);
            if (version != _version || token.IsCancellationRequested) return;
            Application = result;
            Changed?.Invoke();
        }
        catch (Exception) when (version != _version || token.IsCancellationRequested) { }
        catch (Exception)
        {
            Application = new(DefaultApplicationStatus.Unknown);
            Changed?.Invoke();
        }
        finally
        {
            if (ReferenceEquals(_pending, source)) _pending = null;
            source.Dispose();
        }
    }

    public void Clear()
    {
        _version++;
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
        Application = null;
        Changed?.Invoke();
    }
    public void Dispose() => Clear();
}
