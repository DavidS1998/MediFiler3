using System.IO;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MediFiler3.Application.Files;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;
using MediFiler3.Application.Persistence;
using Microsoft.UI;
using Microsoft.UI.Content;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;
using Windows.Storage.Streams;
using Xaml = Microsoft.UI.Xaml;

namespace MediFiler3.Interop;

/// <summary>Native virtualized grid. Only realized containers own images or thumbnail requests.</summary>
public sealed partial class WinUiGalleryIsland : HwndHost
{
    private readonly IThumbnailService _thumbnails;
    private readonly GallerySelectionState _selection;
    private readonly GalleryHoverSelection _hoverSelection;
    private TextBlock? _selectionStatus;
    private FileEntry? _revealTarget;
    private bool _regionRefreshQueued;

    public WinUiGalleryIsland(IThumbnailService thumbnails, GallerySelectionState? selection = null)
    {
        _thumbnails = thumbnails;
        _selection = selection ?? new GallerySelectionState();
        _hoverSelection = new GalleryHoverSelection(_selection);
        Loaded += (_, _) => QueueRegionRefresh();
        IsVisibleChanged += (_, _) => { if (IsVisible) QueueRegionRefresh(); };
    }
    private const int DecodedEntryLimit = 512;
    private const long DecodedByteLimit = 128L * 1024 * 1024;
    private DesktopWindowXamlSource? _source;
    private GridView? _grid;
    private TextBlock? _empty;
    private readonly GalleryFiles _files = [];
    private FileEntry? _current;
    private readonly Dictionary<SelectorItem, Tile> _tiles = [];
    private readonly SemaphoreSlim _workers = new(3);
    private sealed record Decoded(FileEntry File, Xaml.Media.Imaging.BitmapImage Bitmap, long Bytes);
    private readonly LinkedList<Decoded> _decoded = new();
    private readonly Dictionary<FileEntry, LinkedListNode<Decoded>> _decodedIndex = [];
    private long _decodedBytes;
    private double _tileWidth = 150, _tileHeight = 150;
    private bool _updating;
    private bool _active = true;
    private long _activityVersion;
    private long _revealVersion;
    private bool _revealPending, _revealQueued;
    private Grid? _hoveredRoot;
    private double _treeInset = 26;
    private nint _handle;
    private Rect[] _exclusions = [];
    private string? _regionKey;
    private FileEntry[]? _pendingFiles;
    private FileEntry? _pendingCurrent;
    private bool _filesUpdateQueued;
    public int RealizedCount => _tiles.Count;
    public event Action<FileEntry>? FileSelected;
    public event Action<FileEntry>? OpenRequested;
    public event Action? RenameRequested;
    public event Action? DeleteRequested;
    public event Action? UndoRequested;
    public event Action? RedoRequested;
    public event Action? RefreshRequested;

    private sealed record Tile(FileEntry File, Grid Root, Image Image, TextBlock Placeholder, CancellationTokenSource Stop);

    private sealed class GalleryFiles : ObservableCollection<FileEntry>
    {
        public void ReplaceAll(IEnumerable<FileEntry> files)
        {
            CheckReentrancy();
            Items.Clear();
            foreach (var file in files) Items.Add(file);
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        }
    }

    public void SetFiles(IEnumerable<FileEntry> files, FileEntry? current)
    {
        var next = files.ToArray();
        if (_grid is null) { SetFilesCore(next, current); return; }
        _pendingFiles = next; _pendingCurrent = current;
        QueuePendingFiles();
    }

