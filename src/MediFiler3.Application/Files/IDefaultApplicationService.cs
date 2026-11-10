namespace MediFiler3.Application.Files;

public enum DefaultApplicationStatus { Available, None, Unknown }
public sealed record DefaultApplicationInfo(DefaultApplicationStatus Status, string? DisplayName = null, byte[]? IconPng = null);
public sealed record ExternalOpenState(bool Visible, bool Busy, DefaultApplicationInfo? Application)
{
    public string DefaultLabel => Application?.Status == DefaultApplicationStatus.None ? "No default application"
        : !string.IsNullOrWhiteSpace(Application?.DisplayName) ? $"Open in {Application.DisplayName}" : "Open in default application";
    public bool CanOpenDefault => Visible && !Busy && Application?.Status != DefaultApplicationStatus.None;
    public bool CanChooseApplication => Visible && !Busy;
}

public interface IDefaultApplicationService
{
    Task<DefaultApplicationInfo> LookupAsync(string filePath, CancellationToken cancellationToken);
}
