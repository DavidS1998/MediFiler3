using System.IO;
using System.Windows;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Persistence;
using MediFiler3.Infrastructure.Files;
using MediFiler3.Infrastructure.Folders;
using MediFiler3.Infrastructure.Persistence;
using MediFiler3.Interop;
using MediFiler3.Services;
using MediFiler3.Views;
using Native = Microsoft.UI.Xaml;

internal static partial class Program
{
    private static async Task RunHomeChecksAsync()
    {
        var alpha = Path.Combine(Root, "home-alpha");
        var beta = Path.Combine(Root, "home-beta");
        var missing = Path.Combine(Root, "home-missing");
        var removed = Path.Combine(Root, "home-removed");
        var located = Path.Combine(Root, "home-located");
        foreach (var path in new[] { alpha, beta, missing, removed, located }) Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(alpha, "test.txt"), "Home root open");
        var db = Path.Combine(Root, "home.db");
        var availability = new FileSystemRootFolderAvailabilityService();
        var history = new RootFolderHistoryService(new SqliteApplicationStateStore(new SqliteDatabase(db)), availability);
        await history.RecordOpenedRootsAsync([alpha, beta, missing, removed]);
        await history.SetFavoriteAsync(beta, true);
        Directory.Delete(missing); Directory.Delete(removed);
        var home = new HomeView(history, availability);
        var browser = new BrowseView(new FileSystemFolderTreeService(), new FileSystemFileListingService(), new NavigationSessionState(),
            new WicImageDecoder(Files), NativeThumbnails, null!, new FileSystemTextContentService(Files), new External(), new Defaults(),
            rootHistory: history);
        string? opened = null;
        home.OpenRootRequested = async path => { opened = path; await browser.LoadRootsAsync([path]); };
        string? chosenLocation = located;
        home.LocateFolderRequested = _ => Task.FromResult<string?>(chosenLocation);
        var window = new Window { Width = 1280, Height = 800, Opacity = 0, ShowInTaskbar = false,
            ShowActivated = false, Content = home };
        window.Show();
        var island = (WinUiHomeIsland)home.FindName("HomeIsland");
        RootFolderLists Lists() => (RootFolderLists)Field(island, "_lists")!;
        int RecentIndex(string path) => Lists().Recent.ToList().FindIndex(r => r.Path == path);
        async Task Settled() { await WaitUntil(() => !(bool)Field(home, "_busy")!); await home.RefreshAsync(); await Task.Delay(50); }
        try
        {
            await Check("Home native cards use responsive spacing and publish saved lists", async () =>
            {
                await home.RefreshAsync();
                await WaitUntil(() => Field(island, "_cards") is not null && Lists().Recent.Count == 4);
                var cards = (Native.Controls.Grid)Field(island, "_cards")!;
                Require(cards.Children.Count == 3 && cards.ColumnDefinitions.Count == 3, "Three native cards missing");
                Require(cards.ColumnSpacing == 16 && cards.RowSpacing == 16, "Card spacing");
                var card = (Native.Controls.Border)cards.Children[0];
                Require(card.Padding.Left == 16 && ((Native.Controls.StackPanel)card.Child).Spacing == 12, "Card content spacing");
                var rootSurface = (Native.Controls.Grid)Field(island, "_root")!;
                var header = (Native.FrameworkElement)rootSurface.Children[0];
                rootSurface.UpdateLayout();
                await WaitUntil(() => header.ActualHeight > 0 && card.ActualHeight > 0);
                Require(!((Native.Controls.StackPanel)header).Children.OfType<Native.Controls.TextBlock>().Any(t => t.Text.StartsWith("Open a root folder")), "Redundant subtitle remains");
                var headerOrigin = header.TransformToVisual(rootSurface).TransformPoint(new Windows.Foundation.Point());
                var cardsOrigin = cards.TransformToVisual(rootSurface).TransformPoint(new Windows.Foundation.Point());
                Require(header.ActualHeight > 0 && headerOrigin.Y >= 0 && cardsOrigin.Y > headerOrigin.Y + header.ActualHeight,
                    "Home header is hidden or overlaps cards");
                var capture = new Native.Media.Imaging.RenderTargetBitmap();
                await capture.RenderAsync((Native.UIElement)Field(island, "_root")!);
                var buffer = await capture.GetPixelsAsync();
                var pixels = new byte[buffer.Length];
                using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
                var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(capture.PixelWidth, capture.PixelHeight, 96, 96,
                    System.Windows.Media.PixelFormats.Bgra32, null, pixels, capture.PixelWidth * 4);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(Root, "home-cards.png"))) encoder.Save(file);
                window.Width = 700; await Task.Delay(100);
                Require(cards.ColumnDefinitions.Count == 1, "Cards did not stack at narrow widths");
                window.Width = 1280; await Task.Delay(100);
                Require(cards.ColumnDefinitions.Count == 3, "Cards did not restore columns");
                foreach (var width in new[] { 1599d, 1664d, 1800d, 2200d, 3000d, 1100d, 900d, 1280d })
                {
                    window.Width = width; await Task.Delay(75); rootSurface.UpdateLayout();
                    var centered = cards.TransformToVisual(rootSurface).TransformPoint(new Windows.Foundation.Point());
                    Require(Math.Abs(centered.X + cards.ActualWidth / 2 - rootSurface.ActualWidth / 2) <= 12,
                        $"Cards are not centered at width {width}");
                    foreach (var entry in cards.Children.Cast<Native.FrameworkElement>())
                    {
                        var point = entry.TransformToVisual(rootSurface).TransformPoint(new Windows.Foundation.Point());
                        Require(point.X >= 0 && point.X + entry.ActualWidth <= rootSurface.ActualWidth + 1,
                            $"Card exceeds viewport at width {width}: {point.X} + {entry.ActualWidth} > {rootSurface.ActualWidth}");
                    }
                }
                var open = HomeControl<Native.Controls.Button>(island, "OpenRecent0");
                var details = (Native.Controls.StackPanel)open.Content;
                Require(!details.Children.OfType<Native.Controls.TextBlock>().Any(t => t.Text == Lists().Recent[0].Path),
                    "Full path should only appear in the tooltip");
                Require(Native.Controls.ToolTipService.GetToolTip(open).ToString()!.Contains(Lists().Recent[0].Path) &&
                    Native.Controls.ToolTipService.GetToolTip(open).ToString()!.Contains("Last visited:"), "Hover details missing");
                var menu = (Native.Controls.MenuFlyout)HomeControl<Native.Controls.Button>(island, "MenuRecent0").Flyout;
                Require(menu.Items.Count == 2 && ((Native.Controls.MenuFlyoutItem)menu.Items[1]).Text == "Remove entry",
                    "Overflow should have a single Remove entry action");
            });
            await Check("Home favorite stars synchronize all three lists and alphabetize Favorites", async () =>
            {
                var star = HomeControl<Native.Controls.Primitives.ToggleButton>(island, "StarRecent" + RecentIndex(alpha));
                var toggle = (Native.Automation.Provider.IToggleProvider)new Native.Automation.Peers.ToggleButtonAutomationPeer(star)
                    .GetPattern(Native.Automation.Peers.PatternInterface.Toggle);
                toggle.Toggle();
                await WaitUntil(() => Lists().Favorites.Count == 2);
                await Settled();
                Require(Lists().Favorites[0].Path == alpha && Lists().Favorites[1].Path == beta, "Favorites ordering");
                Require(Lists().Recent.Single(r => r.Path == alpha).Favorite && Lists().Frequent.Single(r => r.Path == alpha).Favorite,
                    "Shared favorite state missing");
                foreach (var section in new[] { "Recent", "Frequent", "Favorites" })
                {
                    var items = section == "Recent" ? Lists().Recent : section == "Frequent" ? Lists().Frequent : Lists().Favorites;
                    var index = items.ToList().FindIndex(r => r.Path == alpha);
                    Require(HomeControl<Native.Controls.Primitives.ToggleButton>(island, "Star" + section + index).IsChecked == true,
                        "Native star disagrees in " + section);
                }
            });
            await Check("Home opening uses Browse root publication and increments existing history once", async () =>
            {
                InvokeHome(island, "OpenRecent" + RecentIndex(alpha));
                await WaitUntil(() => opened == alpha && Lists().Recent.Single(r => r.Path == alpha).OpenCount == 2);
                await Settled();
                Require(Lists().Recent[0].Path == alpha && Lists().Frequent[0].Path == alpha, "Home open did not update sorting");
                Require(Lists().Favorites.Single(r => r.Path == alpha).Favorite, "Opening lost favorite state");
            });
            await Check("Home unstar from Favorites updates other lists and can be restored", async () =>
            {
                var index = Lists().Favorites.ToList().FindIndex(r => r.Path == beta);
                var star = HomeControl<Native.Controls.Primitives.ToggleButton>(island, "StarFavorites" + index);
                ((Native.Automation.Provider.IToggleProvider)new Native.Automation.Peers.ToggleButtonAutomationPeer(star)
                    .GetPattern(Native.Automation.Peers.PatternInterface.Toggle)).Toggle();
                await WaitUntil(() => Lists().Favorites.Count == 1); await Settled();
                Require(!Lists().Recent.Single(r => r.Path == beta).Favorite && !Lists().Frequent.Single(r => r.Path == beta).Favorite,
                    "Unstar did not synchronize");
                var frequentIndex = Lists().Frequent.ToList().FindIndex(r => r.Path == beta);
                star = HomeControl<Native.Controls.Primitives.ToggleButton>(island, "StarFrequent" + frequentIndex);
                ((Native.Automation.Provider.IToggleProvider)new Native.Automation.Peers.ToggleButtonAutomationPeer(star)
                    .GetPattern(Native.Automation.Peers.PatternInterface.Toggle)).Toggle();
                await WaitUntil(() => Lists().Favorites.Count == 2); await Settled();
            });
            await Check("Home missing roots expose only Locate and Remove", async () =>
            {
                var index = RecentIndex(missing);
                var locate = HomeControl<Native.Controls.Button>(island, "LocateRecent" + index);
                var actions = (Native.Controls.StackPanel)locate.Parent;
                Require(actions.Visibility == Native.Visibility.Visible && actions.Children.Count == 2,
                    "Missing actions should contain only Locate and Remove");
                await Settled();
                Require(Lists().Recent.Count == 4 && Lists().Frequent.Count == 4, "Availability changed history");
            });
            await Check("Home Locate handles cancellation collision and successful history relocation", async () =>
            {
                chosenLocation = null;
                InvokeHomeMenu(island, "MenuRecent" + RecentIndex(missing), 0);
                await Task.Delay(100); await Settled();
                Require(Lists().Recent.Any(r => r.Path == missing), "Picker cancellation changed history");
                chosenLocation = alpha;
                InvokeHomeMenu(island, "MenuRecent" + RecentIndex(missing), 0);
                await WaitUntil(() => (bool)Field(island, "_error")!); await Settled();
                Require(Lists().Recent.Count == 4, "Locate collision changed history");
                var before = Lists().Recent.Single(r => r.Path == missing);
                chosenLocation = located;
                InvokeHomeMenu(island, "MenuRecent" + RecentIndex(missing), 0);
                await WaitUntil(() => Lists().Recent.Any(r => r.Path == located)); await Settled();
                Require(Lists().Recent.Single(r => r.Path == located) == before with { Path = located }, "Locate lost history or added a visit");
            });
            await Check("Home Remove entry deletes from all lists without touching folders", async () =>
            {
                InvokeHome(island, "RemoveRecent" + RecentIndex(removed));
                await WaitUntil(() => !Lists().Recent.Any(r => r.Path == removed)); await Settled();
                InvokeHomeMenu(island, "MenuRecent" + RecentIndex(alpha), 1);
                await WaitUntil(() => !Lists().Recent.Any(r => r.Path == alpha)); await Settled();
                Require(!Lists().Frequent.Any(r => r.Path == alpha) && !Lists().Favorites.Any(r => r.Path == alpha),
                    "Remove entry retained frequency/favorites");
                Require(Directory.Exists(alpha) && File.Exists(Path.Combine(alpha, "test.txt")), "Home action touched source files");
            });
            await Check("Home native surface reattaches and persisted favorites survive reopening", async () =>
            {
                window.Content = null; await Task.Delay(50); window.Content = home;
                await Settled();
                Require(Field(island, "_root") is not null && Lists().Favorites.Count == 1, "Home did not rebuild on return");
                var reopened = new RootFolderHistoryService(new SqliteApplicationStateStore(new SqliteDatabase(db)), availability);
                var persisted = await reopened.GetListsAsync();
                Require(persisted.Favorites.Count == 1 && persisted.Favorites[0].Path == beta && !persisted.Frequent.Any(r => r.Path == alpha),
                    "Home state did not persist");
            });
            await Check("Home empty library keeps all cards and a working Add folders action", async () =>
            {
                var emptyHistory = new RootFolderHistoryService(new SqliteApplicationStateStore(
                    new SqliteDatabase(Path.Combine(Root, "home-empty.db"))), availability);
                var emptyHome = new HomeView(emptyHistory, availability);
                var requests = 0;
                emptyHome.AddFoldersRequested += () => requests++;
                window.Content = emptyHome;
                try
                {
                    await emptyHome.RefreshAsync(); await Task.Delay(50);
                    var emptyIsland = (WinUiHomeIsland)emptyHome.FindName("HomeIsland");
                    Require(((RootFolderLists)Field(emptyIsland, "_lists")!).Recent.Count == 0, "Empty library has phantom entries");
                    Require(((Native.Controls.Grid)Field(emptyIsland, "_cards")!).Children.Count == 3, "Empty cards missing");
                    InvokeHome(emptyIsland, "AddHomeFolders");
                    await WaitUntil(() => requests == 1);
                }
                finally { emptyHome.Stop(); window.Content = home; }
            });
            await Check("Home renders before slow availability and rejects obsolete probe results", async () =>
            {
                var delayed = new DelayedHomeAvailability();
                var slowHistory = new RootFolderHistoryService(new SqliteApplicationStateStore(
                    new SqliteDatabase(Path.Combine(Root, "home-slow.db"))), delayed);
                await slowHistory.RecordOpenedRootsAsync([alpha]);
                var slowHome = new HomeView(slowHistory, delayed);
                window.Content = slowHome;
                try
                {
                    var slowIsland = (WinUiHomeIsland)slowHome.FindName("HomeIsland");
                    await WaitUntil(() => delayed.Started && ((RootFolderLists)Field(slowIsland, "_lists")!).Recent.Count == 1);
                    Require(!delayed.Pending.Task.IsCompleted, "Availability did not remain pending");
                    await slowHistory.RemoveAsync(alpha);
                    await WaitUntil(() => ((RootFolderLists)Field(slowIsland, "_lists")!).Recent.Count == 0);
                    delayed.Pending.SetResult(RootFolderAvailability.Missing);
                    await Task.Delay(100);
                    Require(!((Dictionary<string, RootFolderStatus>)Field(slowIsland, "_statuses")!).ContainsKey(alpha),
                        "Obsolete availability was applied to the new context");
                }
                finally { delayed.Pending.TrySetResult(RootFolderAvailability.Missing); slowHome.Stop(); window.Content = home; }
            });
        }
        finally { home.Stop(); browser.StopBackgroundFolderCounts(); window.Content = null; window.Close(); }
    }

    private sealed class DelayedHomeAvailability : IRootFolderAvailabilityService
    {
        public bool Started;
        public TaskCompletionSource<RootFolderAvailability> Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<RootFolderAvailability> CheckAsync(string path, CancellationToken cancellationToken = default)
        { Started = true; return Pending.Task; }
    }

    private static T HomeControl<T>(WinUiHomeIsland island, string name) where T : Native.FrameworkElement
    {
        var pending = new Stack<Native.DependencyObject>();
        pending.Push((Native.DependencyObject)Field(island, "_root")!);
        while (pending.TryPop(out var element))
        {
            if (element is T control && control.Name == name) return control;
            for (var index = 0; index < Native.Media.VisualTreeHelper.GetChildrenCount(element); index++)
                pending.Push(Native.Media.VisualTreeHelper.GetChild(element, index));
        }
        throw new InvalidOperationException("Home control missing: " + name);
    }
    private static void InvokeHome(WinUiHomeIsland island, string name)
    {
        var button = HomeControl<Native.Controls.Button>(island, name);
        ((Native.Automation.Provider.IInvokeProvider)new Native.Automation.Peers.ButtonAutomationPeer(button)
            .GetPattern(Native.Automation.Peers.PatternInterface.Invoke)).Invoke();
    }
    private static void InvokeHomeMenu(WinUiHomeIsland island, string name, int index)
    {
        var button = HomeControl<Native.Controls.Button>(island, name);
        var menu = (Native.Controls.MenuFlyout)button.Flyout;
        var item = (Native.Controls.MenuFlyoutItem)menu.Items[index];
        ((Native.Automation.Provider.IInvokeProvider)new Native.Automation.Peers.MenuFlyoutItemAutomationPeer(item)
            .GetPattern(Native.Automation.Peers.PatternInterface.Invoke)).Invoke();
    }
}
