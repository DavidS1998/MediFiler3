using System.Windows.Threading;
using MediFiler3.Application.Operations;

namespace MediFiler3.Interop;

internal sealed class BrowseCollisionResolver(Dispatcher dispatcher, IWinUiDialogService dialogs,
    Func<bool> contextIsCurrent) : IFileCollisionResolver
{
    public async Task<FileCollisionResolution> ResolveAsync(FileCollision collision, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var answer = await dispatcher.InvokeAsync(async () =>
        {
            if (!contextIsCurrent() || cancellationToken.IsCancellationRequested)
                return new FileCollisionResolution(FileCollisionChoice.Cancel);
            return await dialogs.ResolveCollisionAsync(collision);
        });
        var resolution = await answer;
        cancellationToken.ThrowIfCancellationRequested();
        return await dispatcher.InvokeAsync(() => contextIsCurrent() ? resolution : new FileCollisionResolution(FileCollisionChoice.Cancel));
    }
}
