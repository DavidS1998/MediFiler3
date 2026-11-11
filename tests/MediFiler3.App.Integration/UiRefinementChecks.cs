using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using MediFiler3.Application.Files;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Persistence;
using MediFiler3.Domain.Files;
using MediFiler3.Infrastructure.Files;
using MediFiler3.Infrastructure.Folders;
using MediFiler3.Infrastructure.Operations;
using MediFiler3.Infrastructure.Persistence;
using MediFiler3.Interop;
using MediFiler3.Services;
using MediFiler3.Views;

internal static partial class Program
{
    private static async Task RunUiRefinementChecksAsync()
    {
        var directory = Path.Combine(Root, "ui-refinement"); Directory.CreateDirectory(directory);
        for (var i = 0; i < 80; i++) File.Copy(Fixtures[".png"].FullPath, Path.Combine(directory, $"{i:D3}.png"));
        var db = new SqliteDatabase(Path.Combine(Root, "ui-refinement.db")); var store = new SqliteApplicationStateStore(db);
        var settings = new ApplicationSettingsService(store); await settings.InitializeAsync();
        var layout = new WindowLayoutService(store); await layout.InitializeAsync();
        var journal = new SqliteFileOperationJournal(db); var identities = new WindowsFileIdentityProvider();
        var backups = new FileSystemOperationBackupStore(journal, identities, Path.Combine(Root, "ui-backups"));
        var operations = new FileSystemFileOrganizationService(journal, identities, backupStore: backups);
        var availability = new FileSystemRootFolderAvailabilityService(); var state = new NavigationSessionState();
        var thumbnails = new CountingThumbnails();
        var window = new MediFiler3.MainWindow(Microsoft.Extensions.Logging.Abstractions.NullLogger<MediFiler3.MainWindow>.Instance,
            new FileSystemFolderTreeService(), new FileSystemFileListingService(), state, new WicImageDecoder(Files), thumbnails,
            operations, journal, new FileSystemTextContentService(Files), new External(), new Defaults(), settings,
            new RootFolderHistoryService(store, availability), availability, new FileSystemCollisionPreviewService(thumbnails), backups, layout, new WindowsWindowPlacement())
        { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        window.Show();
        ((System.Windows.Threading.DispatcherTimer)Field(window, "_folderHoverTimer")!).Stop();
        var browse = (BrowseView)Field(window, "_browseView")!;
        var settingsView = (SettingsView)Field(window, "_settingsView")!;
        var toolbar = (WinUiToolbarIsland)window.FindName("ToolbarIsland");
        void Destination(string value) => typeof(MediFiler3.MainWindow).GetMethod("OnNavigationSelectionChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [value]);
        void KeyPress(Key key) => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        void StopFullscreenTimer() => ((System.Windows.Threading.DispatcherTimer)Field(window, "_fullscreenTimer")!).Stop();
        try
        {
            await Check("Settings native buttons and carousel switch persist visibility", async () =>
            {
                Destination("Settings");
                var island = (WinUiCarouselSettingsIsland)settingsView.FindName("CarouselSliderIsland");
                await WaitUntil(() => Field(island, "_carouselToggle") is not null);
                Require(Field(island, "_clearBackups") is Microsoft.UI.Xaml.Controls.Button && Field(island, "_clearCache") is Microsoft.UI.Xaml.Controls.Button, "Settings still uses WPF action buttons");
                ((Microsoft.UI.Xaml.Controls.Slider)Field(island, "_slider")!).Value = 5;
                var toggle = (Microsoft.UI.Xaml.Controls.ToggleSwitch)Field(island, "_carouselToggle")!;
                toggle.IsOn = false;
                Require(!browse.CarouselEnabled && !((Microsoft.UI.Xaml.Controls.Slider)Field(island, "_slider")!).IsEnabled, "Carousel was not disabled");
                await settings.FlushAsync();
                Require(!(await ((IApplicationSettingsStore)new SqliteApplicationStateStore(new SqliteDatabase(Path.Combine(Root, "ui-refinement.db")))).LoadAsync()).CarouselEnabled,
                    "Visibility did not survive reopening database");
            });
            await LoadRestartRootsAsync(window, [directory]);
            await WaitUntil(() => state.Files.Count == 80 && browse.CanZoom);
            await Check("disabled carousel performs no carousel work and preserves viewer navigation", async () =>
            {
                var calls = thumbnails.Calls;
                var navigate = typeof(BrowseView).GetMethod("NavigateByOneFile", BindingFlags.Instance | BindingFlags.NonPublic)!;
                for (var i = 0; i < 20; i++) navigate.Invoke(browse, [1]);
                await Task.Delay(100);
                Require(state.CurrentFilePosition == 21 && browse.CanZoom, "Disabling carousel disabled navigation/viewer");
                Require(browse.CarouselItems.Count == 0 && ((FrameworkElement)browse.FindName("CarouselOverlay")).Visibility == Visibility.Collapsed && thumbnails.Calls == calls,
                    "Disabled carousel still displays or requests thumbnails");
            });
            await Check("rapid carousel navigation coalesces and cancels obsolete edge thumbnails", async () =>
            {
                browse.SetCarouselEnabled(true);
                await WaitUntil(() => browse.CarouselItems.OfType<CarouselThumbnailItem>().Count() == 5);
                var old = browse.CarouselItems.OfType<CarouselThumbnailItem>().First();
                await WaitUntil(() => old.ThumbnailCancellation is not null);
                var token = old.ThumbnailCancellation!.Token;
                var navigation = typeof(BrowseView).GetMethod("NavigateByOneFile", BindingFlags.Instance | BindingFlags.NonPublic)!;
                for (var i = 0; i < 20; i++) navigation.Invoke(browse, [1]);
                Require(state.CurrentFilePosition == 41, "Rapid input lost navigation steps");
                await WaitUntil(() => browse.CarouselItems.OfType<CarouselThumbnailItem>().Any(i => i.IsCurrent && ReferenceEquals(i.File, state.CurrentFile)));
                Require(browse.CarouselItems.Count <= 7 && token.IsCancellationRequested, "Carousel backlog is not bounded/cancelled");
                Require(browse.CarouselItems.OfType<CarouselThumbnailItem>().Select(i => i.File).Distinct().Count() == 5, "Duplicate carousel tiles");
                var unfinished = browse.CarouselItems.OfType<CarouselThumbnailItem>().First();
                unfinished.Thumbnail = null;
                unfinished.ThumbnailTask = new TaskCompletionSource<System.Windows.Media.Imaging.BitmapSource?>().Task;
                browse.SetViewerActive(false); browse.SetViewerActive(true);
                await WaitUntil(() => unfinished.Thumbnail is not null);
                Require(state.CurrentFilePosition == 41, "Resuming cancelled thumbnails changed selection");
            });
            await Check("carousel edge tiles fade while surviving items retain identity", async () =>
            {
                typeof(BrowseView).GetMethod("SetCarouselOverlayVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browse, [true]);
                await Task.Delay(200);
                var kept = browse.CarouselItems.OfType<CarouselThumbnailItem>().First(i => i.IsCurrent);
                typeof(BrowseView).GetMethod("NavigateByOneFile", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browse, [1]);
                await WaitUntil(() => browse.CarouselItems.OfType<CarouselThumbnailItem>().Any(i => i.IsCurrent && ReferenceEquals(i.File, state.CurrentFile)));
                Require(browse.CarouselItems.Contains(kept), "Adjacent navigation recreated surviving tile");
                var ghosts = (Canvas)browse.FindName("CarouselGhosts");
                if (SystemParameters.ClientAreaAnimation) Require(ghosts.Children.Count > 0 && ghosts.Children.Count <= 24, "No bounded edge fade-out");
                Require(ghosts.Children.OfType<StackPanel>().All(tile => tile.Children[0] is Border { CornerRadius.TopLeft: 0 }), "A fading carousel image still has rounded corners");
                await Task.Delay(160);
                Require(ghosts.Children.Count == 0, "Fade-out visuals were not released");
            });
            await Check("F11 fullscreen hides chrome and restores normal bounds without persisting fullscreen", async () =>
            {
                await Task.Delay(150);
                var saved = layout.Current;
                var before = new Size(window.ActualWidth, window.ActualHeight);
                KeyPress(Key.F11); StopFullscreenTimer();
                await Task.Delay(50);
                Require(window.IsFullscreen && window.WindowStyle == WindowStyle.None, "F11 did not enter borderless fullscreen");
                Require(((FrameworkElement)window.FindName("ToolbarFrame")).Visibility == Visibility.Collapsed && ((FrameworkElement)window.FindName("Sidebar")).Visibility == Visibility.Collapsed,
                    "Fullscreen chrome remains visible");
                // A pre-existing hover animation can still be settling after the 50 ms shell check.
                await WaitUntil(() => ((System.Windows.Media.TranslateTransform)browse.FindName("CarouselOverlayTransform")).Y == 175 &&
                    ((FrameworkElement)browse.FindName("CarouselOverlay")).Opacity == 0);
                Require(layout.Current == saved, "Fullscreen overwrote persisted normal layout");
                var button = (Microsoft.UI.Xaml.Controls.AppBarButton)Field(toolbar, "_fullscreenButton")!;
                var root = (Microsoft.UI.Xaml.Controls.Grid)Field(toolbar, "_rootElement")!;
                Require(Microsoft.UI.Xaml.Controls.Grid.GetColumn(button) == root.ColumnDefinitions.Count - 1, "Fullscreen button is not rightmost");
                typeof(MediFiler3.MainWindow).GetMethod("SetFullscreenChrome", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [true, true]);
                window.UpdateLayout();
                Require(window.WindowStyle == WindowStyle.SingleBorderWindow, "Top reveal omitted native title bar");
                Require((bool)Field(browse, "_folderOverlayVisible")! && ((FrameworkElement)browse.FindName("FolderOverlay")).Margin.Left == 56,
                    "Left reveal did not show tree beside sidebar");
                var move = new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent };
                typeof(BrowseView).GetMethod("OnBrowseSurfaceMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browse, [browse, move]);
                Require((bool)Field(browse, "_folderOverlayVisible")!, "Crossing into native sidebar hid tree");
                typeof(MediFiler3.MainWindow).GetMethod("SetFullscreenChrome", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false, false]);
                Require(window.WindowStyle == WindowStyle.None && !(bool)Field(browse, "_folderOverlayVisible")!, "Leaving edges retained caption/tree");
                typeof(MediFiler3.MainWindow).GetMethod("SetFullscreenChrome", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [true, true]);
                Require(((FrameworkElement)window.FindName("ToolbarFrame")).Visibility == Visibility.Visible && ((FrameworkElement)window.FindName("Sidebar")).Visibility == Visibility.Visible,
                    "Edge reveal did not expose chrome");
                Require(((System.Windows.Controls.ColumnDefinition)window.FindName("SidebarColumn")).Width.Value == 0, "Reveal changed viewer layout");
                Require(((IReadOnlyList<Rect>)Field(browse, "_shellVideoExclusions")!).Count == 2, "Native video does not exclude revealed shell surfaces");
                KeyPress(Key.F11);
                await Task.Delay(100);
                Require(!window.IsFullscreen && window.WindowStyle != WindowStyle.None && Math.Abs(window.ActualWidth - before.Width) < 2 && Math.Abs(window.ActualHeight - before.Height) < 2, "Fullscreen did not restore normal window");
                Require(((FrameworkElement)window.FindName("ToolbarFrame")).Visibility == Visibility.Visible, "Toolbar missing after exit");
            });
            if (Fixtures.TryGetValue(".mp4", out var video))
                await Check("fullscreen and shell edge reveals retain native video playback session", async () =>
                {
                    var mediaRoot = Path.Combine(Root, "fullscreen-video"); Directory.CreateDirectory(mediaRoot);
                    File.Copy(video.FullPath, Path.Combine(mediaRoot, "video.mp4"));
                    await LoadRestartRootsAsync(window, [mediaRoot]);
                    await WaitUntil(() => browse.PlaybackState.Ready);
                    browse.SetPlaybackVolume(0); if (!browse.PlaybackState.Muted) browse.TogglePlaybackMute();
                    browse.TogglePlayback(); await WaitUntil(() => browse.PlaybackState.Playing);
                    var host = (ViewerHostView)browse.FindName("ViewerHost"); var session = Field(host, "_media");
                    window.ToggleFullscreen(); StopFullscreenTimer();
                    typeof(MediFiler3.MainWindow).GetMethod("SetFullscreenChrome", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [true, true]);
                    await Task.Delay(100);
                    Require(ReferenceEquals(session, Field(host, "_media")) && browse.PlaybackState.Playing, "Fullscreen/reveal replaced or paused video session");
                    window.ToggleFullscreen(); await Task.Delay(100);
                    Require(ReferenceEquals(session, Field(host, "_media")) && browse.PlaybackState.Playing, "Leaving fullscreen interrupted video");
                    browse.StopPlayback();
                });
            await RunGalleryChecksAsync(window, state, browse, toolbar, settings, thumbnails, directory);
            await Check("native-island F11 shortcut works and closing fullscreen retains normal restart layout", async () =>
            {
                Destination("Settings");
                var island = (WinUiCarouselSettingsIsland)settingsView.FindName("CarouselSliderIsland");
                await WaitUntil(() => island.IsLoaded && island.Handle != IntPtr.Zero && Field(island, "_slider") is not null);
                var message = new MSG { hwnd = island.Handle, message = 0x0100, wParam = new IntPtr(0x7A) };
                var arguments = new object[] { message, false };
                typeof(MediFiler3.MainWindow).GetMethod("OnFullscreenMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
                Require((bool)arguments[1], "Native F11 was not handled");
                await WaitUntil(() => window.IsFullscreen); StopFullscreenTimer();
                var normal = layout.Current;
                window.Close(); await layout.FlushAsync();
                var restarted = new WindowLayoutService(new SqliteApplicationStateStore(new SqliteDatabase(Path.Combine(Root, "ui-refinement.db"))));
                await restarted.InitializeAsync();
                Require(restarted.Current == normal, "Closing fullscreen saved fullscreen bounds");
            });
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    private sealed class CountingThumbnails : IThumbnailService
    {
        public int Calls;
        public readonly System.Collections.Concurrent.ConcurrentBag<(string Path, int Width, int Height)> Requests = [];
        public Task<byte[]?> GetDefaultThumbnailPngAsync(FileEntry file, CancellationToken cancellationToken)
        { Requests.Add((file.FullPath, 0, 0)); Interlocked.Increment(ref Calls); return NativeThumbnails.GetDefaultThumbnailPngAsync(file, cancellationToken); }
        public Task<byte[]?> GetThumbnailPngAsync(FileEntry file, int width, int height, CancellationToken cancellationToken)
        { Requests.Add((file.FullPath, width, height)); Interlocked.Increment(ref Calls); return NativeThumbnails.GetThumbnailPngAsync(file, width, height, cancellationToken); }
    }
}
