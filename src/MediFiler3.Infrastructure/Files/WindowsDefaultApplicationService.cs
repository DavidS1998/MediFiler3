using System.IO;
using MediFiler3.Application.Files;

namespace MediFiler3.Infrastructure.Files;

public interface IDefaultApplicationLookup
{
    DefaultApplicationInfo Query(string extension);
}

public sealed class WindowsDefaultApplicationService(IDefaultApplicationLookup lookup) : IDefaultApplicationService
{
    private readonly SemaphoreSlim _gate = new(1);
    public async Task<DefaultApplicationInfo> LookupAsync(string filePath, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await LookupOnStaAsync(filePath, cancellationToken); }
        finally { _gate.Release(); }
    }

    private Task<DefaultApplicationInfo> LookupOnStaAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<DefaultApplicationInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var extension = Path.GetExtension(filePath);
                var result = string.IsNullOrEmpty(extension) ? new(DefaultApplicationStatus.Unknown) : lookup.Query(extension);
                cancellationToken.ThrowIfCancellationRequested();
                completion.SetResult(result);
            }
            catch (OperationCanceledException) { completion.SetCanceled(cancellationToken); }
            catch (Exception) { completion.SetResult(new(DefaultApplicationStatus.Unknown)); }
        }) { IsBackground = true, Name = "MediFiler3 default application lookup" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }
}
