using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace MediFiler3.Application.Folders;

public sealed class FolderTreeEntry : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _expansionTouched;
    private bool? _ruleExpandedByDefault;
    private FolderRuleColor? _ruleTextColor;
    private bool _isHiddenByRule;
    public bool IsHiddenByRule => _isHiddenByRule;
    public FolderRuleColor? RuleTextColor => _ruleTextColor;

    public void ApplyNameRule(FolderNameRule? rule)
    {
        var applyStructure = !IsRoot || rule?.ApplyToRootFolders == true;
        ApplyRuleActions(new(rule?.TextColor, applyStructure ? rule?.ExpandedByDefault : null,
            applyStructure && rule?.HideFolder == true));
    }
    public void ApplyRuleActions(FolderRuleActions actions, bool applyImmediately = false)
    {
        if (_ruleTextColor != actions.TextColor)
        {
            _ruleTextColor = actions.TextColor;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RuleTextColor)));
        }
        SetProperty(ref _isHiddenByRule, actions.Hidden, nameof(IsHiddenByRule));
        _ruleExpandedByDefault = actions.ExpandedByDefault;
        // An unspecified action leaves the current expansion alone. Deliberate
        // settings application overrides manual choices; discovery/count refresh does not.
        if (actions.ExpandedByDefault is { } expanded && (applyImmediately || !_expansionTouched))
            SetProperty(ref _isExpanded, expanded, nameof(IsExpanded));
    }
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
    public int Depth
    {
        get { var depth = 0; for (var parent = Parent; parent is not null; parent = parent.Parent) depth++; return depth; }
    }
    public ReadOnlyObservableCollection<FolderTreeEntry> Children { get; }
    public bool HasChildren => Children.Count > 0;
    private FolderTreeEntry? _lastVisibleChild;
    public FolderTreeEntry? LastVisibleChild
    {
        get => _lastVisibleChild;
        private set
        {
            if (ReferenceEquals(_lastVisibleChild, value)) return;
            _lastVisibleChild = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastVisibleChild)));
        }
    }
    public bool ContainsChild(FolderTreeEntry child) => _childHandlers.ContainsKey(child);
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return; // Two-way binding echoes are not user input.
            _expansionTouched = true;
            SetProperty(ref _isExpanded, value);
        }
    }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            // A default must not collapse the route to the currently selected folder:
            // WPF could otherwise select an ancestor and navigate away from its content.
            if (value && !_isSelected)
                for (var parent = Parent; parent is not null; parent = parent.Parent) parent._expansionTouched = true;
            SetProperty(ref _isSelected, value);
        }
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

    public void CollapseDirectChildren()
    {
        foreach (var child in Children) { child._expansionTouched = true; child.IsExpanded = false; }
    }

    public void AddChild(FolderTreeEntry child)
    {
        child.Parent = this;
        var descendants = new Stack<FolderTreeEntry>(); descendants.Push(child);
        while (descendants.TryPop(out var descendant))
        {
            descendant.PropertyChanged?.Invoke(descendant, new PropertyChangedEventArgs(nameof(Depth)));
            foreach (var nested in descendant.Children) descendants.Push(nested);
        }
        _children.Add(child);
        if (!child.IsHiddenByRule) LastVisibleChild = child;
        var previousTotal = child.TotalFileCount;
        if (previousTotal is { } known) _knownChildFileCount += known;
        else _unknownChildCount++;
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(IsHiddenByRule) &&
                (!child.IsHiddenByRule || ReferenceEquals(child, LastVisibleChild)))
            {
                UpdateLastVisibleChild();
            }
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
        if (Children.Count == 1 && !_expansionTouched)
            SetProperty(ref _isExpanded, _ruleExpandedByDefault ?? true, nameof(IsExpanded));
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
            if (ReferenceEquals(old, LastVisibleChild)) UpdateLastVisibleChild();
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
            {
                _children.Move(_children.IndexOf(existingByPath[children[i].Path]), i);
                UpdateLastVisibleChild();
            }
            await YieldAsync();
        }
        IsExpanded = expanded;
        CompleteChildrenLoad();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChildren)));
    }

    private void UpdateLastVisibleChild()
    {
        for (var index = _children.Count - 1; index >= 0; index--)
        {
            if (_children[index].IsHiddenByRule) continue;
            LastVisibleChild = _children[index];
            return;
        }
        LastVisibleChild = null;
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
