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
            await Check("AppWindow title content reserves caption buttons and preserves native drag hit testing", async () =>
            {
                await WaitUntil(() => ((FrameworkElement)window.FindName("TitleBarFrame")).ActualHeight > 0);
                var appWindow = (Microsoft.UI.Windowing.AppWindow)Field(window, "_appWindow")!;
                var title = (WinUiTitleBarIsland)window.FindName("TitleBarIsland");
                Require(appWindow.TitleBar.ExtendsContentIntoTitleBar && title.Margin.Right > 0,
                    "Title content does not reserve the native caption buttons");
                var point = title.PointToScreen(new Point(title.ActualWidth / 2, title.ActualHeight / 2));
                var coordinates = (nint)(((int)point.X & 0xffff) | (((int)point.Y & 0xffff) << 16));
                Require(SendLayoutMessage(new WindowInteropHelper(window).Handle, 0x0084, 0, coordinates) == 2,
                    "Extended title region no longer reports HTCAPTION for native dragging");
                window.Title = "Title content probe";
                await WaitUntil(() => ((Microsoft.UI.Xaml.Controls.TextBlock)Field(title, "_title")!).Text == window.Title);
                window.Title = "MediFiler3";
            });
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
                await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                var carouselScroll = (ScrollViewer)Field(browse, "_carouselScrollViewer")!;
                Require(!((System.Windows.Threading.DispatcherTimer)Field(browse, "_carouselScrollTimer")!).IsEnabled &&
                    Math.Abs(carouselScroll.HorizontalOffset - 40 * 156) < 1,
                    "Rapid navigation left the carousel easing behind its latest selected position");
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
                var hiddenRevealBounds = browse.FolderRevealBounds;
                Require(hiddenRevealBounds.Left == 0 && hiddenRevealBounds.Right == 56 + ((FrameworkElement)browse.FindName("FolderOverlay")).Width,
                    "Fullscreen hidden reveal does not span the sidebar and pinned tree width");
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
                Require(browse.FolderRevealBounds.Left == hiddenRevealBounds.Left && browse.FolderRevealBounds.Width == hiddenRevealBounds.Width,
                    "Opening the tree changed the reveal width");
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
            await Check("Folder depth colors render on real tree labels and native settings persist live changes", async () =>
            {
                var colorsPath = Directory.CreateDirectory(Path.Combine(directory, "colors")).FullName;
                File.WriteAllText(Path.Combine(colorsPath, "note.txt"), "color fixture");
                var next = colorsPath;
                for (var depth = 1; depth <= 7; depth++)
                {
                    next = Directory.CreateDirectory(Path.Combine(next, "level" + depth)).FullName;
                    File.WriteAllText(Path.Combine(next, "note.txt"), "color fixture");
                }
                Destination("Browse"); browse.SetFolderPinned(true);
                await LoadRestartRootsAsync(window, [colorsPath]);
                var tree = (TreeView)browse.FindName("FolderTree");
                IEnumerable<DependencyObject> Visuals(DependencyObject parent)
                {
                    yield return parent;
                    for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                        foreach (var child in Visuals(System.Windows.Media.VisualTreeHelper.GetChild(parent, i))) yield return child;
                }
                var expected = new byte[] { 255, 255, 223, 192, 160, 128, 128, 128 };
                List<FolderTreeEntry> Chain()
                {
                    var chain = new List<FolderTreeEntry>();
                    for (var folder = state.Roots.FirstOrDefault(); folder is not null; folder = folder.Children.FirstOrDefault()) chain.Add(folder);
                    return chain;
                }
                bool ColorsMatch(bool shaded)
                {
                    window.UpdateLayout();
                    var chain = Chain(); if (chain.Count != 8) return false;
                    var labels = Visuals(tree).OfType<TextBlock>().ToArray();
                    return chain.Select((folder, depth) => (folder, depth)).All(pair =>
                        labels.FirstOrDefault(label => ReferenceEquals(label.DataContext, pair.folder) && label.Text == pair.folder.DisplayName)?.Foreground
                            is System.Windows.Media.SolidColorBrush brush && brush.Color == System.Windows.Media.Color.FromRgb(
                                shaded ? expected[pair.depth] : (byte)255, shaded ? expected[pair.depth] : (byte)255, shaded ? expected[pair.depth] : (byte)255));
                }
                await WaitUntil(() => ColorsMatch(true));
                var originalRoot = state.Roots[0];
                Destination("Settings");
                var island = (WinUiCarouselSettingsIsland)settingsView.FindName("CarouselSliderIsland");
                await WaitUntil(() => Field(island, "_folderDepthToggle") is Microsoft.UI.Xaml.Controls.ToggleSwitch);
                ((Microsoft.UI.Xaml.Controls.ToggleSwitch)Field(island, "_folderDepthToggle")!).IsOn = false;
                await settings.FlushAsync();
                var restored = new ApplicationSettingsService(new SqliteApplicationStateStore(new SqliteDatabase(db.DatabasePath)));
                await restored.InitializeAsync();
                Require(!restored.Current.FolderDepthShadingEnabled && !browse.FolderDepthShadingEnabled,
                    "Folder text setting did not persist/apply");
                Destination("Browse"); await WaitUntil(() => ColorsMatch(false));
                Require(ReferenceEquals(originalRoot, state.Roots[0]), "Changing text colors replaced the tree");
                Destination("Settings");
                await WaitUntil(() => Field(island, "_folderDepthToggle") is Microsoft.UI.Xaml.Controls.ToggleSwitch);
                ((Microsoft.UI.Xaml.Controls.ToggleSwitch)Field(island, "_folderDepthToggle")!).IsOn = true;
                Destination("Browse"); await WaitUntil(() => ColorsMatch(true));
                await settings.FlushAsync();
            });
            await RunFolderNameRuleChecksAsync(window, state, browse, settingsView, settings, db, directory);
            await RunFileNameRuleChecksAsync(window, state, browse, settingsView, settings, db, directory);
            await Check("Carousel uses its full revealed bounds for hover in normal and fullscreen modes", async () =>
            {
                Destination("Browse"); browse.SetCarouselEnabled(true);
                var panel = (FrameworkElement)browse.FindName("CarouselOverlay");
                foreach (var fullscreen in new[] { false, true })
                {
                    browse.SetFullscreenCarousel(fullscreen);
                    browse.UpdateLayout();
                    var bounds = browse.CarouselRevealBounds;
                    var tree = (TreeView)browse.FindName("FolderTree");
                    Require(tree.TranslatePoint(new Point(0, tree.ActualHeight), browse).Y > bounds.Top && browse.TreeScrollClearance == panel.Height,
                        "Tree viewport lost full height or its scrollable footer");
                    Require(bounds.Width == browse.ActualWidth && bounds.Height == panel.Height, "Carousel reveal does not span its fully shown area");
                    var upperPoint = new Point(bounds.Left + bounds.Width / 2, bounds.Top + 2);
                    Require(upperPoint.Y < browse.ActualHeight - 26, "Fixture did not test beyond the old bottom strip");
                    browse.UpdateShellCarouselHover(upperPoint);
                    Require((bool)Field(browse, "_carouselOverlayVisible")!, "Full hidden carousel area did not reveal it");
                    browse.UpdateShellCarouselHover(upperPoint);
                    Require((bool)Field(browse, "_carouselOverlayVisible")!, "Same bounds did not retain the revealed carousel");
                    browse.UpdateShellCarouselHover(new Point(upperPoint.X, bounds.Top - 1));
                    Require(!(bool)Field(browse, "_carouselOverlayVisible")!, "Leaving the full bounds did not hide the carousel");
                    browse.SetCarouselEnabled(false); browse.UpdateShellCarouselHover(upperPoint);
                    Require(browse.TreeScrollClearance == 48, "Disabled carousel retained excessive tree clearance");
                    Require(!(bool)Field(browse, "_carouselOverlayVisible")!, "Disabled carousel revealed");
                    browse.SetCarouselEnabled(true);
                }
                browse.SetFullscreenCarousel(false); browse.UpdateShellCarouselHover(null);
                Require(browse.FindName("BottomRevealTarget") is null, "Separate narrow reveal target remains");
                await Task.CompletedTask;
            });
            await Check("Browse Home and End jump within the current folder without boundary navigation", async () =>
            {
                Destination("Browse");
                Require(state.Files.Count >= 2, "Browse endpoint fixture needs multiple files");
                var folder = state.ActiveFolder; var root = state.ActiveRoot;
                KeyPress(Key.End);
                Require(ReferenceEquals(state.CurrentFile, state.Files[^1]), "End did not select the last file");
                for (var repeat = 0; repeat < 7; repeat++) KeyPress(Key.End);
                KeyPress(Key.Home);
                Require(ReferenceEquals(state.CurrentFile, state.Files[0]), "Home did not select the first file");
                for (var repeat = 0; repeat < 7; repeat++) KeyPress(Key.Home);
                Require(ReferenceEquals(folder, state.ActiveFolder) && ReferenceEquals(root, state.ActiveRoot), "Endpoint keys crossed folders");
                Require(toolbar.ViewShortcutHandler?.Invoke(Key.End, ModifierKeys.None, false) == true && ReferenceEquals(state.CurrentFile, state.Files[^1]),
                    "Forwarded native toolbar End did not jump to the last file");
                browse.SetViewerActive(false);
                Require(!browse.HandleViewShortcut(Key.Home, ModifierKeys.None) && ReferenceEquals(state.CurrentFile, state.Files[^1]), "Inactive Browse consumed Home");
                browse.SetViewerActive(true);
                await Task.CompletedTask;
            });
            await Check("large folder trees recycle across fullscreen and collapse only direct children", async () =>
            {
                Destination("Browse"); browse.SetFolderPinned(true);
                var groups = Enumerable.Range(0, 100).Select(g => new FolderTreeEntry($"group{g:D3}", Path.Combine(directory, $"group{g:D3}"),
                    Enumerable.Range(0, 100).Select(i => new FolderTreeEntry($"leaf{i:D3}", Path.Combine(directory, $"group{g:D3}", $"leaf{i:D3}"), directFileCount: 0)).ToArray(), directFileCount: 0)).ToArray();
                groups[0].Children[0].AddChild(new FolderTreeEntry("deep", Path.Combine(directory, "group000", "leaf000", "deep"), directFileCount: 0));
                var root = new FolderTreeEntry("scale", directory, groups, isRoot: true, directFileCount: 0);
                state.ReplaceRoots([root]); state.SetActiveFolder(root);
                window.UpdateLayout(); await Task.Delay(100);
                var tree = (TreeView)browse.FindName("FolderTree");
                IEnumerable<DependencyObject> Visuals(DependencyObject parent)
                {
                    yield return parent;
                    for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                        foreach (var child in Visuals(System.Windows.Media.VisualTreeHelper.GetChild(parent, i))) yield return child;
                }
                int Realized() => Visuals(tree).OfType<TreeViewItem>().Count();
                Require(VirtualizingPanel.GetIsVirtualizing(tree) && VirtualizingPanel.GetVirtualizationMode(tree) == VirtualizationMode.Recycling,
                    "Tree recycling is disabled");
                var initial = Realized();
                Require(initial > 0 && initial < 500, $"Large tree realized {initial} containers");
                var timer = System.Diagnostics.Stopwatch.StartNew();
                window.ToggleFullscreen(); StopFullscreenTimer(); window.UpdateLayout();
                typeof(MediFiler3.MainWindow).GetMethod("SetFullscreenChrome", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [true, true]);
                window.UpdateLayout(); var enterMs = timer.Elapsed.TotalMilliseconds;
                var scroll = Visuals(tree).OfType<ScrollViewer>().First();
                scroll.ScrollToVerticalOffset(scroll.ScrollableHeight * .75); window.UpdateLayout(); await Task.Delay(100);
                var distant = Realized();
                Require(distant > 0 && distant < 500 && root.IsExpanded && groups.All(g => g.IsExpanded) && groups[0].Children[0].IsExpanded,
                    $"Tree recycling lost expansion or retained {distant} containers");
                timer.Restart(); window.ToggleFullscreen(); window.UpdateLayout(); var exitMs = timer.Elapsed.TotalMilliseconds;
                await browse.ExecuteFolderMenuActionAsync(root, "collapse-children");
                Require(root.IsExpanded && groups.All(g => !g.IsExpanded) && groups[0].Children[0].IsExpanded && ReferenceEquals(state.Roots[0], root),
                    "Collapse child folders reset the root, deeper expansion or node identity");
                groups[0].IsExpanded = true;
                Require(groups[0].Children[0].IsExpanded && root.TotalFileCount == 0, "Reopening lost deeper expansion or counters");
                await WaitUntil(() =>
                {
                    scroll.ScrollToEnd(); window.UpdateLayout();
                    var last = Visuals(tree).OfType<TreeViewItem>().FirstOrDefault(item => ReferenceEquals(item.DataContext, groups[^1]));
                    return last is not null && last.TranslatePoint(new Point(0, last.ActualHeight), browse).Y <= browse.CarouselRevealBounds.Top + 1;
                });
                Require(tree.TranslatePoint(new Point(0, tree.ActualHeight), browse).Y > browse.CarouselRevealBounds.Top,
                    "Tree footer shortened the viewport rather than extending the scrollable content");
                var sample = new { Records = 10102, InitialContainers = initial, DistantContainers = distant, EnterFullscreenMs = enterMs, ExitFullscreenMs = exitMs };
                var json = System.Text.Json.JsonSerializer.Serialize(sample);
                File.WriteAllText(Path.Combine(Root, "tree-scale.json"), json); Console.WriteLine("TREE SCALE " + json);
            });
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
