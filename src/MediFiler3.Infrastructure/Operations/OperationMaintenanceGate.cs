namespace MediFiler3.Infrastructure.Operations;

internal static class OperationMaintenanceGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try { return await action().ConfigureAwait(false); }
        finally { Gate.Release(); }
    }
}
