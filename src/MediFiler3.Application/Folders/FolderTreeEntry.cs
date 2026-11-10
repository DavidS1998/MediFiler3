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
    private string? _publishedDisplayName;
    private long _knownChildFileCount;
    private int _unknownChildCount;
    private readonly ObservableCollection<FolderTreeEntry> _children = [];
    private readonly Dictionary<FolderTreeEntry, PropertyChangedEventHandler> _childHandlers = [];
    public bool AreChildrenLoaded { get; private set; }

    public long? DirectFileCount => _directFileCount;
    public long? TotalFileCount => _totalFileCount;
    public long CountVersion { get; private set; }
    public string DisplayName => $"({DirectFileCount?.ToString() ?? "?"}/{TotalFileCount?.ToString() ?? "?"}) - {Name}";

    public string Name { get; }
    public string Path { get; }
    public bool IsRoot { get; }
    public FolderTreeEntry? Parent { get; private set; }
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
        _publishedDisplayName = DisplayName;
    }

    public void AddChild(FolderTreeEntry child)
    {
        child.Parent = this;
        _children.Add(child);
        var previousTotal = child.TotalFileCount;
        if (previousTotal is { } known) _knownChildFileCount += known;
        else _unknownChildCount++;
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName != nameof(TotalFileCount)) return;
            if (previousTotal is { } previous) _knownChildFileCount -= previous;
            else _unknownChildCount--;
            previousTotal = child.TotalFileCount;
            if (previousTotal is { } current) _knownChildFileCount += current;
            else _unknownChildCount++;
            NotifyTotalChanged();
        };
        _childHandlers[child] = handler;
        child.PropertyChanged += handler;
        // Expand when the first children arrive, but preserve a later user collapse.
        if (Children.Count == 1) IsExpanded = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChildren)));
        NotifyTotalChanged();
    }

    public void ReconcileChildren(IReadOnlyList<FolderTreeEntry> children) =>
        ReconcileChildrenAsync(children).GetAwaiter().GetResult();

    public async Task ReconcileChildrenAsync(IReadOnlyList<FolderTreeEntry> children,
        Func<Task>? yieldBatch = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        // A rescan of an unchanged subtree must not publish known -> unknown -> known
        // totals through every ancestor. Large trees otherwise repeatedly repaint the root.
        if (AreChildrenLoaded && _children.Select(c => c.Path).SequenceEqual(children.Select(c => c.Path), StringComparer.OrdinalIgnoreCase)) return;
        var names = children.Select(c => c.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.SetEquals(_children.Select(c => c.Path)))
        {
            AreChildrenLoaded = false;
            NotifyTotalChanged();
        }
        var batch = 0;
        async Task YieldAsync()
        {
            token.ThrowIfCancellationRequested();
            if (++batch % 32 == 0 && yieldBatch is not null) await yieldBatch();
            token.ThrowIfCancellationRequested();
        }
        foreach (var old in _children.Where(c => !names.Contains(c.Path)).ToArray())
        {
            old.PropertyChanged -= _childHandlers[old];
            _childHandlers.Remove(old);
            _children.Remove(old);
            if (old.TotalFileCount is { } count) _knownChildFileCount -= count;
            else _unknownChildCount--;
            old.Parent = null;
            await YieldAsync();
        }
        var expanded = IsExpanded;
        var existingByPath = _children.ToDictionary(c => c.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var child in children)
        {
            if (!existingByPath.ContainsKey(child.Path)) { AddChild(child); existingByPath.Add(child.Path, child); }
            await YieldAsync();
        }
        for (var i = 0; i < children.Count; i++)
        {
            if (!string.Equals(_children[i].Path, children[i].Path, StringComparison.OrdinalIgnoreCase))
                _children.Move(_children.IndexOf(existingByPath[children[i].Path]), i);
            await YieldAsync();
        }
        IsExpanded = expanded;
        CompleteChildrenLoad();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChildren)));
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
        var displayName = DisplayName;
        if (_publishedDisplayName != displayName)
        {
            _publishedDisplayName = displayName;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
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
