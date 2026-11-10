namespace MediFiler3.Application.Files;

public enum ExternalFileAction { DefaultApplication, ChooseApplication }
public enum ExternalFileOutcome { Opened, Cancelled, MissingFile, NoAssociation, Failed }
public sealed record ExternalFileResult(ExternalFileOutcome Outcome, string Message);

public interface IExternalFileService
{
    Task<ExternalFileResult> OpenAsync(string path, ExternalFileAction action, nint ownerWindow,
        CancellationToken cancellationToken = default);
}
