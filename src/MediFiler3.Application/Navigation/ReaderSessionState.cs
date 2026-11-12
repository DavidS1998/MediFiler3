using MediFiler3.Domain.Files;

namespace MediFiler3.Application.Navigation;

public sealed record ReaderAnchor(string Path, double Fraction, double GapFraction = 0);

/// <summary>Image projection and lightweight geometry; never owns decoded image data.</summary>
public sealed class ReaderSessionState
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" };
    private readonly Dictionary<FileEntry, double> _ratios = [];
    private double[] _tops = [0];
    private double _width = 1, _viewportHeight = 1;
    private double[] _heights = [];
    public ReaderOptions Options { get; private set; } = new();
    public void SetOptions(ReaderOptions options)
    {
        options.Validate();
        Options = options; Arrange(_width, _viewportHeight);
    }
    private string? _folder;
    public IReadOnlyList<FileEntry> Pages { get; private set; } = [];
    public ReaderAnchor? Anchor { get; private set; }
    public double Extent => _tops[^1];
    public static bool Supports(FileEntry file) => Extensions.Contains(file.Extension);

    public void SetFiles(string? folder, IReadOnlyList<FileEntry> files, FileEntry? current, bool preferCurrent)
    {
        var sameFolder = StringComparer.OrdinalIgnoreCase.Equals(_folder, folder);
        _folder = folder;
        if (!sameFolder) { Anchor = null; _ratios.Clear(); }
        Pages = files.Where(Supports).ToArray();
        var members = Pages.ToHashSet();
        foreach (var key in _ratios.Keys.Where(key => !members.Contains(key)).ToArray()) _ratios.Remove(key);
        if (preferCurrent || Anchor is null || !Pages.Any(file => StringComparer.OrdinalIgnoreCase.Equals(file.FullPath, Anchor.Path)))
        {
            var index = current is null ? 0 : Math.Max(0, files.ToList().FindIndex(file => file == current));
            var page = files.Skip(index).FirstOrDefault(Supports) ?? files.Take(index).LastOrDefault(Supports) ?? Pages.FirstOrDefault();
            Anchor = page is null ? null : new(page.FullPath, 0);
        }
        Arrange(_width, _viewportHeight);
    }

    private double ColumnWidth => Options.Scaling == ReaderScalingMode.FitWidth ? _width * Options.WidthPercent / 100d : _width;
    public void Arrange(double width, double viewportHeight = 1)
    {
        _width = Math.Max(1, width); _viewportHeight = Math.Max(1, viewportHeight);
        _tops = new double[Pages.Count + 1]; _heights = new double[Pages.Count];
        for (var i = 0; i < Pages.Count; i++)
        {
            var height = ColumnWidth * _ratios.GetValueOrDefault(Pages[i], 1.5);
            _heights[i] = Math.Max(24, Options.Scaling == ReaderScalingMode.FitHeight ? Math.Min(height, _viewportHeight) : height);
            _tops[i + 1] = _tops[i] + _heights[i] + (i < Pages.Count - 1 ? Options.Spacing : 0);
        }
    }
    public double Width(int index) => Math.Min(ColumnWidth, Height(index) / _ratios.GetValueOrDefault(Pages[index], 1.5));
    public bool SetDimensions(FileEntry file, int width, int height)
    {
        if (width <= 0 || height <= 0 || !Pages.Contains(file)) return false;
        var ratio = (double)height / width;
        if (_ratios.GetValueOrDefault(file) == ratio) return false;
        _ratios[file] = ratio; Arrange(_width, _viewportHeight); return true;
    }
    public double Top(int index) => _tops[index];
    public double Height(int index) => _heights[index];
    public int IndexAt(double offset)
    {
        if (Pages.Count == 0) return -1;
        var index = Array.BinarySearch(_tops, Math.Max(0, offset));
        return Math.Clamp(index >= 0 ? index : ~index - 1, 0, Pages.Count - 1);
    }
    public ReaderAnchor? Capture(double offset)
    {
        var index = IndexAt(offset);
        Anchor = index < 0 ? null : new(Pages[index].FullPath, Math.Clamp((offset - Top(index)) / Height(index), 0, 1),
            Options.Spacing == 0 || index == Pages.Count - 1 ? 0 : Math.Clamp((offset - Top(index) - Height(index)) / Options.Spacing, 0, 1));
        return Anchor;
    }
    public double Offset(ReaderAnchor? anchor)
    {
        var index = anchor is null ? -1 : Pages.ToList().FindIndex(file => StringComparer.OrdinalIgnoreCase.Equals(file.FullPath, anchor.Path));
        return index < 0 ? 0 : Top(index) + Height(index) * Math.Clamp(anchor!.Fraction, 0, 1) + (index < Pages.Count - 1 ? Options.Spacing * Math.Clamp(anchor.GapFraction, 0, 1) : 0);
    }
    public bool SelectPage(int index)
    {
        if (index < 0 || index >= Pages.Count) return false;
        Anchor = new(Pages[index].FullPath, 0); return true;
    }
    public bool Navigate(ReaderNavigation command, double viewport, int currentIndex)
    {
        if (Pages.Count == 0 || !Enum.IsDefined(command)) return false;
        if (command is ReaderNavigation.PreviousImage or ReaderNavigation.NextImage)
            return SelectPage(Math.Clamp(currentIndex + (command == ReaderNavigation.PreviousImage ? -1 : 1), 0, Pages.Count - 1));
        var offset = Offset(Anchor);
        viewport = Math.Max(1, viewport);
        var next = command switch
        {
            ReaderNavigation.LineUp => offset - 48,
            ReaderNavigation.LineDown => offset + 48,
            ReaderNavigation.ViewUp => offset - viewport * .9,
            ReaderNavigation.ViewDown => offset + viewport * .9,
            ReaderNavigation.Start => 0,
            ReaderNavigation.End => Math.Max(0, Extent - viewport),
            _ => offset
        };
        Capture(Math.Clamp(next, 0, Math.Max(0, Extent - viewport))); return true;
    }

    public IReadOnlyList<int> Window(double offset, double viewport, int? preload = null)
    {
        if (Pages.Count == 0) return [];
        var start = Math.Max(0, IndexAt(offset) - 1);
        var end = Math.Min(Pages.Count - 1, IndexAt(offset + Math.Max(1, viewport)) + Math.Clamp(preload ?? Options.Preload, 0, 6));
        return Enumerable.Range(start, end - start + 1).ToArray();
    }
}