    private void QueuePendingFiles()
    {
        if (_pendingFiles is null || _filesUpdateQueued || !_active) return;
        _filesUpdateQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            _filesUpdateQueued = false;
            if (!_active || !IsLoaded || !IsVisible || _grid is not { IsLoaded: true } || _pendingFiles is not { } files) return;
            var current = _pendingCurrent;
            _pendingFiles = null; _pendingCurrent = null;
            SetFilesCore(files, current);
        }));
    }

    private void SetFilesCore(FileEntry[] next, FileEntry? current)
    {
        if (_grid?.ItemsSource is not null && _files.SequenceEqual(next)) { RevealCurrentFile(current, preserveSelection: true); return; }
        _selectionMenu?.Hide();
        _hoverSelection.Reset();
        _selectionContextVersion++;
        _current = current;
        _updating = true;
        try
        {
            // Keep the native source and unchanged items stable when a watcher changes one file.
            var paths = next.Select(f => f.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!_files.Any(file => paths.Contains(file.FullPath)))
            {
                // A wholly different folder has no surviving containers to retain. Publish
                // one reset rather than exposing thousands of transient indices to WinUI.
                CancelPendingReveal();
                _grid?.SelectedItems.Clear();
                if (_hoveredRoot is not null) SetTileHover(_hoveredRoot, false);
                _hoveredRoot = null;
                CancelTiles();
                _files.ReplaceAll(next);
            }
            else
            {
                for (var i = _files.Count - 1; i >= 0; i--)
                    if (!paths.Contains(_files[i].FullPath)) _files.RemoveAt(i);
                var existingPaths = _files.Select(f => f.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < next.Length; i++)
                {
                    if (i < _files.Count && string.Equals(_files[i].FullPath, next[i].FullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (_files[i] != next[i]) _files[i] = next[i];
                        continue;
                    }
                    var existing = -1;
                    // Usually an insertion. Only search for a move if that path is still in the source.
                    if (existingPaths.Contains(next[i].FullPath))
                        for (var j = i + 1; j < _files.Count; j++)
                            if (string.Equals(_files[j].FullPath, next[i].FullPath, StringComparison.OrdinalIgnoreCase)) { existing = j; break; }
                    if (existing >= 0) { _files.Move(existing, i); if (_files[i] != next[i]) _files[i] = next[i]; }
                    else _files.Insert(i, next[i]);
                }
            }
            // Input can arrive between session publication and this deferred native diff.
            // Prune any old-list selection accepted during that interval as well.
            _selection.Reconcile(_files);
            if (_grid is null) return;
            _grid.ItemsSource ??= _files;
            RevealCurrentFile(current, preserveSelection: true);
            if (_empty is not null) _empty.Visibility = _files.Count == 0 ? Xaml.Visibility.Visible : Xaml.Visibility.Collapsed;
        }
        finally { _updating = false; }
    }

    public void RevealCurrentFile(FileEntry? file, bool preserveSelection = false)
    {
        _current = file;
        if (_grid is null) return;
        var wasUpdating = _updating;
        _updating = true;
        try
        {
            var target = _files.FirstOrDefault(f => string.Equals(f.FullPath, file?.FullPath, StringComparison.OrdinalIgnoreCase));
            if (target is not null && !preserveSelection)
                _selection.Select(_files, target);
            ApplySelection();
            _revealTarget = target;
            _revealVersion++;
            _revealPending = target is not null;
            QueuePendingReveal();
        }
        finally { _updating = wasUpdating; }
    }

    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active; _activityVersion++;
        if (!active) PauseFileColors();
        _hoverSelection.Reset();
        if (!active) { _selectionMenu?.Hide(); FinishMoveDrag(); }
        if (!active) CancelPendingReveal();
        else QueueRegionRefresh();
        if (active) QueuePendingFiles();
        if (_hoveredRoot is not null) SetTileHover(_hoveredRoot, false);
        _hoveredRoot = null;
        foreach (var (container, tile) in _tiles.ToArray())
        {
            if (!active) tile.Stop.Cancel();
            else if (tile.Image.Source is null)
            {
                tile.Stop.Dispose();
                var resumed = tile with { Stop = new CancellationTokenSource() };
                _tiles[container] = resumed;
                _ = LoadThumbnailAsync(container, resumed, resumed.Stop.Token);
            }
        }
        if (active) ResumeFileColors();
    }

    public void SetTreeInset(double inset)
    {
        if (_treeInset == inset) return;
        _treeInset = inset;
        CenterTiles();
    }

    private void CenterTiles()
    {
        if (_grid is null || _grid.ActualWidth <= 0) return;
        var available = Math.Max(0, _grid.ActualWidth - _treeInset - 4);
        var columns = Math.Max(1, Math.Floor(available / (_tileWidth + 2)));
        var spare = Math.Max(0, available - columns * (_tileWidth + 2));
        var padding = new Xaml.Thickness(_treeInset + 2 + spare / 2, 2, 2 + spare / 2, 2);
        if (!_grid.Padding.Equals(padding)) _grid.Padding = padding;
    }

    public void InvalidateThumbnails(IEnumerable<string> changedPaths)
    {
        var paths = changedPaths.Select(Path.TrimEndingDirectorySeparator).ToArray();
        foreach (var entry in _decoded.ToArray())
            if (Affected(entry.File)) RemoveDecoded(_decodedIndex[entry.File]);
        foreach (var (container, tile) in _tiles.ToArray())
        {
            if (!Affected(tile.File)) continue;
            tile.Stop.Cancel();
            tile.Image.Source = null;
            tile.Placeholder.Visibility = Xaml.Visibility.Visible;
            if (!_active) continue; // Retained hidden tiles resume only when requested.
            tile.Stop.Dispose();
            var refreshed = tile with { Stop = new CancellationTokenSource() };
            _tiles[container] = refreshed;
            _ = LoadThumbnailAsync(container, refreshed, refreshed.Stop.Token);
        }
        bool Affected(FileEntry file) => paths.Any(path => string.Equals(file.FullPath, path, StringComparison.OrdinalIgnoreCase) ||
            file.FullPath.StartsWith(Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    public void SetTileSize(double width, double height)
    {
        _tileWidth = ApplicationSettings.SnapGalleryDimension(width);
        _tileHeight = ApplicationSettings.SnapGalleryDimension(height);
        if (_grid?.ItemsPanelRoot is ItemsWrapGrid panel)
        {
            panel.ItemWidth = _tileWidth + 2;
            panel.ItemHeight = _tileHeight + 2;
        }
        // Fixed-size cache requests avoid disk-cache proliferation while dragging either slider.
        foreach (var tile in _tiles.Values) SizeTile(tile);
        CenterTiles();
    }

    private void SizeTile(Tile tile)
    {
        tile.Root.Width = _tileWidth;
        tile.Root.Height = _tileHeight;
        tile.Root.Clip = new Xaml.Media.RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, _tileWidth, _tileHeight) };
        SetTileHover(tile.Root, ReferenceEquals(_hoveredRoot, tile.Root));
    }

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        _source = new DesktopWindowXamlSource();
        _source.Initialize(Win32Interop.GetWindowIdFromWindow(parent.Handle));
        _source.ShouldConstrainPopupsToWorkArea = true;
        var root = new Grid { RequestedTheme = Xaml.ElementTheme.Dark,
            Background = new Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black) };
        _grid = new GridView { SelectionMode = ListViewSelectionMode.Extended,
            Padding = new Xaml.Thickness(_treeInset + 2, 2, 2, 2), IsItemClickEnabled = false };
        _grid.ItemContainerStyle = (Xaml.Style)Xaml.Markup.XamlReader.Load(
            "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='GridViewItem'><Setter Property='Padding' Value='0'/><Setter Property='Margin' Value='1'/><Setter Property='MinWidth' Value='0'/><Setter Property='MinHeight' Value='0'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='VerticalContentAlignment' Value='Stretch'/></Style>");
        _grid.ItemTemplate = (Xaml.DataTemplate)Xaml.Markup.XamlReader.Load(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid Background="Black">
                <TextBlock HorizontalAlignment="Center" VerticalAlignment="Center"/>
                <Image Stretch="UniformToFill"/>
                <Border Height="40" VerticalAlignment="Bottom" IsHitTestVisible="False">
                  <Border.Background><LinearGradientBrush StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#00000000" Offset="0"/><GradientStop Color="#CC000000" Offset="1"/></LinearGradientBrush></Border.Background>
                  <TextBlock Margin="4,0,4,3" VerticalAlignment="Bottom" Foreground="White" TextTrimming="CharacterEllipsis" FontSize="12"/>
                </Border>
                <Grid Visibility="Collapsed" IsHitTestVisible="False">
                  <Border BorderThickness="5" BorderBrush="#FFFF1493"/>
                </Grid>
              </Grid>
            </DataTemplate>
            """);
        _grid.ItemsPanel = (Xaml.Controls.ItemsPanelTemplate)Xaml.Markup.XamlReader.Load(
            "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid Orientation='Horizontal' ItemWidth='152' ItemHeight='152' CacheLength='1'/></ItemsPanelTemplate>");
        ScrollViewer.SetHorizontalScrollBarVisibility(_grid, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_grid, ScrollBarVisibility.Auto);
        Xaml.Automation.AutomationProperties.SetName(_grid, "Gallery files");
        _grid.ContainerContentChanging += OnContainerChanging;
        _grid.Loaded += (_, _) =>
        {
            QueuePendingFiles();
            SetTileSize(_tileWidth, _tileHeight);
            RevealCurrentFile(_current, preserveSelection: true);
            QueueRegionRefresh();
        };
        _grid.LayoutUpdated += (_, _) => { QueuePendingFiles(); QueuePendingReveal(); };
        _grid.SelectionChanged += (_, args) =>
        {
            UpdateSelectionBorders();
            if (!_updating && _active)
            {
                CancelPendingReveal();
                var focused = FocusedFile() ?? args.AddedItems.OfType<FileEntry>().LastOrDefault() ?? args.RemovedItems.OfType<FileEntry>().LastOrDefault();
                var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
                _selection.ReplaceSelection(_files, _grid.SelectedItems.OfType<FileEntry>(), focused, preserveAnchor: shift);
            }
        };
        _grid.GotFocus += (_, _) =>
        {
            if (!_updating && _active && FocusedFile() is { } file) _selection.Focus(_files, file);
        };
        _grid.ContextRequested += OnGalleryContextRequested;
        _grid.SizeChanged += (_, _) => { CenterTiles(); QueuePendingReveal(); };
        _grid.AddHandler(Xaml.UIElement.PointerWheelChangedEvent, new Xaml.Input.PointerEventHandler((_, _) => CancelPendingReveal()), true);
        _grid.AddHandler(Xaml.UIElement.PointerPressedEvent, new Xaml.Input.PointerEventHandler((_, _) => CancelPendingReveal()), true);
        _grid.AddHandler(Xaml.UIElement.PointerMovedEvent, new Xaml.Input.PointerEventHandler(OnMoveDragPointerMoved), true);
        _grid.AddHandler(Xaml.UIElement.PointerReleasedEvent, new Xaml.Input.PointerEventHandler(OnMoveDragPointerReleased), true);
        _grid.AddHandler(Xaml.UIElement.PointerCanceledEvent, new Xaml.Input.PointerEventHandler(OnMoveDragPointerReleased), true);
        _grid.DoubleTapped += (_, args) =>
        {
            var source = args.OriginalSource as Xaml.DependencyObject;
            while (source is not null && source is not SelectorItem)
                source = Xaml.Media.VisualTreeHelper.GetParent(source);
            if (source is not SelectorItem item || !_tiles.TryGetValue(item, out var tile)) return;
            args.Handled = true;
            RequestOpen(tile.File);
        };
        _grid.AddHandler(Xaml.UIElement.KeyDownEvent, new Xaml.Input.KeyEventHandler((_, args) =>
        {
            CancelPendingReveal();
            var control = ControlHeld();
            PaintHoveredTile(AltHeld());
            if (HandleSelectionKey(args.Key, control)) args.Handled = true;
        }), true);
        _grid.AddHandler(Xaml.UIElement.KeyUpEvent, new Xaml.Input.KeyEventHandler((_, args) =>
        {
            if (args.Key is Windows.System.VirtualKey.Menu or Windows.System.VirtualKey.LeftMenu or Windows.System.VirtualKey.RightMenu)
                _hoverSelection.Reset();
        }), true);
        _grid.PointerExited += (_, args) =>
        {
            var point = args.GetCurrentPoint(_grid).Position;
            if (point.X < 0 || point.Y < 0 || point.X >= _grid.ActualWidth || point.Y >= _grid.ActualHeight)
                _hoverSelection.Reset();
        };
        _empty = new TextBlock { Text = "No files in this folder. Select a folder or add folders to begin.",
            HorizontalAlignment = Xaml.HorizontalAlignment.Center, VerticalAlignment = Xaml.VerticalAlignment.Center,
            IsHitTestVisible = false };
        root.Children.Add(_grid); root.Children.Add(_empty);
        root.Children.Add(CreateSelectionStatus());
        _source.Content = root;
        _handle = Win32Interop.GetWindowFromWindowId(_source.SiteBridge.WindowId);
        _grid.ItemsSource = _files;
        _selection.Changed += OnSelectionChanged;
        RevealCurrentFile(_current, preserveSelection: true);
        _empty.Visibility = _files.Count == 0 ? Xaml.Visibility.Visible : Xaml.Visibility.Collapsed;
        return new HandleRef(this, _handle);
    }

    private void Forward(Action action)
    {
        var version = _activityVersion;
        Dispatcher.BeginInvoke(new Action(() => { if (_source is not null && _active && version == _activityVersion) action(); }));
    }
    private void RequestOpen(FileEntry file) => Forward(() =>
    {
        var current = _files.FirstOrDefault(candidate => StringComparer.OrdinalIgnoreCase.Equals(candidate.FullPath, file.FullPath));
        if (current is not null) OpenRequested?.Invoke(current);
    });

    private void CancelPendingReveal() { _revealPending = false; _revealVersion++; }

    private void QueuePendingReveal()
    {
        if (!_revealPending || _revealQueued || !_active || _grid is null) return;
        _revealQueued = true;
        var grid = _grid; var activity = _activityVersion; var version = _revealVersion;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            _revealQueued = false;
            if (grid != _grid || activity != _activityVersion || version != _revealVersion)
            { QueuePendingReveal(); return; }
            if (!_revealPending || !_active || !IsLoaded || !IsVisible || !_grid.IsLoaded ||
                ActualWidth <= 0 || ActualHeight <= 0 || _grid.ActualWidth <= 0 || _grid.ActualHeight <= 0 || _grid.ItemsPanelRoot is null) return;
            if (_revealTarget is not { } selected || !_files.Contains(selected)) { CancelPendingReveal(); return; }
            if (_grid.ContainerFromItem(selected) is GridViewItem item && item.IsLoaded && item.ActualWidth > 0 && item.ActualHeight > 0)
            {
                var point = item.TransformToVisual(_grid).TransformPoint(new Windows.Foundation.Point());
                var width = Math.Max(1, _grid.ActualWidth - _grid.Padding.Left - _grid.Padding.Right);
                var height = Math.Max(1, _grid.ActualHeight - _grid.Padding.Top - _grid.Padding.Bottom);
                var visibleWidth = Math.Min(point.X + item.ActualWidth, _grid.ActualWidth - _grid.Padding.Right) - Math.Max(point.X, _grid.Padding.Left);
                var visibleHeight = Math.Min(point.Y + item.ActualHeight, _grid.ActualHeight - _grid.Padding.Bottom) - Math.Max(point.Y, _grid.Padding.Top);
                if (visibleWidth >= Math.Min(width, item.ActualWidth) - 2 && visibleHeight >= Math.Min(height, item.ActualHeight) - 2)
                { _revealPending = false; return; }
            }
            // Keep pending until the ensuing native layout actually realizes/reveals the item.
            _grid.ScrollIntoView(selected);
        }));
    }

    private void OnContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        var container = args.ItemContainer;
        if (!args.InRecycleQueue && args.Item is FileEntry same && _tiles.TryGetValue(container, out var retained) &&
            retained.File == same && ReferenceEquals(container.ContentTemplateRoot, retained.Root))
        {
            args.Handled = true;
            SetSelectionBorder(container, retained.Root);
            return;
        }
        if (_tiles.Remove(container, out var old)) { old.Stop.Cancel(); old.Stop.Dispose(); old.Image.Source = null; }
        if (args.InRecycleQueue) return;
        if (args.Item is not FileEntry file) return;
        if (container.ContentTemplateRoot is not Grid root)
        {
            if (args.Phase == 0) args.RegisterUpdateCallback(1, OnContainerChanging);
            return;
        }
        args.Handled = true;
        var placeholder = (TextBlock)root.Children[0];
        placeholder.Text = string.IsNullOrEmpty(file.Extension) ? "FILE" : file.Extension.ToUpperInvariant();
        placeholder.Visibility = Xaml.Visibility.Visible;
        var picture = (Image)root.Children[1]; picture.Source = null;
        ((TextBlock)((Border)root.Children[2]).Child).Text = file.Name;
        SetTileHover(root, false);
        if (_hoveredRoot == root) _hoveredRoot = null;
        if (root.Tag is null)
        {
            root.Tag = true;
            root.CanDrag = false; // Explicit threshold drag keeps ordinary GridView clicks native.
            root.DragStarting += OnTileMoveDragStarting;
            root.DropCompleted += (_, _) => FinishMoveDrag();
            root.AddHandler(Xaml.UIElement.PointerPressedEvent, new Xaml.Input.PointerEventHandler(OnTileDragPointerPressed), true);
            root.PointerEntered += OnTilePointerEntered;
            root.PointerExited += OnTilePointerExited;
            root.PointerMoved += OnTilePointerMoved;
        }
        ToolTipService.SetToolTip(root, file.FullPath);
        Xaml.Automation.AutomationProperties.SetName(container, file.Name);
        Xaml.Automation.AutomationProperties.SetHelpText(container, $"{file.FullPath}\n{file.Size:N0} bytes");
        var tile = new Tile(file, root, picture, placeholder, new CancellationTokenSource());
        _tiles[container] = tile;
        PaintFileColor(tile, null); QueueFileColor(container, tile);
        SizeTile(tile);
        SetSelectionBorder(container, root);
        if (_active) _ = LoadThumbnailAsync(container, tile, tile.Stop.Token);
    }

    private void OnTilePointerEntered(object sender, Xaml.Input.PointerRoutedEventArgs args)
    {
        if (sender is not Grid root || !_active || _startingMoveDrag || _dragCaption is not null) return;
        if (_hoveredRoot is not null && _hoveredRoot != root) SetTileHover(_hoveredRoot, false);
        _hoveredRoot = root;
        SetTileHover(root, true);
        PaintHoveredTile(AltHeld());
    }

    private void OnTilePointerExited(object sender, Xaml.Input.PointerRoutedEventArgs args)
    {
        if (sender is not Grid root) return;
        SetTileHover(root, false);
        if (_hoveredRoot == root) _hoveredRoot = null;
    }

    private static void SetTileHover(Grid root, bool hovered)
    {
        var picture = (Image)root.Children[1];
        picture.Stretch = hovered ? Xaml.Media.Stretch.Uniform : Xaml.Media.Stretch.UniformToFill;
        picture.HorizontalAlignment = Xaml.HorizontalAlignment.Center;
        picture.VerticalAlignment = Xaml.VerticalAlignment.Center;
        if (double.IsFinite(root.Width) && double.IsFinite(root.Height))
        {
            picture.Width = root.Width; picture.Height = root.Height;
            if (!hovered && picture.Source is Xaml.Media.Imaging.BitmapImage bitmap && bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
            {
                // Match the carousel: center-square framing, then fill the requested tile.
                // Enlarge the uncropped source around its center and clip at tile bounds,
                // retaining that same bitmap for the full-aspect hover presentation.
                var scale = Math.Max(root.Width, root.Height) / Math.Min(bitmap.PixelWidth, bitmap.PixelHeight);
                picture.Width = bitmap.PixelWidth * scale;
                picture.Height = bitmap.PixelHeight * scale;
            }
        }
        ((Border)root.Children[2]).Visibility = hovered ? Xaml.Visibility.Collapsed : Xaml.Visibility.Visible;
    }

    private async Task LoadThumbnailAsync(SelectorItem container, Tile tile, CancellationToken token)
    {
        var entered = false;
        try
        {
            if (_decodedIndex.TryGetValue(tile.File, out var cached))
            {
                _decoded.Remove(cached); _decoded.AddFirst(cached);
                tile.Image.Source = cached.Value.Bitmap;
                SetTileHover(tile.Root, ReferenceEquals(_hoveredRoot, tile.Root));
                tile.Placeholder.Visibility = Xaml.Visibility.Collapsed;
                return;
            }
            await _workers.WaitAsync(token); entered = true;
            token.ThrowIfCancellationRequested();
            var png = await _thumbnails.GetDefaultThumbnailPngAsync(tile.File, token);
            token.ThrowIfCancellationRequested();
            if (png is null) return;
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            { writer.WriteBytes(png); await writer.StoreAsync(); await writer.FlushAsync(); }
            stream.Seek(0);
            var bitmap = new Xaml.Media.Imaging.BitmapImage();
            await bitmap.SetSourceAsync(stream);
            if (token.IsCancellationRequested || !_tiles.TryGetValue(container, out var current) || !ReferenceEquals(tile, current)) return;
            tile.Image.Source = bitmap;
            SetTileHover(tile.Root, ReferenceEquals(_hoveredRoot, tile.Root));
            CacheDecoded(tile.File, bitmap);
            tile.Placeholder.Visibility = Xaml.Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException or ArgumentException or FormatException or NotSupportedException) { }
        finally { if (entered) _workers.Release(); }
    }

    private void CacheDecoded(FileEntry file, Xaml.Media.Imaging.BitmapImage bitmap)
    {
        if (_decodedIndex.TryGetValue(file, out var previous)) RemoveDecoded(previous);
        var entry = new Decoded(file, bitmap, Math.Max(1L, (long)bitmap.PixelWidth * bitmap.PixelHeight * 4));
        _decodedIndex[file] = _decoded.AddFirst(entry); _decodedBytes += entry.Bytes;
        while (_decoded.Count > DecodedEntryLimit || _decodedBytes > DecodedByteLimit) RemoveDecoded(_decoded.Last!);
    }

    private void RemoveDecoded(LinkedListNode<Decoded> node)
    {
        _decoded.Remove(node); _decodedIndex.Remove(node.Value.File); _decodedBytes -= node.Value.Bytes;
    }

    private void UpdateSelectionBorders()
    {
        foreach (var (container, tile) in _tiles) SetSelectionBorder(container, tile.Root);
    }

    private void SetSelectionBorder(SelectorItem container, Grid root)
    {
        if (root.Children.Count < 4) return;
        root.Children[3].Visibility = container.IsSelected ? Xaml.Visibility.Visible : Xaml.Visibility.Collapsed;
    }

    private void CancelTiles()
    {
        _hoveredRoot = null;
        foreach (var tile in _tiles.Values) { tile.Stop.Cancel(); tile.Stop.Dispose(); tile.Image.Source = null; }
        _tiles.Clear();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        if (_source is not null)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _source.SiteBridge.MoveAndResize(new RectInt32(0, 0, Math.Max(0, (int)Math.Round(result.Width * dpi.DpiScaleX)),
                Math.Max(0, (int)Math.Round(result.Height * dpi.DpiScaleY))));
            _regionKey = null;
            UpdateRegion();
            QueueRegionRefresh();
            QueuePendingReveal();
        }
        return result;
    }

    public void SetExclusions(IEnumerable<Rect> exclusions) { _exclusions = exclusions.ToArray(); UpdateRegion(); }
    private void QueueRegionRefresh()
    {
        if (_regionRefreshQueued) return;
        _regionRefreshQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            _regionRefreshQueued = false;
            if (!_active || !IsVisible || _handle == 0) return;
            // WPF/XAML-island reattachment may replace the HWND region even when our
            // exclusion rectangles are unchanged. Reapply after host layout finishes.
            _regionKey = null;
            UpdateRegion();
        }));
    }

    private void UpdateRegion()
    {
        if (_handle == 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(0, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(0, (int)Math.Round(ActualHeight * dpi.DpiScaleY));
        var rectangles = _exclusions.Select(rect => (L: (int)Math.Floor(rect.Left * dpi.DpiScaleX),
            T: (int)Math.Floor(rect.Top * dpi.DpiScaleY), R: (int)Math.Ceiling(rect.Right * dpi.DpiScaleX), B: (int)Math.Ceiling(rect.Bottom * dpi.DpiScaleY))).ToArray();
        var key = $"{width},{height};" + string.Join(";", rectangles);
        if (key == _regionKey) return;
        var region = CreateRectRgn(0, 0, width, height);
        if (region == 0) return;
        try
        {
            foreach (var rect in rectangles)
            {
                var cutout = CreateRectRgn(rect.L, rect.T, rect.R, rect.B);
                if (cutout == 0) continue;
                try { CombineRgn(region, region, cutout, 4); }
                finally { DeleteObject(cutout); }
            }
            if (SetWindowRgn(_handle, region, true) != 0) { region = 0; _regionKey = key; }
        }
        finally { if (region != 0) DeleteObject(region); }
    }
    protected override void DestroyWindowCore(HandleRef handle)
    {
        PauseFileColors();
        FinishMoveDrag();
        _selection.Changed -= OnSelectionChanged;
        _pendingFiles = null; _pendingCurrent = null;
        _hoverSelection.Reset();
        _selectionOutlines.Clear();
        _selectionMenu?.Hide();
        _selectionStatus = null;
        CancelPendingReveal();
        CancelTiles();
        if (_grid is not null) _grid.ContainerContentChanging -= OnContainerChanging;
        if (_source is not null) { _source.Content = null; _source.Dispose(); _source = null; }
        _grid = null; _empty = null; _handle = 0; _regionKey = null;
    }
    protected override bool TabIntoCore(System.Windows.Input.TraversalRequest request) => _source?.NavigateFocus(
        new XamlSourceFocusNavigationRequest(XamlSourceFocusNavigationReason.First)).WasFocusMoved == true;
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint destination, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
}
