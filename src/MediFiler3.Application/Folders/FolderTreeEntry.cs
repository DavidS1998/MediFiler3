namespace MediFiler3.Application.Folders;

public sealed class FolderTreeEntry(string name, string path, IReadOnlyList<FolderTreeEntry>? children = null, bool isRoot = false, string? error = null)
{
    public string Name { get; } = name;
    public string Path { get; } = path;
    public bool IsRoot { get; } = isRoot;
    public IReadOnlyList<FolderTreeEntry> Children { get; } = children ?? [];
    public bool HasChildren => Children.Count > 0;
    public bool IsExpanded { get; set; } = (children?.Count ?? 0) > 0;
    public string? Error { get; } = error;
}

public sealed record FolderTreeLoadResult(IReadOnlyList<FolderTreeEntry> Roots, IReadOnlyList<string> Errors);

public interface IFolderTreeService
{
    Task<FolderTreeLoadResult> LoadRootsAsync(IEnumerable<string> folderPaths, CancellationToken cancellationToken);
}
