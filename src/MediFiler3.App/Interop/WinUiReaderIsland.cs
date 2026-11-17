using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MediFiler3.Application.Files;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Xaml = Microsoft.UI.Xaml;

namespace MediFiler3.Interop;

/// <summary>Native virtualized page surface. The full sequence owns geometry, not image controls.</summary>
public sealed partial class WinUiReaderIsland(ReaderSessionState session, IReaderImageService images) : HwndHost
{
    public const long CacheLimit = 128L * 1024 * 1024;
    private DesktopWindowXamlSource? _source;
    private ScrollViewer? _scroll;
    private Canvas? _canvas;
    private TextBlock? _empty, _indicator;
    private bool _active, _queued, _restore, _updating;
    private double _inset, _width, _viewportHeight;
    private Grid? _indicatorHost;
    private readonly List<TextBlock> _indicatorOutlines = [];
    private readonly SemaphoreSlim _workers = new(2);
    private sealed record Row(FileEntry File, Border Root, Image Image, TextBlock Status, StackPanel ErrorActions, CancellationTokenSource Stop, int Width);
    private sealed record Cached(FileEntry File, int Width, WriteableBitmap Image, long Bytes, int OriginalWidth, int OriginalHeight);
    private readonly Dictionary<FileEntry, Row> _rows = [];
    private readonly LinkedList<Cached> _cache = [];
    private long _cacheBytes;
    private Rect[] _exclusions = [];
    private string? _regionKey;
    private nint _handle;
    private FileEntry? _reported;
    public event Action<FileEntry>? CurrentPageChanged;
    public int RealizedCount => _rows.Count;
    public long DecodedBytes => _cacheBytes;
    public bool IsActive => _active;

    public void SetOptions(ReaderOptions options)
    {
        options.Validate();
        if (session.Options == options) return;
        if (_active && _scroll is not null && !_restore) session.Capture(_scroll.VerticalOffset);
        var layoutChanged = session.Options.Scaling != options.Scaling || session.Options.Spacing != options.Spacing ||
            options.Scaling == ReaderScalingMode.FitWidth && session.Options.WidthPercent != options.WidthPercent;
        session.SetOptions(options);
        if (_indicatorHost is not null) _indicatorHost.Visibility = options.ShowPageIndicator ? Xaml.Visibility.Visible : Xaml.Visibility.Collapsed;
        _restore |= layoutChanged;
        QueueUpdate();
    }

    private readonly List<(string OldPath, string NewPath)> _pendingPathMappings = [];
    public void MapPaths(IReadOnlyList<(string OldPath, string NewPath)> mappings)
    {
        if (mappings.Count == 0) return;
        if (_active && _scroll is not null && !_restore) session.Capture(_scroll.VerticalOffset);
        // Apply mappings atomically with the replacement projection. The native dispatcher
        // can render while its listing is awaited, so never expose a mapped anchor to old pages.
        _pendingPathMappings.AddRange(mappings); _interactionVersion++;
    }

