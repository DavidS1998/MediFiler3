using MediFiler3.Domain.Files;
using MediFiler3.Application.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace MediFiler3.Interop;

public sealed partial class WinUiGalleryIsland
{
    public event Func<IReadOnlyList<FileEntry>, string?>? MoveDragStarting;
    public event Action? MoveDragCompleted;
    private FileEntry[]? _pressedDragFiles;
    private Grid? _pressedDragRoot;
    private readonly GalleryDragGesture _dragGesture = new();
    private bool _startingMoveDrag;
    private string? _dragCaption;
    private long _dragPreviewVersion;
    private SoftwareBitmap? _dragPreviewBitmap;
    private const double DragPreviewOpacity = 0.8;
    private static readonly Windows.Foundation.Point DragPreviewAnchor = new(0, 0);

    private void ResetMoveDragCandidate()
    {
        _pressedDragFiles = null; _pressedDragRoot = null; _dragGesture.Reset();
    }

    private void OnTileDragPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (_startingMoveDrag) return;
        ResetMoveDragCandidate();
        if (!_active || _grid is null || sender is not Grid root || !args.GetCurrentPoint(root).Properties.IsLeftButtonPressed || AltHeld()) return;
        if (FileFromElement(root) is { } origin)
        {
            _pressedDragFiles = _selection.GetDraggedFiles(_files, origin).ToArray();
            _pressedDragRoot = root;
            var point = args.GetCurrentPoint(_grid).Position;
            _dragGesture.Begin(point.X, point.Y);
        }
        // Leave the press untouched so GridView owns click, Ctrl-click and Shift ranges.
    }

    private async void OnMoveDragPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (!_active || _grid is null || _startingMoveDrag || _pressedDragRoot is not { IsLoaded: true } root) return;
        if (FileFromElement(root) is not { } origin || _pressedDragFiles?.Contains(origin) != true) { ResetMoveDragCandidate(); return; }
        var point = args.GetCurrentPoint(_grid);
        if (!_dragGesture.TryStart(point.Position.X, point.Position.Y, point.Properties.IsLeftButtonPressed,
                System.Windows.SystemParameters.MinimumHorizontalDragDistance, System.Windows.SystemParameters.MinimumVerticalDragDistance)) return;
        _startingMoveDrag = true;
        args.Handled = true;
        try { await root.StartDragAsync(args.GetCurrentPoint(root)); }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException) { }
        finally { _startingMoveDrag = false; FinishMoveDrag(); }
    }

    private void OnMoveDragPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (!_startingMoveDrag) ResetMoveDragCandidate();
    }

    private async void OnTileMoveDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (!_startingMoveDrag) { args.Cancel = true; return; }
        var files = _pressedDragFiles?.ToArray();
        if (!PrepareMoveDrag(args.Data) || files is null) { args.Cancel = true; return; }
        args.AllowedOperations = DataPackageOperation.Move;
        var version = ++_dragPreviewVersion;
        var deferral = args.GetDeferral();
        try
        {
            var bitmap = await CreateMoveDragPreviewAsync(sender, files, version);
            if (bitmap is null) return;
            if (version != _dragPreviewVersion || !_active || _dragCaption is null) { bitmap.Dispose(); return; }
            _dragPreviewBitmap?.Dispose();
            _dragPreviewBitmap = bitmap;
            args.DragUI.SetContentFromSoftwareBitmap(bitmap, DragPreviewAnchor);
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // Keep the native default preview and the validated move gesture if capture fails.
        }
        finally { deferral.Complete(); }
    }

    private async Task<SoftwareBitmap?> CreateMoveDragPreviewAsync(UIElement origin, IReadOnlyList<FileEntry> files, long version)
    {
        if (origin is not FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } element) return null;
        // Capture only realized visuals. No source reads, thumbnail generation or offscreen realization.
        var roots = new List<UIElement> { origin };
        foreach (var file in files)
        {
            if (roots.Count == 3) break;
            if (_grid?.ContainerFromItem(file) is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem item &&
                _tiles.TryGetValue(item, out var tile) && tile.Root.IsLoaded && !roots.Contains(tile.Root)) roots.Add(tile.Root);
        }
        var scale = Math.Min(1, 192 / Math.Max(element.ActualWidth, element.ActualHeight));
        var width = Math.Max(1, (int)Math.Round(element.ActualWidth * scale));
        var height = Math.Max(1, (int)Math.Round(element.ActualHeight * scale));
        var offset = (roots.Count - 1) * 8;
        var canvasWidth = width + offset; var canvasHeight = height + offset;
        var pixels = new byte[canvasWidth * canvasHeight * 4];
        for (var layer = roots.Count - 1; layer >= 0; layer--)
        {
            if (version != _dragPreviewVersion || !_active || _dragCaption is null) return null;
            // A drag leaves hover-to-fit: capture the regular cropped tile, not letterboxing.
            if (roots[layer] is Grid tileRoot) { SetTileHover(tileRoot, false); tileRoot.UpdateLayout(); }
            var capture = new RenderTargetBitmap();
            await capture.RenderAsync(roots[layer], width, height);
            var source = (await capture.GetPixelsAsync()).ToArray();
            if (capture.PixelWidth <= 0 || capture.PixelHeight <= 0) continue;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var src = ((y * capture.PixelHeight / height) * capture.PixelWidth + x * capture.PixelWidth / width) * 4;
                    var dst = ((y + layer * 8) * canvasWidth + x + layer * 8) * 4;
                    var remaining = 255 - source[src + 3];
                    for (var channel = 0; channel < 4; channel++)
                        pixels[dst + channel] = (byte)Math.Min(255, source[src + channel] + (pixels[dst + channel] * remaining + 127) / 255);
                }
        }
        ApplyDragPreviewOpacity(pixels);
        return SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, canvasWidth, canvasHeight, BitmapAlphaMode.Premultiplied);
    }

    private static void ApplyDragPreviewOpacity(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)Math.Round(pixels[i] * DragPreviewOpacity);
    }

    private bool PrepareMoveDrag(DataPackage data)
    {
        var files = _pressedDragFiles;
        _pressedDragFiles = null;
        // No origin snapshot means this is not an authorized Gallery file gesture.
        if (!_active || files is not { Length: > 0 } || files.Any(file => !_files.Contains(file))) { return false; }
        var token = MoveDragStarting?.Invoke(files);
        if (token is null) { return false; }
        data.RequestedOperation = DataPackageOperation.Move;
        data.SetText(token);
        data.Properties.Title = $"Move {files.Length} {(files.Length == 1 ? "file" : "files")}";
        _dragCaption = $"Moving {files.Length} {(files.Length == 1 ? "file" : "files")} - drop on a folder";
        _hoverSelection.Reset();
        if (_hoveredRoot is not null) SetTileHover(_hoveredRoot, false);
        _hoveredRoot = null;
        CancelPendingReveal();
        ApplySelection();
        return true;
    }

    public void ClearMoveDragVisual()
    {
        _dragPreviewVersion++;
        _dragPreviewBitmap?.Dispose(); _dragPreviewBitmap = null;
        ResetMoveDragCandidate(); _dragCaption = null;
        ApplySelection();
    }

    private void FinishMoveDrag()
    {
        ClearMoveDragVisual();
        MoveDragCompleted?.Invoke();
    }
}
