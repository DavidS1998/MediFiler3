namespace MediFiler3.Interop;

public interface IWinUiDialogService
{
    Task<string?> PromptTextAsync(string title, string message, string initialValue = "", string primaryAction = "OK");
    Task<bool> ConfirmAsync(string title, string message, string confirmAction);
    Task<MediFiler3.Application.Operations.FileCollisionResolution> ResolveCollisionAsync(
        MediFiler3.Application.Operations.FileCollision collision);
}
