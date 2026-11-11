namespace MediFiler3.Application.Folders;

// Notifications are hints. Overflow deliberately forgets individual paths and requests a rescan.
public sealed class FolderChangeBuffer(IReadOnlyList<string> roots)
{
    private readonly object _sync = new();
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _rescans = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reconciles = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _first;
    private DateTimeOffset _last;
    private bool _hierarchy;
    private string? _status;

    public void Add(string path, bool hierarchy, DateTimeOffset now, bool overflow = false, string? status = null)
    {
        lock (_sync)
        {
            _first ??= now;
            _last = now;
            _hierarchy |= hierarchy;
            _status = status ?? _status;
            if (overflow || _paths.Count >= 1024 && !_paths.Contains(path))
            {
                _paths.Clear(); _rescans.UnionWith(roots); _reconciles.Clear(); _hierarchy = true;
            }
            else if (_rescans.Count == 0) _paths.Add(path);
        }
    }

    // Starting healthy coverage only requests metadata reconciliation, not invalidation.
    public void ReconcileRoot(string root, DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_rescans.Contains(root)) return;
            _first ??= now; _last = now; _hierarchy = true; _reconciles.Add(root);
        }
    }

    public FolderChangeBatch? Take(DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_first is null || now - _last < TimeSpan.FromMilliseconds(500) && now - _first.Value < TimeSpan.FromSeconds(2)) return null;
            var batch = new FolderChangeBatch(_paths.ToArray(), _rescans.ToArray(), _hierarchy, _status, _reconciles.ToArray());
            _paths.Clear(); _rescans.Clear(); _reconciles.Clear(); _first = null; _hierarchy = false; _status = null;
            return batch;
        }
    }
}
