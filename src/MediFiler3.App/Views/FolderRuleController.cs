using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using MediFiler3.Application.Folders;

namespace MediFiler3.Views;

/// <summary>Incremental tree observation, one bounded worker batch, UI-only publication.</summary>
internal sealed class FolderRuleController : IDisposable
{
    private readonly ObservableCollection<FolderTreeEntry> _roots;
    private readonly Dispatcher _dispatcher;
    private readonly Action<Exception> _failure;
    private readonly Dictionary<FolderTreeEntry, NotifyCollectionChangedEventHandler> _subscriptions = [];
    private readonly Dictionary<FolderTreeEntry, PropertyChangedEventHandler> _countSubscriptions = [];
    private readonly Queue<FolderTreeEntry> _queue = [];
    private readonly HashSet<FolderTreeEntry> _queued = [];
    private readonly HashSet<FolderTreeEntry> _immediateStructure = [];
    private readonly HashSet<FolderTreeEntry> _protectedExpansion = [];
    private FolderNameRule[] _rules = [];
    private CancellationTokenSource _stop = new();
    private bool _running, _disposed;
    private long _version;
    private bool _usesCounts;
    public Task Completion { get; private set; } = Task.CompletedTask;

    public FolderRuleController(ObservableCollection<FolderTreeEntry> roots, Dispatcher dispatcher, Action<Exception> failure)
    {
        _roots = roots; _dispatcher = dispatcher; _failure = failure;
        _roots.CollectionChanged += RootsChanged;
        foreach (var root in roots) Enqueue(root);
        Schedule();
    }

    public void SetRules(IReadOnlyList<FolderNameRule> rules, bool applyStructureImmediately = false)
    {
        FolderNameRule.ValidateAll(rules);
        if (_rules.SequenceEqual(rules) && !applyStructureImmediately) return;
        _immediateStructure.Clear();
        if (applyStructureImmediately)
        {
            _immediateStructure.UnionWith(_subscriptions.Keys); _immediateStructure.UnionWith(_roots);
        }
        UpdateProtectedExpansion();
        _rules = rules.ToArray(); _version++;
        _usesCounts = _rules.Any(rule => rule.Enabled && rule.Kind == FolderRuleKind.FileCount);
        _stop.Cancel(); _stop.Dispose(); _stop = new();
        foreach (var node in _subscriptions.Keys) Enqueue(node);
        foreach (var root in _roots) Enqueue(root);
        Schedule();
    }

    private void UpdateProtectedExpansion()
    {
        _protectedExpansion.Clear();
        foreach (var selected in _subscriptions.Keys.Where(node => node.IsSelected))
            for (var parent = selected.Parent; parent is not null; parent = parent.Parent) _protectedExpansion.Add(parent);
    }
    private void Enqueue(FolderTreeEntry node) { if (_queued.Add(node)) _queue.Enqueue(node); }
    private void Changed(NotifyCollectionChangedEventArgs args)
    {
        if (_disposed) return;
        if (args.Action == NotifyCollectionChangedAction.Reset)
            foreach (var node in _subscriptions.Keys) Enqueue(node);
        if (args.OldItems is not null) foreach (FolderTreeEntry node in args.OldItems) Enqueue(node);
        if (args.NewItems is not null) foreach (FolderTreeEntry node in args.NewItems) Enqueue(node);
        Schedule();
    }
    private void RootsChanged(object? sender, NotifyCollectionChangedEventArgs args) => Changed(args);
    private bool IsAttached(FolderTreeEntry node)
    {
        while (node.Parent is { } parent)
        {
            if (!parent.ContainsChild(node)) return false;
            node = parent;
        }
        return _roots.Contains(node);
    }
    private void Schedule()
    {
        if (_running || _disposed) return;
        _running = true;
        Completion = _dispatcher.InvokeAsync(RunAsync, DispatcherPriority.Background).Task.Unwrap();
    }
    private async Task RunAsync()
    {
        try
        {
            while (!_disposed && _queue.Count > 0)
            {
                var batch = new List<(FolderTreeEntry Node, string Name, long? Count)>();
                for (var i = 0; i < 32 && _queue.TryDequeue(out var node); i++)
                {
                    _queued.Remove(node);
                    if (!IsAttached(node))
                    {
                        if (_subscriptions.Remove(node, out var handler)) ((INotifyCollectionChanged)node.Children).CollectionChanged -= handler;
                        if (_countSubscriptions.Remove(node, out var countHandler)) node.PropertyChanged -= countHandler;
                        foreach (var child in node.Children) Enqueue(child);
                        continue;
                    }
                    if (!_subscriptions.ContainsKey(node))
                    {
                        NotifyCollectionChangedEventHandler handler = (_, args) => Changed(args);
                        _subscriptions.Add(node, handler);
                        ((INotifyCollectionChanged)node.Children).CollectionChanged += handler;
                        var observedNode = node;
                        PropertyChangedEventHandler countHandler = (_, args) =>
                        {
                            if (!_disposed && args.PropertyName == nameof(FolderTreeEntry.IsSelected)) UpdateProtectedExpansion();
                            if (!_disposed && _usesCounts && args.PropertyName == nameof(FolderTreeEntry.DirectFileCount)) { Enqueue(observedNode); Schedule(); }
                        };
                        _countSubscriptions.Add(node, countHandler); node.PropertyChanged += countHandler;
                    }
                    foreach (var child in node.Children) if (!_subscriptions.ContainsKey(child)) Enqueue(child);
                    batch.Add((node, node.Name, node.DirectFileCount));
                }
                var version = _version; var token = _stop.Token; var rules = _rules;
                try
                {
                    var matches = await Task.Run(() => batch.Select(item =>
                    {
                        token.ThrowIfCancellationRequested();
                        return (item.Node, item.Count, Actions: FolderNameRule.ResolveActions(item.Name, item.Count, item.Node.IsRoot, rules, token));
                    }).ToArray(), token);
                    if (!_disposed && version == _version && !token.IsCancellationRequested)
                        foreach (var match in matches) if (IsAttached(match.Node) && (!_usesCounts || match.Node.DirectFileCount == match.Count)) {
                            var immediate = _immediateStructure.Remove(match.Node);
                            match.Node.ApplyRuleActions(match.Actions, immediate && (match.Actions.ExpandedByDefault != false || !_protectedExpansion.Contains(match.Node)));
                        }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                await _dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
        }
        catch (Exception error) { if (!_disposed) _failure(error); }
        finally { _running = false; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _stop.Cancel(); _stop.Dispose();
        _roots.CollectionChanged -= RootsChanged;
        foreach (var (node, handler) in _subscriptions) ((INotifyCollectionChanged)node.Children).CollectionChanged -= handler;
        foreach (var (node, handler) in _countSubscriptions) node.PropertyChanged -= handler;
        _countSubscriptions.Clear();
        _subscriptions.Clear(); _immediateStructure.Clear(); _protectedExpansion.Clear(); _queue.Clear(); _queued.Clear();
    }
}
