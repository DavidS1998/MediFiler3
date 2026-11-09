using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace MediFiler3.Application.Folders;

public sealed class FolderTreeEntry : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private long? _directFileCount;
    private long? _totalFileCount;
    private long _knownChildFileCount;
    private int _unknownChildCount;
    private readonly ObservableCollection<FolderTreeEntry> _children = [];
    public bool AreChildrenLoaded { get; private set; }

    public long? DirectFileCount => _directFileCount;
    public long? TotalFileCount => _totalFileCount;
    public long CountVersion { get; private set; }
    public string DisplayName => $"({DirectFileCount?.ToString() ?? "?"}/{TotalFileCount?.ToString() ?? "?"}) - {Name}";

    public string Name { get; }
    public string Path { get; }
    public bool IsRoot { get; }
    public ReadOnlyObservableCollection<FolderTreeEntry> Children { get; }
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

    public FolderTreeEntry(string name, string path, IReadOnlyList<FolderTreeEntry>? children = null, bool isRoot = false, string? error = null,
        long? directFileCount = null, bool areChildrenLoaded = true)
    {
        Name = name;
        Path = path;
        Children = new ReadOnlyObservableCollection<FolderTreeEntry>(_children);
        AreChildrenLoaded = areChildrenLoaded;
        IsRoot = isRoot;
        Error = error;
        if (directFileCount < 0) throw new ArgumentOutOfRangeException(nameof(directFileCount));
        _directFileCount = directFileCount;
        foreach (var child in children ?? []) AddChild(child);
        _totalFileCount = CalculateTotal();
        _isExpanded = Children.Count > 0;
    }

    public void AddChild(FolderTreeEntry child)
    {
        _children.Add(child);
        var previousTotal = child.TotalFileCount;
        if (previousTotal is { } known) _knownChildFileCount += known;
        else _unknownChildCount++;
        child.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(TotalFileCount)) return;
            if (previousTotal is { } previous) _knownChildFileCount -= previous;
            else _unknownChildCount--;
            previousTotal = child.TotalFileCount;
            if (previousTotal is { } current) _knownChildFileCount += current;
            else _unknownChildCount++;
            NotifyTotalChanged();
        };
        // Expand when the first children arrive, but preserve a later user collapse.
        if (Children.Count == 1) IsExpanded = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChildren)));
        NotifyTotalChanged();
    }

    public void CompleteChildrenLoad()
    {
        AreChildrenLoaded = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AreChildrenLoaded)));
        NotifyTotalChanged();
    }

    public void UpdateDirectFileCount(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        CountVersion++;
        if (_directFileCount == count) return;
        _directFileCount = count;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DirectFileCount)));
        NotifyTotalChanged();
    }

    private void NotifyTotalChanged()
    {
        var previousTotal = _totalFileCount;
        _totalFileCount = CalculateTotal();
        if (previousTotal != _totalFileCount)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalFileCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
    }

    private long? CalculateTotal() => !AreChildrenLoaded || DirectFileCount is null || _unknownChildCount > 0
        ? null
        : DirectFileCount + _knownChildFileCount;

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

public sealed record FolderFileCountResult(string Path, long? Count, IReadOnlyList<string> Errors);

public sealed record FolderChildrenResult(FolderTreeEntry Parent, IReadOnlyList<FolderTreeEntry> Children, IReadOnlyList<string> Errors);

public interface IFolderTreeService
{
    Task<FolderTreeLoadResult> LoadRootsAsync(IEnumerable<string> folderPaths, CancellationToken cancellationToken);
    IAsyncEnumerable<FolderFileCountResult> CountFilesAsync(IEnumerable<string> folderPaths, CancellationToken cancellationToken);
    IAsyncEnumerable<FolderChildrenResult> LoadHierarchyAsync(IEnumerable<FolderTreeEntry> roots, CancellationToken cancellationToken);
}