    public void SetFiles(string? folder, IReadOnlyList<FileEntry> files, FileEntry? current, bool preferCurrent, bool firstOnMissing = false, bool keepPosition = false)
    {
        if (_active && _scroll is not null && !preferCurrent && !_restore) session.Capture(_scroll.VerticalOffset);
        var previous = session.Pages.ToArray();
        var explicitPath = _explicitPage?.FullPath;
        foreach (var (oldPath, newPath) in _pendingPathMappings)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(explicitPath, oldPath)) explicitPath = newPath;
            else if (explicitPath?.StartsWith(System.IO.Path.TrimEndingDirectorySeparator(oldPath) + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) == true) explicitPath = newPath + explicitPath[System.IO.Path.TrimEndingDirectorySeparator(oldPath).Length..];
        }
        session.MapPaths(_pendingPathMappings); _pendingPathMappings.Clear();
        session.SetFiles(folder, files, current, preferCurrent, firstOnMissing, keepPosition);
        if (!previous.SequenceEqual(session.Pages))
        {
            _interactionVersion++; _endRequested = false;
            _explicitPage = keepPosition ? null :
                session.Pages.FirstOrDefault(file => StringComparer.OrdinalIgnoreCase.Equals(file.FullPath, explicitPath));
            foreach (var row in _rows.Values.Where(row => !session.Pages.Contains(row.File)).ToArray()) RemoveRow(row);
            foreach (var item in _cache.ToArray()) if (!session.Pages.Contains(item.File)) RemoveCache(item);
        }
        _reported = null;
        if (preferCurrent) { _reported = null; _explicitPage = session.Pages.FirstOrDefault(file => file == current); _endRequested = false; }
        _restore = true; QueueUpdate();
    }

    private string _emptyMessage = "No supported images in this folder.";

    public void SetFilterNotice(bool filtered, bool incomplete)
    {
        _emptyMessage = incomplete ? "The folder listing is incomplete." :
            filtered ? "No images match the current filter." : "No supported images in this folder.";
        if (_empty is not null) _empty.Text = _emptyMessage;
    }

    public void SetActive(bool active)
    {
        if (_active == active) return;
        if (!active && _scroll is not null && !_restore) session.Capture(_scroll.VerticalOffset);
        _active = active; _regionKey = null; _interactionVersion++;
        if (!active) StopRows();
        else { _reported = null; _restore = true; QueueUpdate(); UpdateRegion(); }
    }
    public void SetTreeInset(double value) { if (_inset == value) return; _inset = value; _restore = true; QueueUpdate(); }
    public void Invalidate(IEnumerable<string> paths)
    {
        var hints = paths.Select(System.IO.Path.TrimEndingDirectorySeparator).ToArray();
        bool Affected(FileEntry file) => hints.Any(path => StringComparer.OrdinalIgnoreCase.Equals(file.FullPath, path) ||
            file.FullPath.StartsWith(System.IO.Path.EndsInDirectorySeparator(path) ? path : path + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        foreach (var row in _rows.Values.Where(row => Affected(row.File)).ToArray()) RemoveRow(row);
        foreach (var cached in _cache.Where(item => Affected(item.File)).ToArray()) RemoveCache(cached);
        QueueUpdate();
    }
    private void QueueUpdate()
    {
        if (!_active || _queued) return;
        _queued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _queued = false;
            if (_active && IsVisible && IsLoaded && _scroll is { IsLoaded: true } && _canvas is not null) UpdatePages();
        }));
    }
    private void UpdatePages()
    {
        if (_scroll is null || _canvas is null || _scroll.ViewportHeight <= 0 || _scroll.ViewportWidth <= 0) return;
        _updating = true;
        try
        {
            var width = Math.Max(1, _scroll.ViewportWidth - _inset - 32);
            if (Math.Abs(width - _width) > 0.1 || Math.Abs(_scroll.ViewportHeight - _viewportHeight) > 0.1)
            {
                _restore = true; _width = width; _viewportHeight = _scroll.ViewportHeight;
                session.Arrange(width, _viewportHeight);
            }
            _canvas.Width = _scroll.ViewportWidth;
            _canvas.Height = Math.Max(_scroll.ViewportHeight, session.Extent);
            var offset = _scroll.VerticalOffset;
            if (_restore)
            {
                offset = _endRequested ? Math.Max(0, session.Extent - _scroll.ViewportHeight) :
                    Math.Clamp(session.Offset(session.Anchor), 0, Math.Max(0, session.Extent - _scroll.ViewportHeight));
                _canvas.UpdateLayout(); _scroll.UpdateLayout();
                _scroll.ChangeView(null, offset, null, true);
                // ChangeView can apply asynchronously even with animation disabled. Do not
                // overwrite the path/fraction anchor with the old offset in the meantime.
                _restore = Math.Abs(_scroll.VerticalOffset - offset) > 0.5;
                if (!_restore) _explicitScrollOffset = _scroll.VerticalOffset;
            }
            // Realize the intended window while restoration is pending, avoiding unrelated
            // first-page decodes when a reattached ScrollViewer temporarily reports zero.
            var first = session.IndexAt(offset);
            var last = session.IndexAt(offset + _scroll.ViewportHeight);
            var wanted = session.Window(offset, _scroll.ViewportHeight);
            var members = wanted.Select(index => session.Pages[index]).ToHashSet();
            foreach (var row in _rows.Values.Where(row => !members.Contains(row.File)).ToArray()) RemoveRow(row);
            foreach (var index in wanted.OrderBy(index => index < first || index > last ? 1 : 0).ThenBy(index => Math.Abs(index - first)))
            {
                var file = session.Pages[index];
                var pageWidth = session.Width(index);
                var pixelWidth = Math.Clamp((int)(Math.Ceiling(pageWidth * VisualTreeHelper.GetDpi(this).DpiScaleX / 128) * 128), 128, 8192);
                if (_rows.TryGetValue(file, out var existing) && existing.Width != pixelWidth) RemoveRow(existing);
                if (!_rows.TryGetValue(file, out var row))
                {
                    var image = new Image { Stretch = Xaml.Media.Stretch.Uniform, IsHitTestVisible = false };
                    var status = new TextBlock { Text = "Loading " + file.Name, Foreground = new Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                        HorizontalAlignment = Xaml.HorizontalAlignment.Center, VerticalAlignment = Xaml.VerticalAlignment.Center, TextWrapping = Xaml.TextWrapping.Wrap };
                    var errorActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                        HorizontalAlignment = Xaml.HorizontalAlignment.Center, Visibility = Xaml.Visibility.Collapsed };
                    var retry = new Button { Content = "Retry" }; var open = new Button { Content = "Open in Browse" };
                    Xaml.Automation.AutomationProperties.SetName(retry, "Retry " + file.Name);
                    ToolTipService.SetToolTip(retry, "Retry this page (Ctrl+R retries the current page)");
                    Xaml.Automation.AutomationProperties.SetName(open, "Open " + file.Name + " in Browse");
                    errorActions.Children.Add(retry); errorActions.Children.Add(open);
                    var message = new StackPanel { Spacing = 12, VerticalAlignment = Xaml.VerticalAlignment.Center };
                    message.Children.Add(status); message.Children.Add(errorActions);
                    var content = new Grid(); content.Children.Add(message); content.Children.Add(image);
                    var border = new Border { Child = content, Background = new Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black) };
                    Xaml.Automation.AutomationProperties.SetName(border, $"Page {index + 1} of {session.Pages.Count}: {file.Name}");
                    Xaml.Automation.AutomationProperties.SetName(image, $"Page {index + 1} of {session.Pages.Count}: {file.Name}");
                    Xaml.Automation.AutomationProperties.SetAccessibilityView(border, Xaml.Automation.Peers.AccessibilityView.Raw);
                    Xaml.Automation.AutomationProperties.SetAccessibilityView(image, Xaml.Automation.Peers.AccessibilityView.Raw);
                    Xaml.Automation.AutomationProperties.SetAccessibilityView(status, Xaml.Automation.Peers.AccessibilityView.Raw);
                    row = new(file, border, image, status, errorActions, new CancellationTokenSource(), pixelWidth);
                    var created = row;
                    retry.Click += (_, _) => QueueRowAction(created, () => Invalidate([file.FullPath]));
                    open.Click += (_, _) => QueueRowAction(created, () => OpenInBrowseRequested?.Invoke(file));
                    _rows.Add(file, row); _canvas.Children.Add(border);
                    _ = LoadRowAsync(row);
                }
                Xaml.Automation.AutomationProperties.SetName(row.Root, $"Page {index + 1} of {session.Pages.Count}: {file.Name}");
                Xaml.Automation.AutomationProperties.SetName(row.Image, $"Page {index + 1} of {session.Pages.Count}: {file.Name}");
                row.Root.Width = pageWidth; row.Root.Height = session.Height(index);
                Canvas.SetLeft(row.Root, _inset + 16 + (width - pageWidth) / 2); Canvas.SetTop(row.Root, session.Top(index));
            }
            if (_empty is not null) _empty.Visibility = session.Pages.Count == 0 ? Xaml.Visibility.Visible : Xaml.Visibility.Collapsed;
            PublishCurrent();
        }
        finally { _updating = false; }
    }
    private void PublishCurrent()
    {
        if (_scroll is null || !_active || _restore) return;
        session.Capture(_scroll.VerticalOffset);
        var index = CurrentPageIndex;
        var text = index < 0 ? "0 / 0" : $"{index + 1} / {session.Pages.Count}";
        var accessibleName = index < 0 ? "No supported pages" : $"Page {index + 1} of {session.Pages.Count}: {session.Pages[index].Name}";
        if (_indicator is not null && (_indicator.Text != text || Xaml.Automation.AutomationProperties.GetName(_indicator) != accessibleName))
        {
            _indicator.Text = text;
            foreach (var outline in _indicatorOutlines) outline.Text = text;
            Xaml.Automation.AutomationProperties.SetName(_indicator, accessibleName);
            Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(_indicator)?
                .RaiseAutomationEvent(Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        if (index < 0 || session.Pages[index] == _reported) return;
        var file = _reported = session.Pages[index]; var version = _interactionVersion;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_active && version == _interactionVersion && _reported == file && session.Pages.Contains(file)) CurrentPageChanged?.Invoke(file);
        }));
    }
    private bool Live(Row row) => _active && !row.Stop.IsCancellationRequested && _rows.TryGetValue(row.File, out var current) && ReferenceEquals(row, current);
    private async Task LoadRowAsync(Row row)
    {
        var entered = false;
        var token = row.Stop.Token;
        try
        {
            var hit = _cache.FirstOrDefault(item => item.File == row.File && item.Width == row.Width);
            if (hit is not null)
            {
                if (Live(row)) { _cache.Remove(hit); _cache.AddFirst(hit); ApplyImage(row, hit); }
                return;
            }
            await _workers.WaitAsync(token); entered = true;
            if (!Live(row)) return;
            var decoded = await images.DecodeAsync(row.File, row.Width, token);
            if (!Live(row)) return;
            if (decoded.Pixels.LongLength != (long)decoded.Width * decoded.Height * 4 || decoded.Pixels.LongLength > 16L * 1024 * 1024)
                throw new System.IO.InvalidDataException("Reader image exceeds decoded page bounds.");
            // SoftwareBitmapSource can copy on a render worker after SetBitmapAsync completes.
            // Closing its input SoftwareBitmap here caused RO_E_CLOSED / native fail-fast.
            // Copy into a XAML-owned pixel buffer instead; no disposable input outlives this scope.
            var source = new WriteableBitmap(decoded.Width, decoded.Height);
            using (var buffer = source.PixelBuffer.AsStream()) buffer.Write(decoded.Pixels, 0, decoded.Pixels.Length);
            source.Invalidate();
            if (!Live(row)) return;
            while (_cacheBytes + decoded.Pixels.LongLength > CacheLimit && _cache.LastOrDefault(item => !_rows.TryGetValue(item.File, out var present) || present.Width != item.Width) is { } old) RemoveCache(old);
            if (_cacheBytes + decoded.Pixels.LongLength > CacheLimit) throw new InvalidOperationException("Reader memory budget reached. Scroll to release distant pages.");
            var cached = new Cached(row.File, row.Width, source, decoded.Pixels.LongLength, decoded.OriginalWidth, decoded.OriginalHeight);
            _cache.AddFirst(cached); _cacheBytes += cached.Bytes;
            ApplyImage(row, cached);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            if (Live(row))
            {
                row.Status.Text = $"Cannot display {row.File.Name}\n{exception.Message}";
                row.ErrorActions.Visibility = Xaml.Visibility.Visible;
                Xaml.Automation.AutomationProperties.SetName(row.Status, Xaml.Automation.AutomationProperties.GetName(row.Root) + ". " + row.Status.Text);
                Xaml.Automation.AutomationProperties.SetAccessibilityView(row.Status, Xaml.Automation.Peers.AccessibilityView.Content);
                Xaml.Automation.AutomationProperties.SetHelpText(row.Root, row.Status.Text + ". Retry this page, or use Open in Browse.");
            }
        }
        finally { if (entered) _workers.Release(); }
    }
    private void ApplyImage(Row row, Cached cached)
    {
        row.Image.Source = cached.Image; row.Status.Visibility = Xaml.Visibility.Collapsed; row.ErrorActions.Visibility = Xaml.Visibility.Collapsed;
        Xaml.Automation.AutomationProperties.SetAccessibilityView(row.Image, Xaml.Automation.Peers.AccessibilityView.Content);
        if (session.SetDimensions(row.File, cached.OriginalWidth, cached.OriginalHeight)) { _restore = true; QueueUpdate(); }
    }
    private void RemoveCache(Cached cached) { _cache.Remove(cached); _cacheBytes -= cached.Bytes; }
    private void RemoveRow(Row row)
    {
        row.Stop.Cancel(); row.Image.Source = null; _canvas?.Children.Remove(row.Root); _rows.Remove(row.File);
        row.Stop.Dispose();
    }
    private void StopRows() { foreach (var row in _rows.Values.ToArray()) RemoveRow(row); }

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        _source = new DesktopWindowXamlSource(); _source.Initialize(Win32Interop.GetWindowIdFromWindow(parent.Handle));
        _canvas = new Canvas { Background = new Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black) };
        _scroll = new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, ZoomMode = ZoomMode.Disabled, HorizontalScrollMode = ScrollMode.Disabled };
        Xaml.Automation.AutomationProperties.SetName(_scroll, "Continuous reader pages");
        Xaml.Automation.AutomationProperties.SetHelpText(_scroll, "Up/Down scroll. Left/Right select images. Page Up/Down or Space/Shift+Space scroll a viewport. Home/End reach the sequence ends. Ctrl+G jumps to a page. Ctrl+R retries the current page. F3 toggles the folder pin.");
        _scroll.Loaded += (_, _) => { _regionKey = null; UpdateRegion(); QueueUpdate(); };
        _scroll.AddHandler(Xaml.UIElement.PointerWheelChangedEvent, new Xaml.Input.PointerEventHandler((_, _) => OnReaderInput()), true);
        _scroll.AddHandler(Xaml.UIElement.PointerPressedEvent, new Xaml.Input.PointerEventHandler((_, _) => OnReaderInput()), true);
        _scroll.AddHandler(Xaml.UIElement.KeyDownEvent, new Xaml.Input.KeyEventHandler((_, args) => { if (!args.Handled) OnReaderInput(); }), true);
        _scroll.SizeChanged += (_, _) => { _restore = true; QueueUpdate(); };
        _scroll.ViewChanging += (_, args) => OnReaderViewChanging(args.NextView.VerticalOffset);
        _scroll.ViewChanged += (_, _) => OnReaderViewChanged();
        var root = new Grid { Background = new Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black) }; root.Children.Add(_scroll);
        WinUiViewKeyboard.Attach(root, (key, modifiers, repeat) => ViewShortcutHandler?.Invoke(key, modifiers, repeat) ?? false);
        _empty = new TextBlock { Text = _emptyMessage, HorizontalAlignment = Xaml.HorizontalAlignment.Center,
            VerticalAlignment = Xaml.VerticalAlignment.Center, Foreground = new Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White), IsHitTestVisible = false };
        _indicatorHost = OutlinedStatus.Create(_indicatorOutlines, out _indicator);
        _indicatorHost.Visibility = session.Options.ShowPageIndicator ? Xaml.Visibility.Visible : Xaml.Visibility.Collapsed;
        root.Children.Add(_empty); root.Children.Add(_indicatorHost); _source.Content = root;
        _handle = Win32Interop.GetWindowFromWindowId(_source.SiteBridge.WindowId);
        return new HandleRef(this, _handle);
    }
    protected override Size ArrangeOverride(Size size)
    {
        var arranged = base.ArrangeOverride(size);
        if (_source is not null)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _source.SiteBridge.MoveAndResize(new RectInt32(0, 0, Math.Max(1, (int)(arranged.Width * dpi.DpiScaleX)), Math.Max(1, (int)(arranged.Height * dpi.DpiScaleY))));
            UpdateRegion(); QueueUpdate();
        }
        return arranged;
    }
    public void SetExclusions(IEnumerable<Rect> exclusions) { _exclusions = exclusions.ToArray(); UpdateRegion(); }
    private void UpdateRegion()
    {
        if (_handle == 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Round(ActualWidth * dpi.DpiScaleX); var height = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        var rects = _exclusions.Select(rect => (L: (int)Math.Floor(rect.Left * dpi.DpiScaleX), T: (int)Math.Floor(rect.Top * dpi.DpiScaleY),
            R: (int)Math.Ceiling(rect.Right * dpi.DpiScaleX), B: (int)Math.Ceiling(rect.Bottom * dpi.DpiScaleY))).ToArray();
        var key = $"{width},{height};" + string.Join(";", rects); if (key == _regionKey) return;
        var region = CreateRectRgn(0, 0, width, height); if (region == 0) return;
        try
        {
            foreach (var rect in rects)
            {
                var cutout = CreateRectRgn(rect.L, rect.T, rect.R, rect.B); if (cutout == 0) continue;
                try { CombineRgn(region, region, cutout, 4); } finally { DeleteObject(cutout); }
            }
            if (SetWindowRgn(_handle, region, true) != 0) { region = 0; _regionKey = key; }
        }
        finally { if (region != 0) DeleteObject(region); }
    }
    protected override bool TabIntoCore(System.Windows.Input.TraversalRequest request) => _source?.NavigateFocus(
        new XamlSourceFocusNavigationRequest(XamlSourceFocusNavigationReason.First)).WasFocusMoved == true;
    protected override void DestroyWindowCore(HandleRef handle)
    {
        SetActive(false); StopRows(); _cache.Clear(); _cacheBytes = 0;
        if (_source is not null) { _source.Content = null; _source.Dispose(); _source = null; }
        _explicitPage = null; _canvas = null; _scroll = null; _indicatorHost = null; _indicator = null; _indicatorOutlines.Clear(); _handle = 0;
    }
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint destination, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint region);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
}
