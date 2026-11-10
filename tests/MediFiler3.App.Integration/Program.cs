using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediFiler3.Application.Files;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Viewing;
using MediFiler3.Domain.Files;
using MediFiler3.Infrastructure.Files;
using MediFiler3.Interop;
using MediFiler3.Services;
using MediFiler3.Views;

// Explicit Windows smoke runner: real dispatcher, WIC and WinUI, no visible window or audible output.
internal static partial class Program
{
    private static int _passed, _failed, _skipped;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "MediFiler3Integration-" + Guid.NewGuid().ToString("N"));
    private static ViewerHostView _viewer = null!;
    private static readonly FileSystemFileContentService Files = new();
    private static readonly Dictionary<string, FileEntry> Fixtures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Thumbnails Thumbs = new();
    private static readonly FileSystemThumbnailService NativeThumbnails = new();
    private static WinUiToolbarIsland _toolbar = null!;

    [STAThread]
    private static void Main(string[] args)
    {
        var app = new System.Windows.Application();
        var environment = typeof(WinUiMediaPlaybackSession).Assembly.GetType("MediFiler3.Interop.WinUiXamlEnvironment")!;
        environment.GetMethod("Initialize")!.Invoke(null, null);
        _viewer = new ViewerHostView();
        _viewer.Initialize(new WicImageDecoder(Files), Thumbs, new FileSystemTextContentService(Files), new External(), new Defaults());
        _toolbar = new WinUiToolbarIsland { Height = 48 };
        _viewer.PresentationChanged += () =>
        {
            _toolbar.SetViewerCapabilities(_viewer.Capabilities, _viewer.CanZoom);
            _toolbar.SetExternalOpenState(_viewer.ExternalOpenState);
        };
        _viewer.PlaybackStateChanged += () => _toolbar.SetMediaPlaybackState(_viewer.PlaybackState);
        var layout = new DockPanel();
        DockPanel.SetDock(_toolbar, Dock.Top); layout.Children.Add(_toolbar); layout.Children.Add(_viewer);
        var window = new Window { Width = 1280, Height = 800, Opacity = 0, ShowInTaskbar = false,
            ShowActivated = false, Content = layout };
        window.Loaded += async (_, _) =>
        {
            try { await RunAsync(args.FirstOrDefault()); }
            catch (Exception error) { _failed++; Console.WriteLine("FATAL " + error); }
            finally
            {
                _viewer.SetActive(false); _viewer.Clear(); window.Content = null; _toolbar.Dispose(); window.Close();
                environment.GetMethod("Shutdown")!.Invoke(null, null);
                Console.WriteLine($"RESULT: {_passed} passed, {_failed} failed, {_skipped} skipped. Fixtures: {Root}");
                Environment.ExitCode = _failed == 0 ? 0 : 1;
                app.Shutdown();
            }
        };
        app.Run(window);
    }

    private static async Task RunAsync(string? suppliedFixtures)
    {
        Directory.CreateDirectory(Root);
        MakeFixtures();
        if (suppliedFixtures is not null)
            foreach (var path in Directory.EnumerateFiles(Path.GetFullPath(suppliedFixtures)))
            {
                var extension = Path.GetExtension(path);
                if (MediaFileTypes.Supports(extension) || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
                {
                    var target = Path.Combine(Root, "supplied" + extension);
                    File.Copy(path, target, overwrite: false);
                    Fixtures[extension] = Entry(target);
                }
            }
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" })
        {
            if (!Fixtures.TryGetValue(extension, out var file)) { Skip(extension + " fixture unavailable"); continue; }
            await Check("image " + extension, async () =>
            {
                await _viewer.LoadAsync(file, true);
                Require(_viewer.CanZoom && _viewer.Metadata?.PixelWidth > 0 && _viewer.Metadata.PixelHeight > 0, "Decoded image/resolution missing");
                Require(_viewer.Capabilities == (ViewerCapabilities.Zoom | ViewerCapabilities.Pan), "Image controls");
                Require(Visible("ImageViewer") && !Visible("TextPanel") && !Visible("AudioPanel"), "Image visibility");
                Require(((BitmapSource)((Image)_viewer.FindName("ImageViewer")).Source).IsFrozen, "Decoded bitmap is not frozen");
                Require(_viewer.ExternalOpenState.CanChooseApplication, "External opening unavailable");
                var thumbnail = await NativeThumbnails.GetThumbnailPngAsync(file, 120, 120, CancellationToken.None);
                Require(thumbnail is { Length: > 8 } && thumbnail[0] == 137 && thumbnail[1] == 80, "PNG thumbnail unavailable");
                using var exclusive = File.Open(file.FullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            });
        }
        foreach (var extension in TextExtensions)
            await Check("text " + extension, async () =>
            {
                await _viewer.LoadAsync(Fixtures[extension], true);
                Require(_viewer.Metadata?.WordCount == 3 && !_viewer.Metadata.IsPartialWordCount, "Text word count");
                Require(Visible("TextPanel") && !Visible("ImageViewer") && !_viewer.CanZoom, "Text visibility/zoom");
                Require(_viewer.Capabilities == ViewerCapabilities.DocumentScroll, "Text controls");
                Require(((TextBox)_viewer.FindName("TextViewer")).Text == "one two three", "Text content");
            });
        foreach (var (name, encoding) in Encodings)
            await Check("encoding " + name, async () =>
            {
                var file = WriteText("encoding-" + name + ".txt", "hello 世界 😀", encoding);
                await _viewer.LoadAsync(file, true);
                Require(((TextBox)_viewer.FindName("TextViewer")).Text == "hello 世界 😀", "Unicode changed");
            });
        await Check("large text paging and tab restoration", async () =>
        {
            var file = WriteText("large.txt", string.Concat(Enumerable.Repeat("hello world\n", 110000)), new UTF8Encoding(false));
            await _viewer.LoadAsync(file, true);
            Require(_viewer.Metadata?.IsPartialWordCount == true && _viewer.CanReadNextTextWindow, "Bounded first window");
            await _viewer.ReadTextWindowAsync(true);
            Require(_viewer.CanReadPreviousTextWindow && !_viewer.CanReadNextTextWindow, "Last window");
            var last = ((TextBox)_viewer.FindName("TextViewer")).Text;
            _viewer.SetActive(false); _viewer.SetActive(true);
            await WaitUntil(() => Visible("TextPanel"));
            Require(((TextBox)_viewer.FindName("TextViewer")).Text == last, "Window lost on tab change");
            await _viewer.ReadTextWindowAsync(false);
            Require(_viewer.CanReadNextTextWindow && !_viewer.CanReadPreviousTextWindow, "Previous window");
        });
        foreach (var extension in new[] { ".mp4", ".m4v", ".wmv", ".webm", ".mkv", ".mp3", ".wav", ".wma", ".m4a", ".aac", ".opus" })
        {
            if (!Fixtures.TryGetValue(extension, out var file)) { Skip(extension + " fixture unavailable"); continue; }
            await Check("native media " + extension, async () =>
            {
                await _viewer.LoadAsync(file, true);
                Require(_viewer.PlaybackState.Ready, "Media failed: " + ((TextBlock)_viewer.FindName("ImageMessageDetail")).Text);
                Require(!_viewer.PlaybackState.Playing && _viewer.Metadata?.Duration > TimeSpan.Zero, "Paused opening/duration");
                bool audio = MediaFileTypes.IsAudio(extension);
                Require(Visible("AudioPanel") == audio && Visible("MediaHost") != audio, "Media surface routing");
                Require(audio ? _viewer.Metadata?.VideoHeight is null : _viewer.Metadata?.VideoHeight > 0, "Video resolution");
                _viewer.SetPlaybackVolume(0);
                if (!_viewer.PlaybackState.Muted) _viewer.TogglePlaybackMute();
                _viewer.TogglePlayback();
                await WaitUntil(() => _viewer.PlaybackState.Position > TimeSpan.Zero || !_viewer.PlaybackState.Ready);
                Require(_viewer.PlaybackState.Ready && _viewer.PlaybackState.Playing && _viewer.PlaybackState.Position > TimeSpan.Zero, "Playback did not advance");
                _viewer.JumpPlayback(5); _viewer.JumpPlayback(-5); _viewer.StopPlayback();
                Require(!_viewer.PlaybackState.Playing, "Stop failed");
                Console.WriteLine("  " + ViewerMetadataFormatter.Format(_viewer.Metadata));
                using (_viewer.SuspendMediaForFileOperation())
                {
                    var moved = file.FullPath + ".moved";
                    File.Move(file.FullPath, moved); File.Move(moved, file.FullPath);
                }
                await WaitUntil(() => _viewer.PlaybackState.Ready);
                Require(!_viewer.PlaybackState.Playing && _viewer.PlaybackState.Muted, "Operation reload state");
                _viewer.SetActive(false);
                using (File.Open(file.FullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                _viewer.SetActive(true);
                await WaitUntil(() => _viewer.PlaybackState.Ready);
                Require(!_viewer.PlaybackState.Playing && _viewer.PlaybackState.Muted, "Tab reload state");
            });
        }
        await Check("rapid cross-type changes reject stale metadata", async () =>
        {
            var pending = new List<Task>();
            for (int i = 0; i < 20; i++)
                foreach (var key in new[] { ".png", ".txt", ".wav", ".unknown" })
                    pending.Add(_viewer.LoadAsync(Fixtures[key], true));
            await Task.WhenAll(pending);
            Require(_viewer.Capabilities == ViewerCapabilities.None && _viewer.Metadata?.PixelWidth is null &&
                _viewer.Metadata?.Duration is null && _viewer.Metadata?.WordCount is null, "Stale metadata/capabilities");
            Require(!Visible("TextPanel") && !Visible("AudioPanel") && !Visible("MediaHost"), "Stale surface");
        });
        await Check("interrupt an opening media session", async () =>
        {
            var opening = _viewer.LoadAsync(Fixtures[".wav"], true);
            await WaitUntil(() => Field(_viewer, "_media") is not null);
            await _viewer.LoadAsync(Fixtures[".txt"], true);
            await opening;
            Require(Visible("TextPanel") && !Visible("AudioPanel") && !_viewer.PlaybackState.Ready, "Late media replaced text");
            using var exclusive = File.Open(Fixtures[".wav"].FullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        });
        await Check("late audio artwork cannot affect the next file", async () =>
        {
            Thumbs.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await _viewer.LoadAsync(Fixtures[".wav"], true);
            await _viewer.LoadAsync(Fixtures[".txt"], true);
            Thumbs.Pending.SetResult(await File.ReadAllBytesAsync(Fixtures[".png"].FullPath));
            Thumbs.Pending = null;
            await Task.Delay(100);
            Require(!Visible("AudioArtwork") && ((Image)_viewer.FindName("AudioArtwork")).Source is null, "Stale artwork applied");
        });
        await Check("text and image input routing", async () =>
        {
            var text = (TextBox)_viewer.FindName("TextViewer");
            var viewport = (FrameworkElement)_viewer.FindName("ImageViewport");
            int navigation = 0;
            void Navigate(int direction) => navigation += direction;
            _viewer.NavigationRequested += Navigate;
            try
            {
                await _viewer.LoadAsync(Fixtures[".txt"], true);
                text.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent });
                Require(navigation == 1, "Text navigation wheel");
                _viewer.ToggleTextInteraction();
                text.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent });
                Require(navigation == 1 && _viewer.IsTextInteraction, "Text interaction navigated");
                await _viewer.LoadAsync(Fixtures[".png"], true);
                viewport.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent });
                Require(navigation == 2 && !_viewer.IsTextInteraction, "Image navigation/reset");
                _viewer.ZoomInFromToolbar();
                Require(_viewer.CurrentZoomLevel > 1 && _viewer.Interaction == ViewerInteraction.Content, "Zoom interaction");
                viewport.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent });
                Require(navigation == 2, "Image zoom navigated");
                await _viewer.LoadAsync(Fixtures[".txt"], true);
                Require(_viewer.CurrentZoomLevel == 1 && !_viewer.CanZoom, "Zoom state leaked to text");
            }
            finally { _viewer.NavigationRequested -= Navigate; }
        });
        await Check("native toolbar groups follow the active handler", async () =>
        {
            foreach (var (extension, active) in new[] { (".png", "_imageToolbar"), (".txt", "_textToolbar"), (".wav", "_mediaToolbar"), (".unknown", "") })
            {
                await _viewer.LoadAsync(Fixtures[extension], true);
                foreach (var name in new[] { "_imageToolbar", "_textToolbar", "_mediaToolbar" })
                {
                    var group = Field(_toolbar, name)!;
                    var control = (Microsoft.UI.Xaml.FrameworkElement)group.GetType().GetProperty("Control")!.GetValue(group)!;
                    Require((control.Visibility == Microsoft.UI.Xaml.Visibility.Visible) == (name == active), "Wrong toolbar group for " + extension);
                }
                var external = Field(_toolbar, "_externalToolbar")!;
                var choose = (Microsoft.UI.Xaml.Controls.AppBarButton)external.GetType().GetProperty("ChooseButton")!.GetValue(external)!;
                Require(choose.IsEnabled, "Open With disabled for " + extension);
            }
        });
        await Check("mixed folder enumeration and centralized navigation", async () =>
        {
            var state = new NavigationSessionState();
            var listing = new FileSystemFileListingService();
            var files = await listing.GetFilesAsync(Root, CancellationToken.None);
            state.ReplaceFiles(files.Files);
            var navigation = new NavigationController(state, _ => Task.CompletedTask);
            Task load = Task.CompletedTask;
            navigation.FileSelected += file => load = _viewer.LoadAsync(file, true);
            foreach (var key in new[] { ".png", ".txt", ".wav", ".unknown" })
            {
                var selected = state.Files.Single(file => file.FullPath == Fixtures[key].FullPath);
                navigation.SelectFile(selected); await load;
                Require(state.CurrentFile == selected && _viewer.Metadata?.FileSize == selected.Size, "Selection and viewer diverged");
                int index = state.Files.IndexOf(selected);
                if (index + 1 < state.Files.Count)
                {
                    navigation.NavigateByOneFile(1); await load;
                    Require(state.CurrentFile == state.Files[index + 1], "Next file navigation");
                }
            }
        });
        await Check("backward overscroll presents the previous folder's last file", async () =>
        {
            var previous = new FolderTreeEntry("Previous", "previous");
            var current = new FolderTreeEntry("Current", "current");
            var state = new NavigationSessionState();
            state.ReplaceRoots([previous, current]); state.SetActiveFolder(current);
            state.ReplaceFiles([Fixtures[".unknown"]]);
            var loads = new List<Task>();
            var navigation = new NavigationController(state, folder =>
            {
                state.SetActiveFolder(folder);
                state.ReplaceFiles([Fixtures[".png"], Fixtures[".txt"]]);
                loads.Add(_viewer.LoadAsync(state.CurrentFile, true));
                return Task.CompletedTask;
            });
            navigation.FileSelected += file => loads.Add(_viewer.LoadAsync(file, true));
            for (int attempt = 0; attempt < 5; attempt++) navigation.NavigateByOneFile(-1);
            await Task.WhenAll(loads);
            Require(state.CurrentFile == Fixtures[".txt"] && state.CurrentFilePosition == 2 &&
                Visible("TextPanel") && _viewer.Metadata?.WordCount == 3, "Backward folder entry did not present the last file");
        });
        await Check("resize preserves media and centers the text viewport", async () =>
        {
            var window = Window.GetWindow(_viewer);
            await _viewer.LoadAsync(Fixtures[".wav"], true);
            var media = Field(_viewer, "_media");
            window.Width = 1500;
            await Task.Delay(250);
            Require(ReferenceEquals(media, Field(_viewer, "_media")) && _viewer.PlaybackState.Ready, "Resize reopened media");
            await _viewer.LoadAsync(Fixtures[".txt"], true);
            _viewer.TextSideClearance = 340;
            window.UpdateLayout();
            var panel = (FrameworkElement)_viewer.FindName("TextPanel");
            Require(panel.Margin.Left == 364 && panel.Margin.Right == 364 && panel.ActualWidth > 0, "Asymmetric or empty text viewport");
            _viewer.TextSideClearance = 300; window.Width = 1280;
        });
        await Check("unsupported file with thumbnail remains a fallback", async () =>
        {
            Thumbs.Image = await File.ReadAllBytesAsync(Fixtures[".png"].FullPath);
            try
            {
                await _viewer.LoadAsync(Fixtures[".unknown"], true);
                Require(Visible("ImageViewer") && Visible("ThumbnailFallbackBadge") && Visible("ExternalFallbackPanel") && !_viewer.CanZoom, "Thumbnail mistaken for integrated image");
            }
            finally { Thumbs.Image = null; }
        });
        await Check("missing file has a usable fallback", async () =>
        {
            var missing = new FileEntry(Path.Combine(Root, "missing.png"), "missing.png", 1, DateTime.UtcNow);
            await _viewer.LoadAsync(missing, true);
            Require(Visible("ExternalFallbackPanel") && _viewer.ExternalOpenState.CanChooseApplication, "Missing file fallback");
        });
        foreach (var extension in new[] { ".png", ".txt", ".mp4", ".unknown" })
            await Check("corrupt/unsupported fallback " + extension, async () =>
            {
                var path = Path.Combine(Root, "invalid" + extension);
                await File.WriteAllBytesAsync(path, new byte[] { 0xff, 0, 0xff, 0, 1, 2, 3 });
                await _viewer.LoadAsync(Entry(path), true);
                Require(Visible("ExternalFallbackPanel") && _viewer.ExternalOpenState.CanChooseApplication, "Fallback missing");
                Require(!Visible("TextPanel") && !Visible("AudioPanel") && !Visible("MediaHost"), "Failed surface visible");
            });
        await Check("clearing selection removes state and actions", async () =>
        {
            await _viewer.LoadAsync(Fixtures[".txt"], true);
            await _viewer.LoadAsync(null, true);
            Require(_viewer.Metadata is null && _viewer.Capabilities == ViewerCapabilities.None && !_viewer.ExternalOpenState.Visible, "Selection not cleared");
        });
        await Check("unsupported right-click opens the chooser with or without a thumbnail", async () =>
        {
            foreach (bool thumbnail in new[] { false, true })
            {
                Thumbs.Image = thumbnail ? await File.ReadAllBytesAsync(Fixtures[".png"].FullPath) : null;
                try
                {
                    await _viewer.LoadAsync(Fixtures[".unknown"], true);
                    int calls = External.Calls;
                    var click = RightClickViewer();
                    await WaitUntil(() => External.Calls > calls);
                    Require(click.Handled && External.Calls == calls + 1 && External.LastAction == ExternalFileAction.ChooseApplication &&
                        External.LastPath == Fixtures[".unknown"].FullPath, "Right-click did not choose an application for the current file");
                }
                finally { Thumbs.Image = null; }
            }
        });
        await Check("right-click preserves image/text/media behavior and ignores empty selection", async () =>
        {
            int calls = External.Calls;
            await _viewer.LoadAsync(Fixtures[".png"], true);
            RightClickViewer();
            Require(_viewer.Interaction == ViewerInteraction.Content && External.Calls == calls, "Image right-click changed");
            await _viewer.LoadAsync(Fixtures[".txt"], true);
            RightClickViewer();
            Require(_viewer.IsTextInteraction && External.Calls == calls, "Text right-click changed");
            await _viewer.LoadAsync(Fixtures[".wav"], true);
            _viewer.SetPlaybackVolume(0);
            RightClickViewer();
            Require(_viewer.PlaybackState.Playing && External.Calls == calls, "Media right-click changed");
            _viewer.StopPlayback();
            await _viewer.LoadAsync(null, true);
            Require(!RightClickViewer().Handled && External.Calls == calls, "Empty viewer opened chooser");
            await _viewer.LoadAsync(Fixtures[".unknown"], true);
            _viewer.SetActive(false);
            Require(!RightClickViewer().Handled && External.Calls == calls, "Inactive viewer opened chooser");
            _viewer.SetActive(true);
        });
        await RunGifChecksAsync();
        await RunSettingsAndRefreshChecksAsync();
        await RunHomeChecksAsync();
        await RunRootHistoryChecksAsync();
        await RunWindowLayoutChecksAsync();
    }

    private static readonly string[] TextExtensions = [".txt", ".md", ".log", ".csv", ".json", ".xml", ".yaml", ".yml", ".ini", ".cfg", ".config", ".cs", ".xaml", ".html", ".htm", ".css", ".js", ".ts", ".sql", ".ps1", ".sh", ".srt", ".nfo", ".toml", "README", "LICENSE"];
    private static readonly (string, Encoding)[] Encodings = [("utf8", new UTF8Encoding(false)), ("utf8bom", new UTF8Encoding(true)), ("utf16le", new UnicodeEncoding(false, true)), ("utf16be", new UnicodeEncoding(true, true)), ("utf32le", new UTF32Encoding(false, true)), ("utf32be", new UTF32Encoding(true, true))];
    private static void MakeFixtures()
    {
        var pixels = Enumerable.Repeat((byte)127, 64 * 32 * 3).ToArray();
        var bitmap = BitmapSource.Create(64, 32, 96, 96, PixelFormats.Rgb24, null, pixels, 64 * 3);
        foreach (var (extension, encoder) in new (string, BitmapEncoder)[] { (".jpg", new JpegBitmapEncoder()), (".jpeg", new JpegBitmapEncoder()), (".png", new PngBitmapEncoder()), (".bmp", new BmpBitmapEncoder()), (".gif", new GifBitmapEncoder()), (".tif", new TiffBitmapEncoder()), (".tiff", new TiffBitmapEncoder()) })
        {
            var path = Path.Combine(Root, "image" + extension);
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
            Fixtures[extension] = Entry(path);
        }
        foreach (var extension in TextExtensions)
            Fixtures[extension] = WriteText(extension.StartsWith('.') ? "document" + extension : extension, "one two three", new UTF8Encoding(false));
        var wav = Path.Combine(Root, "silent.wav");
        using (var writer = new BinaryWriter(File.Create(wav)))
        {
            const int length = 44100 * 2;
            writer.Write("RIFF"u8); writer.Write(36 + length); writer.Write("WAVEfmt "u8); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(22050); writer.Write(44100);
            writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(length); writer.Write(new byte[length]);
        }
        Fixtures[".wav"] = Entry(wav);
        Fixtures[".unknown"] = WriteText("file.unknown", "unknown format", new UTF8Encoding(false));
    }
    private static FileEntry WriteText(string name, string text, Encoding encoding)
    {
        var path = Path.Combine(Root, name); File.WriteAllText(path, text, encoding); return Entry(path);
    }
    private static FileEntry Entry(string path) { var file = new FileInfo(path); return new(file.FullName, file.Name, file.Length, file.LastWriteTimeUtc); }
    private static bool Visible(string name) => ((FrameworkElement)_viewer.FindName(name)).Visibility == Visibility.Visible;
    private static MouseButtonEventArgs RightClickViewer()
    {
        var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
        { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent };
        ((UIElement)_viewer.FindName("ImageViewport")).RaiseEvent(click);
        return click;
    }
    private static object? Field(object instance, string name) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200; i++) { if (condition()) return; await Task.Delay(25); }
        throw new TimeoutException("Viewer did not settle within 5 seconds.");
    }
    private static async Task Check(string name, Func<Task> test)
    {
        try { await test().WaitAsync(TimeSpan.FromSeconds(40)); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { _failed++; Console.WriteLine("FAIL " + name + ": " + error); }
    }
    private static void Skip(string name) { _skipped++; Console.WriteLine("SKIP " + name); }
    // Association/launch stubs prevent opening other applications. Native association contracts have separate tests.
    private sealed class Defaults : IDefaultApplicationService
    { public Task<DefaultApplicationInfo> LookupAsync(string path, CancellationToken token) => Task.FromResult(new DefaultApplicationInfo(DefaultApplicationStatus.Unknown)); }
    private sealed class External : IExternalFileService
    {
        public static int Calls;
        public static string? LastPath;
        public static ExternalFileAction LastAction;
        public Task<ExternalFileResult> OpenAsync(string path, ExternalFileAction action, nint owner, CancellationToken token = default)
        {
            Calls++; LastPath = path; LastAction = action;
            return Task.FromResult(new ExternalFileResult(ExternalFileOutcome.Cancelled, "Cancelled"));
        }
    }
    private sealed class Thumbnails : IThumbnailService
    {
        public TaskCompletionSource<byte[]?>? Pending;
        public byte[]? Image;
        public Task<byte[]?> GetThumbnailPngAsync(FileEntry file, int width, int height, CancellationToken token) => Pending?.Task ?? Task.FromResult(Image);
    }
}
