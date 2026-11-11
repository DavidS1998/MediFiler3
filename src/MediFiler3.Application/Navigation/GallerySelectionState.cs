using MediFiler3.Domain.Files;

namespace MediFiler3.Application.Navigation;

/// <summary>Mode-local selection. Call on the session's owning thread, like navigation state.</summary>
public sealed class GallerySelectionState
{
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    public string? FocusedPath { get; private set; }
    public string? AnchorPath { get; private set; }
    public int Count => _selected.Count;
    public event Action? Changed;

    public bool IsSelected(FileEntry file) => _selected.Contains(file.FullPath);

    // Snapshots follow the current shared sort order, never click or hash-set order.
    public IReadOnlyList<FileEntry> GetSelectedFiles(IReadOnlyList<FileEntry> files) =>
        files.Where(IsSelected).ToArray();

    public void Select(IReadOnlyList<FileEntry> files, FileEntry file, bool toggle = false, bool range = false)
    {
        var target = IndexOf(files, file.FullPath);
        if (target < 0) return;
        var anchor = IndexOf(files, AnchorPath);
        if (range && anchor >= 0)
        {
            if (!toggle) _selected.Clear();
            for (var i = Math.Min(anchor, target); i <= Math.Max(anchor, target); i++)
                _selected.Add(files[i].FullPath);
        }
        else
        {
            if (!toggle) _selected.Clear();
            if (!toggle || !_selected.Remove(file.FullPath)) _selected.Add(file.FullPath);
            AnchorPath = files[target].FullPath;
        }
        FocusedPath = files[target].FullPath;
        Changed?.Invoke();
    }

    /// <summary>Ctrl+arrow moves focus without changing the selected set or range anchor.</summary>
    public void Focus(IReadOnlyList<FileEntry> files, FileEntry file)
    {
        var index = IndexOf(files, file.FullPath);
        if (index < 0) return;
        FocusedPath = files[index].FullPath;
        Changed?.Invoke();
    }

    public void SelectAll(IReadOnlyList<FileEntry> files)
    {
        _selected.Clear();
        foreach (var file in files) _selected.Add(file.FullPath);
        FocusedPath ??= files.FirstOrDefault()?.FullPath;
        AnchorPath ??= FocusedPath;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (Count == 0 && FocusedPath is null && AnchorPath is null) return;
        _selected.Clear();
        FocusedPath = AnchorPath = null;
        Changed?.Invoke();
    }

    public void Reconcile(IReadOnlyList<FileEntry> files)
    {
        if (Count == 0 && FocusedPath is null && AnchorPath is null) return;
        var paths = files.Select(file => file.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = _selected.RemoveWhere(path => !paths.Contains(path)) > 0;
        if (FocusedPath is not null && !paths.Contains(FocusedPath)) { FocusedPath = null; changed = true; }
        if (AnchorPath is not null && !paths.Contains(AnchorPath)) { AnchorPath = null; changed = true; }
        if (changed) Changed?.Invoke();
    }

    private static int IndexOf(IReadOnlyList<FileEntry> files, string? path)
    {
        if (path is null) return -1;
        for (var i = 0; i < files.Count; i++)
            if (StringComparer.OrdinalIgnoreCase.Equals(files[i].FullPath, path)) return i;
        return -1;
    }
}
