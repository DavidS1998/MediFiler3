using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MediFiler3.Application.Folders;

public sealed class FolderTreeEntry : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public string Name { get; }
    public string Path { get; }
    public bool IsRoot { get; }
    public IReadOnlyList<FolderTreeEntry> Children { get; }
    public bool HasChildren => Children.Count > 0;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
    public string? Error { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FolderTreeEntry(string name, string path, IReadOnlyList<FolderTreeEntry>? children = null, bool isRoot = false, string? error = null)
    {
        Name = name;
        Path = path;
        Children = children ?? [];
        IsRoot = isRoot;
        Error = error;
        _isExpanded = Children.Count > 0;
    }

    private void SetProperty(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record FolderTreeLoadResult(IReadOnlyList<FolderTreeEntry> Roots, IReadOnlyList<string> Errors);

public interface IFolderTreeService
{
    Task<FolderTreeLoadResult> LoadRootsAsync(IEnumerable<string> folderPaths, CancellationToken cancellationToken);
}
