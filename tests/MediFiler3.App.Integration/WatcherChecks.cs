using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MediFiler3.Application.Files;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Infrastructure.Files;
using MediFiler3.Infrastructure.Folders;
using MediFiler3.Views;
using MediFiler3.Services;

internal static partial class Program
{
    private static async Task RunWatcherChecksAsync()
    {
        var first = Path.Combine(Root, "watch-first"); var second = Path.Combine(Root, "watch-second");
        Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "1.txt"), "original text");
        File.WriteAllText(Path.Combine(first, "2.txt"), "second text");
        var state = new NavigationSessionState(); var monitor = new ManualMonitor();
        var database = new MediFiler3.Infrastructure.Persistence.SqliteDatabase(Path.Combine(Root, "watch-history.db"));
        var history = new RootFolderHistoryService(new MediFiler3.Infrastructure.Persistence.SqliteApplicationStateStore(database), new FileSystemRootFolderAvailabilityService());
        using var thumbnails = new FileSystemThumbnailService(cachePath: Path.Combine(Root, "watch-cache"));
        var browse = new BrowseView(new FileSystemFolderTreeService(), new FileSystemFileListingService(), state,
            new WicImageDecoder(Files), thumbnails, null!, new FileSystemTextContentService(Files), new External(), new Defaults(), rootHistory: history, changeMonitor: monitor);
        var window = new Window { Width = 1280, Height = 800, Opacity = 0, ShowInTaskbar = false, ShowActivated = false, Content = browse };
        window.Show();
        var host = (ViewerHostView)browse.FindName("ViewerHost");
        try
        {
            await browse.LoadRootsAsync([first, second]);
            await WaitUntil(() => state.Files.Count == 2 && monitor.Runs == 1 && browse.ViewerMetadata?.WordCount == 2);
            await Check("watcher insertion preserves selection text interaction and tree identity", async () =>
            {
                browse.ToggleTextInteraction();
                var folder = state.ActiveFolder; var selected = state.CurrentFile;
                state.Roots[0].IsExpanded = false;
                File.WriteAllText(Path.Combine(first, "0.txt"), "inserted");
                await monitor.Emit(new([Path.Combine(first, "0.txt")], [], true));
                Require(state.Files.Count == 3 && state.CurrentFilePosition == 2, "Insertion did not reconcile sorting/selection");
                Require(ReferenceEquals(selected, state.CurrentFile) && ReferenceEquals(folder, state.ActiveFolder), "Unchanged context replaced");
                Require(browse.IsTextInteraction && !state.Roots[0].IsExpanded, "Text interaction/collapse reset");
            });
            await Check("watcher refreshes existing nonselected thumbnail with unchanged metadata", async () =>
            {
                var path = Path.Combine(first, "preview.png"); File.Copy(Fixtures[".png"].FullPath, path);
                await monitor.Emit(new([path], [], true));
                var item = browse.CarouselItems.OfType<CarouselThumbnailItem>().Single(i => i.File.FullPath == path);
                await WaitUntil(() => item.Thumbnail is not null);
                var before = item.Thumbnail; var selected = state.CurrentFile;
                var timestamp = File.GetLastWriteTimeUtc(path);
                File.WriteAllBytes(path, File.ReadAllBytes(path)); File.SetLastWriteTimeUtc(path, timestamp);
                await monitor.Emit(new([path], [], false));
                var refreshed = browse.CarouselItems.OfType<CarouselThumbnailItem>().Single(i => i.File.FullPath == path);
                Require(ReferenceEquals(item, refreshed) && refreshed.Thumbnail is not null && !ReferenceEquals(before, refreshed.Thumbnail), "Existing thumbnail was not refreshed");
                Require(ReferenceEquals(selected, state.CurrentFile) && browse.IsTextInteraction, "Thumbnail refresh disturbed selected text");
                var unaffectedPreview = refreshed.Thumbnail;
                var unrelated = Path.Combine(first, "unrelated.txt"); File.WriteAllText(unrelated, "unrelated");
                await monitor.Emit(new([unrelated], [], true));
                Require(ReferenceEquals(unaffectedPreview, browse.CarouselItems.OfType<CarouselThumbnailItem>().Single(i => i.File.FullPath == path).Thumbnail), "Unrelated insertion flushed carousel previews");
                File.Delete(unrelated); await monitor.Emit(new([unrelated], [], true));
                File.Delete(path); await monitor.Emit(new([path], [], true));
            });
            await Check("watcher updates inactive-root hierarchy without another root visit", async () =>
            {
                var added = Path.Combine(second, "added"); Directory.CreateDirectory(added);
                File.WriteAllText(Path.Combine(added, "a.txt"), "a");
                await monitor.Emit(new([added], [], true));
                Require(state.Roots[1].Children.Single().Path == added, "Inactive root hierarchy missing");
                Require((await history.GetListsAsync()).Recent.All(r => r.OpenCount == 1), "Automatic reconciliation counted visits");
            });
            await Check("watcher changed hint reloads same-size same-timestamp text", async () =>
            {
                var file = state.CurrentFile!;
                File.WriteAllText(file.FullPath, "modified text"); File.SetLastWriteTimeUtc(file.FullPath, file.LastModifiedUtc);
                await monitor.Emit(new([file.FullPath], [], false));
                await WaitUntil(() => ((TextBox)host.FindName("TextViewer")).Text == "modified text");
            });
            await Check("watcher removal selects nearest valid prior position", async () =>
            {
                var path = state.CurrentFile!.FullPath; File.Delete(path);
                await monitor.Emit(new([path], [], true));
                Require(state.CurrentFile?.Name == "2.txt" && state.CurrentFilePosition == 2, "Nearest-index selection failed");
            });
            await Check("watcher waits for in-flight user command before applying batch", async () =>
            {
                var defer = (IDisposable)typeof(BrowseView).GetMethod("DeferReconciliation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browse, null)!;
                var path = Path.Combine(first, "3.txt"); File.WriteAllText(path, "three");
                var pending = monitor.Emit(new([path], [], true));
                try { await Task.Delay(150); Require(state.Files.Count == 2 && !pending.IsCompleted, "Reconciliation interrupted command"); }
                finally { defer.Dispose(); }
                await pending; Require(state.Files.Count == 3, "Deferred batch lost");
            });
            await Check("watcher overflow rescans roots and retains surviving selection", async () =>
            {
                var selected = state.CurrentFile!.FullPath;
                File.WriteAllText(Path.Combine(first, "4.txt"), "four");
                await monitor.Emit(new([], [first, second], true));
                Require(state.Files.Count == 4 && state.CurrentFile?.FullPath == selected, "Overflow recovery lost state");
            });
            await Check("watcher stops outside Browse and return reconciliation sees changes", async () =>
            {
                var runs = monitor.Runs;
                browse.SetViewerActive(false);
                Require(monitor.Token.IsCancellationRequested, "Leaving Browse retained watcher");
                File.WriteAllText(Path.Combine(first, "5.txt"), "five");
                Require(state.Files.Count == 4, "Inactive Browse changed");
                browse.SetViewerActive(true);
                await WaitUntil(() => monitor.Runs == runs + 1);
                await monitor.Emit(new([], [first, second], true));
                Require(state.Files.Count == 5, "Return reconciliation failed");
            });
            await Check("watcher removal of active nested folder falls back safely", async () =>
            {
                var child = state.Roots[1].Children.Single();
                await (Task)typeof(BrowseView).GetMethod("ActivateFolderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browse, [child, null, 0])!;
                var path = child.Path; Directory.Delete(path, true);
                await monitor.Emit(new([path], [], true));
                Require(state.ActiveFolder == state.Roots[0] && state.Roots[1].Children.Count == 0, "Removed folder left stale context");
            });
            if (Fixtures.TryGetValue(".wav", out var media))
                await Check("unrelated watcher changes retain playing native media; changed media reopens paused", async () =>
                {
                    File.Copy(media.FullPath, Path.Combine(first, "0.wav"));
                    await monitor.Emit(new([Path.Combine(first, "0.wav")], [], true));
                    var file = state.Files.Single(f => f.Name == "0.wav");
                    state.SetCurrentFile(file);
                    // Present after selection, as NavigationController does.
                    typeof(BrowseView).GetMethod("PresentSelectedFile", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browse, [file]);
                    await WaitUntil(() => browse.PlaybackState.Ready);
                    browse.SetPlaybackVolume(0); browse.TogglePlayback();
                    await WaitUntil(() => browse.PlaybackState.Playing);
                    var other = Path.Combine(first, "6.txt"); File.WriteAllText(other, "six");
                    await monitor.Emit(new([other], [], true));
                    Require(browse.PlaybackState.Playing, "Unrelated insertion interrupted media");
                    await monitor.Emit(new([file.FullPath], [], false));
                    await WaitUntil(() => browse.PlaybackState.Ready);
                    Require(!browse.PlaybackState.Playing, "Changed media autoplayed");
                });
            await Check("Gallery shares live watcher coverage without reloading inactive Browse or counting visits", async () =>
            {
                var runs = monitor.Runs; var token = monitor.Token;
                var visits = (await history.GetListsAsync()).Frequent.Sum(r => r.OpenCount);
                browse.SetViewingMode(ViewingMode.Gallery);
                await WaitUntil(() => Field(browse, "_gallery") is MediFiler3.Interop.WinUiGalleryIsland island && island.RealizedCount > 0);
                var prior = Field(host, "_currentFile");
                var path = Path.Combine(first, "gallery-added.txt"); File.WriteAllText(path, "gallery live update");
                await monitor.Emit(new([path], [], false));
                var gallery = (MediFiler3.Interop.WinUiGalleryIsland)Field(browse, "_gallery")!;
                await WaitUntil(() => ((Microsoft.UI.Xaml.Controls.GridView)Field(gallery, "_grid")!).Items.Count == state.Files.Count);
                Require(state.Files.Any(f => f.FullPath == path) && monitor.Runs == runs && !token.IsCancellationRequested, "Gallery lost or duplicated watcher coverage");
                Require(ReferenceEquals(Field(host, "_currentFile"), prior) && !(bool)Field(host, "_active")!, "Live Gallery updates loaded inactive Browse");
                Require((await history.GetListsAsync()).Frequent.Sum(r => r.OpenCount) == visits, "Mode/live update counted root visits");
                state.SetCurrentFile(state.Files.Single(f => f.FullPath == path));
                browse.SetViewingMode(ViewingMode.Browse);
                await WaitUntil(() => browse.ViewerMetadata?.WordCount == 3);
                Require(state.CurrentFile?.FullPath == path && monitor.Runs == runs, "Browse did not adopt requested Gallery context");
            });
            await Check("Retained Gallery previews refresh on same-metadata change hints", async () =>
            {
                browse.SetViewingMode(ViewingMode.Gallery);
                var path = Path.Combine(first, "0-gallery.png"); File.Copy(Fixtures[".png"].FullPath, path);
                await monitor.Emit(new([path], [], false));
                var gallery = (MediFiler3.Interop.WinUiGalleryIsland)Field(browse, "_gallery")!;
                var grid = (Microsoft.UI.Xaml.Controls.GridView)Field(gallery, "_grid")!;
                var file = state.Files.Single(f => f.FullPath == path);
                Microsoft.UI.Xaml.Controls.Image? Preview() =>
                    (grid.ContainerFromItem(file) as Microsoft.UI.Xaml.Controls.GridViewItem)?.ContentTemplateRoot is Microsoft.UI.Xaml.Controls.Grid root
                        ? (Microsoft.UI.Xaml.Controls.Image)root.Children[1] : null;
                await WaitUntil(() => Preview()?.Source is not null);
                var source = Preview()!.Source; var listing = grid.ItemsSource;
                var timestamp = File.GetLastWriteTimeUtc(path);
                File.WriteAllBytes(path, File.ReadAllBytes(path)); File.SetLastWriteTimeUtc(path, timestamp);
                await monitor.Emit(new([path], [], false));
                await WaitUntil(() => Preview()?.Source is not null && !ReferenceEquals(source, Preview()!.Source));
                Require(ReferenceEquals(listing, grid.ItemsSource), "Same-metadata thumbnail refresh replaced entire Gallery listing");
            });
            await Check("Gallery F5 reconciles external additions with Browse presentation still inactive", async () =>
            {
                browse.SetViewingMode(ViewingMode.Gallery);
                var path = Path.Combine(first, "gallery-refresh.txt"); File.WriteAllText(path, "refresh");
                await browse.RefreshAsync();
                await WaitUntil(() => state.Files.Any(f => f.FullPath == path));
                var gallery = (MediFiler3.Interop.WinUiGalleryIsland)Field(browse, "_gallery")!;
                await WaitUntil(() => ((Microsoft.UI.Xaml.Controls.GridView)Field(gallery, "_grid")!).Items.Count == state.Files.Count);
                Require(!(bool)Field(host, "_active")!, "F5 resumed Browse inside Gallery");
                browse.SetViewingMode(ViewingMode.Browse);
            });
            await Check("Settings shows indexed cache usage and clears only thumbnail artifacts", async () =>
            {
                await thumbnails.GetThumbnailPngAsync(Fixtures[".png"], 20, 20, default);
                var settings = new SettingsView(5)
                {
                    CacheUsageRequested = () => thumbnails.GetUsageAsync(),
                    ClearThumbnailCacheRequested = () => thumbnails.MaintainAsync(clear: true)
                };
                var settingsWindow = new Window { Width = 850, Height = 700, Opacity = 0, ShowInTaskbar = false, ShowActivated = false, Content = settings };
                settingsWindow.Show();
                try
                {
                    var settingsIsland = (MediFiler3.Interop.WinUiCarouselSettingsIsland)settings.FindName("CarouselSliderIsland");
                    await WaitUntil(() => Field(settingsIsland, "_cacheStatus") is not null);
                    var status = (Microsoft.UI.Xaml.Controls.TextBlock)Field(settingsIsland, "_cacheStatus")!;
                    await WaitUntil(() => status.Text.Contains("entries"));
                    Require((await thumbnails.GetUsageAsync()).EntryCount > 0, "Usage omitted cached entries");
                    var button = (Microsoft.UI.Xaml.Controls.Button)Field(settingsIsland, "_clearCache")!;
                    ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)
                        .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                    await WaitUntil(() => status.Text.StartsWith("0 entries retained") && button.IsEnabled);
                    Require((await thumbnails.GetUsageAsync()).EntryCount == 0 && File.Exists(Fixtures[".png"].FullPath), "Clear cache removed source or left indexed data");
                }
                finally { settingsWindow.Content = null; settingsWindow.Close(); }
            });
        }
        finally { browse.StopBackgroundFolderCounts(); window.Content = null; window.Close(); }
    }

    private sealed class ManualMonitor : IFolderChangeMonitor
    {
        private Func<FolderChangeBatch, CancellationToken, Task>? _callback;
        public CancellationToken Token { get; private set; }
        public int Runs;
        public async Task RunAsync(IReadOnlyList<string> roots, Func<string?> activeFolder,
            Func<FolderChangeBatch, CancellationToken, Task> reconcile, CancellationToken token)
        {
            _callback = reconcile; Token = token; Interlocked.Increment(ref Runs);
            try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
        public Task Emit(FolderChangeBatch batch) => Task.Run(() => _callback!(batch, Token));
    }
}
