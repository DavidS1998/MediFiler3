using MediFiler3.Domain.Files;

namespace MediFiler3.Application.Navigation;

/// <summary>An Alt-held paint gesture chooses its direction once and visits each path once.</summary>
public sealed class GalleryHoverSelection(GallerySelectionState selection)
{
    private bool? _selecting;
    private readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);

    public void Reset() { _selecting = null; _visited.Clear(); }

    public void Visit(IReadOnlyList<FileEntry> files, FileEntry file, bool alt)
    {
        if (!alt) { Reset(); return; }
        if (_visited.Contains(file.FullPath)) return;
        if (!files.Any(candidate => StringComparer.OrdinalIgnoreCase.Equals(candidate.FullPath, file.FullPath))) return;
        var selected = selection.IsSelected(file);
        _selecting ??= !selected;
        if (!_visited.Add(file.FullPath)) return;
        selection.SetSelected(files, file, _selecting.Value);
    }
}
