using MediFiler3.Application.Files;
using MediFiler3.Application.Folders;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Xaml = Microsoft.UI.Xaml;

namespace MediFiler3.Interop;

public sealed partial class WinUiGalleryIsland
{
    private FileNameColorRule[] _fileNameRules = [];
    private readonly Queue<(SelectorItem Container, Tile Tile)> _fileColorQueue = [];
    private readonly HashSet<Tile> _fileColorQueued = [];
    private long _fileColorVersion;
    private bool _fileColorsRunning;
    public Task FileColorsCompletion { get; private set; } = Task.CompletedTask;
    public void SetFileNameRules(IReadOnlyList<FileNameColorRule> rules)
    {
        FileNameColorRule.ValidateAll(rules);
        if (_fileNameRules.SequenceEqual(rules)) return;
        _fileNameRules = rules.ToArray(); _fileColorVersion++;
        _fileColorQueue.Clear(); _fileColorQueued.Clear();
        foreach (var pair in _tiles) QueueFileColor(pair.Key, pair.Value);
    }
    private static void PaintFileColor(Tile tile, FolderRuleColor? color)
    {
        var rgb = color ?? new FolderRuleColor(0, 0, 0);
        var brush = new Xaml.Media.LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
        brush.GradientStops.Add(new Xaml.Media.GradientStop { Offset = 0, Color = ColorHelper.FromArgb(0, rgb.Red, rgb.Green, rgb.Blue) });
        brush.GradientStops.Add(new Xaml.Media.GradientStop { Offset = 1, Color = ColorHelper.FromArgb(204, rgb.Red, rgb.Green, rgb.Blue) });
        ((Border)tile.Root.Children[2]).Background = brush;
    }
    private void QueueFileColor(SelectorItem container, Tile tile)
    {
        if (!_active) return;
        if (_fileNameRules.Length == 0) { PaintFileColor(tile, null); return; }
        if (_fileColorQueued.Add(tile)) _fileColorQueue.Enqueue((container, tile));
        if (_fileColorsRunning) return;
        _fileColorsRunning = true;
        FileColorsCompletion = Dispatcher.InvokeAsync(ApplyFileColorsAsync, System.Windows.Threading.DispatcherPriority.Background).Task.Unwrap();
    }
    private async Task ApplyFileColorsAsync()
    {
        try
        {
            while (_active && _source is not null && _fileColorQueue.Count > 0)
            {
                var batch = new List<(SelectorItem Container, Tile Tile)>();
                while (batch.Count < 32 && _fileColorQueue.TryDequeue(out var item))
                { _fileColorQueued.Remove(item.Tile); batch.Add(item); }
                var version = _fileColorVersion; var activity = _activityVersion; var rules = _fileNameRules;
                var colors = await Task.Run(() => batch.Select(item => FileNameColorRule.Match(item.Tile.File.Name, rules)).ToArray());
                if (_active && _source is not null && version == _fileColorVersion && activity == _activityVersion)
                    for (var index = 0; index < batch.Count; index++)
                        if (_tiles.TryGetValue(batch[index].Container, out var live) && ReferenceEquals(live, batch[index].Tile)) PaintFileColor(live, colors[index]);
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        finally { _fileColorsRunning = false; }
    }
    private void PauseFileColors()
    {
        _fileColorVersion++; _fileColorQueue.Clear(); _fileColorQueued.Clear();
    }
    private void ResumeFileColors()
    {
        foreach (var pair in _tiles) QueueFileColor(pair.Key, pair.Value);
    }
}
