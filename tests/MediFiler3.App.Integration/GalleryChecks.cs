using System.Reflection;
using System.Windows;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Persistence;
using MediFiler3.Domain.Files;
using MediFiler3.Interop;
using MediFiler3.Views;

internal static partial class Program
{
    private static async Task RunGalleryChecksAsync(MediFiler3.MainWindow window, NavigationSessionState state,
        BrowseView browse, WinUiToolbarIsland toolbar, ApplicationSettingsService settings, CountingThumbnails thumbnails, string directory)
    {
        var navigation = (WinUiNavigationIsland)window.FindName("NavigationIsland");
        WinUiGalleryIsland Gallery() => (WinUiGalleryIsland)Field(browse, "_gallery")!;
        Microsoft.UI.Xaml.Controls.GridView Grid() => (Microsoft.UI.Xaml.Controls.GridView)Field(Gallery(), "_grid")!;
        var host = (ViewerHostView)browse.FindName("ViewerHost");
        await RunThumbnailRenderingChecksAsync();
        await LoadRestartRootsAsync(window, [directory]);
        await WaitUntil(() => state.Files.Count == 80 && browse.CanZoom);
        await Check("sidebar Gallery preserves shared context and suspends Browse resources", async () =>
        {
            var roots = state.Roots.ToArray(); var folder = state.ActiveFolder; var current = state.CurrentFile;
            await WaitUntil(() => thumbnails.Requests.Any(r => r.Path == current!.FullPath));
            var carouselRequests = thumbnails.Requests.Count(r => r.Path == current!.FullPath);
            var nativeNavigation = (Microsoft.UI.Xaml.Controls.NavigationView)Field(navigation, "_navigationView")!;
            Require(nativeNavigation.MenuItems.OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>().Any(i => (string)i.Tag == "Gallery"), "Gallery missing from sidebar tabs");
            navigation.SelectDestination("Gallery");
            await WaitUntil(() => Field(browse, "_gallery") is WinUiGalleryIsland island && Field(island, "_grid") is not null && island.RealizedCount > 0);
            Require(browse.IsGallery && state.Roots.SequenceEqual(roots) && ReferenceEquals(folder, state.ActiveFolder) && ReferenceEquals(current, state.CurrentFile), "Mode switch lost shared context");
            Require(!(bool)Field(host, "_active")! && Field(host, "_media") is null, "Gallery kept Browse renderer/media active");
            Require(((FrameworkElement)browse.FindName("CarouselOverlay")).Visibility == Visibility.Collapsed, "Gallery displays Browse carousel");
            await WaitUntil(() => HasGalleryImage(Grid()));
            await WaitUntil(() => thumbnails.Requests.Count(r => r.Path == current!.FullPath) > carouselRequests);
            Require(thumbnails.Requests.Where(r => r.Path == current!.FullPath).All(r => r.Width == 0 && r.Height == 0), "Carousel and Gallery do not share platform-default thumbnail requests");
        });
        await Check("Gallery sliders independently resize rectangular thumbnails and persist", async () =>
        {
            var x = (Microsoft.UI.Xaml.Controls.Slider)Field(toolbar, "_galleryWidthSlider")!;
            var y = (Microsoft.UI.Xaml.Controls.Slider)Field(toolbar, "_galleryHeightSlider")!;
            Require(x.Minimum == 100 && x.Maximum == 400 && x.StepFrequency == 50 && y.StepFrequency == 50, "Gallery slider steps/range incorrect");
            var controls = (Microsoft.UI.Xaml.Controls.Grid)Field(toolbar, "_galleryControls")!;
            Require(controls.Children.OfType<Microsoft.UI.Xaml.Controls.TextBlock>().Select(t => t.Text).SequenceEqual(new[] { "Width", "Height" }) && x.MaxWidth >= 110 && y.MaxWidth >= 110,
                "Gallery slider labels/compact widths incorrect");
            x.Value = 250; y.Value = 100;
            await WaitUntil(() => settings.Current.GalleryThumbnailWidth == 250 && settings.Current.GalleryThumbnailHeight == 100);
            await WaitUntil(() => Grid().ItemsPanelRoot is Microsoft.UI.Xaml.Controls.ItemsWrapGrid panel && panel.ItemWidth == 252 && panel.ItemHeight == 102);
            Require(((Microsoft.UI.Xaml.Controls.Grid)Field(toolbar, "_galleryControls")!).Visibility == Microsoft.UI.Xaml.Visibility.Visible, "Gallery sliders hidden");
            await settings.FlushAsync();
        });
        await Check("Gallery retains its grid and decoded tiles across mode switches", async () =>
        {
            await WaitUntil(() => Grid().ContainerFromItem(state.Files[0]) is Microsoft.UI.Xaml.Controls.GridViewItem);
            var first = (Microsoft.UI.Xaml.Controls.GridViewItem)Grid().ContainerFromItem(state.Files[0]);
            var root = (Microsoft.UI.Xaml.Controls.Grid)first.ContentTemplateRoot;
            var image = (Microsoft.UI.Xaml.Controls.Image)root.Children[1];
            await WaitUntil(() => image.Source is not null);
            var source = image.Source; var grid = Grid(); var listing = grid.ItemsSource; var island = Gallery();
            navigation.SelectDestination("Browse");
            await WaitUntil(() => !browse.IsGallery);
            navigation.SelectDestination("Gallery");
            await WaitUntil(() => browse.IsGallery);
            Require(ReferenceEquals(island, Gallery()) && ReferenceEquals(grid, Grid()) && ReferenceEquals(listing, Grid().ItemsSource), "Tab switch rebuilt Gallery");
            Require(ReferenceEquals(source, image.Source), "Tab switch discarded an existing thumbnail");
        });
        await Check("Gallery reveals the current Browse file without replacing its listing", async () =>
        {
            var grid = Grid(); var listing = grid.ItemsSource;
            navigation.SelectDestination("Browse");
            var selected = state.Files[^1];
            ((NavigationController)Field(browse, "_navigationController")!).SelectFile(selected);
            navigation.SelectDestination("Gallery");
            await WaitUntil(() => Grid().ContainerFromItem(selected) is Microsoft.UI.Xaml.Controls.GridViewItem container &&
                container.TransformToVisual(Grid()).TransformPoint(new Windows.Foundation.Point()).Y >= 0 &&
                container.TransformToVisual(Grid()).TransformPoint(new Windows.Foundation.Point()).Y + container.ActualHeight <= Grid().ActualHeight + 2);
            Require(ReferenceEquals(listing, Grid().ItemsSource) && ReferenceEquals(Grid().SelectedItem, selected), "Current-file reveal reset the listing or selected wrong file");
        });
        await Check("Gallery retains previews through hidden listing changes and layout recycling", async () =>
        {
            var files = state.Files.ToArray(); var selected = files[0];
            Gallery().RevealCurrentFile(selected);
            await WaitUntil(() => Grid().ContainerFromItem(selected) is Microsoft.UI.Xaml.Controls.GridViewItem item && HasGalleryImage(item));
            Microsoft.UI.Xaml.Controls.Image Image() => (Microsoft.UI.Xaml.Controls.Image)((Microsoft.UI.Xaml.Controls.Grid)((Microsoft.UI.Xaml.Controls.GridViewItem)Grid().ContainerFromItem(selected)).ContentTemplateRoot).Children[1];
            var bitmap = Image().Source; var listing = Grid().ItemsSource; var island = Gallery();
            navigation.SelectDestination("Browse");
            await Task.Delay(150);
            state.ReplaceFiles(new[] { new FileEntry(System.IO.Path.Combine(directory, "-added.unknown"), "-added.unknown", 1, DateTime.UnixEpoch) }.Concat(files));
            state.SetCurrentFile(selected);
            var width = window.Width; window.Width = width + 100;
            await Task.Delay(150);
            navigation.SelectDestination("Gallery");
            await WaitUntil(() => Grid().Items.Count == 81 && Grid().ContainerFromItem(selected) is Microsoft.UI.Xaml.Controls.GridViewItem item && HasGalleryImage(item));
            Require(ReferenceEquals(island, Gallery()) && ReferenceEquals(listing, Grid().ItemsSource) && ReferenceEquals(bitmap, Image().Source), "A small inactive listing change reloaded surviving previews");
            state.ReplaceFiles(files); state.SetCurrentFile(selected); window.Width = width;
            await WaitUntil(() => Grid().Items.Count == 80);
        });
        await Check("Gallery centers complete rows and gives selected tiles a distinct border", async () =>
        {
            await Task.Delay(100);
            Require(Math.Abs(Grid().Padding.Left - Grid().Padding.Right - 26) < 1 && Grid().Padding.Right >= 2, "Gallery row padding is not centered in available space");
            var current = Grid().SelectedItem;
            await WaitUntil(() => Grid().ContainerFromItem(current) is Microsoft.UI.Xaml.Controls.GridViewItem);
            var item = (Microsoft.UI.Xaml.Controls.GridViewItem)Grid().ContainerFromItem(current);
            var highlight = (Microsoft.UI.Xaml.Controls.Grid)((Microsoft.UI.Xaml.Controls.Grid)item.ContentTemplateRoot).Children[3];
            var border = (Microsoft.UI.Xaml.Controls.Border)highlight.Children[0];
            Require(highlight.Visibility == Microsoft.UI.Xaml.Visibility.Visible && border.BorderThickness.Left == 5 && border.BorderBrush is Microsoft.UI.Xaml.Media.SolidColorBrush color && color.Color == Microsoft.UI.Colors.DeepPink && border.Child is null,
                "Selected tile lacks a visible distinct border");
            Require(highlight.Children.Count == 1, "Gallery selection still has a patterned overlay");
        });
        await Check("Gallery uses tight cropped tiles and hides gradient captions while hovered", async () =>
        {
            var container = (Microsoft.UI.Xaml.Controls.GridViewItem)Grid().ContainerFromItem(state.CurrentFile!);
            var root = (Microsoft.UI.Xaml.Controls.Grid)container.ContentTemplateRoot;
            var image = (Microsoft.UI.Xaml.Controls.Image)root.Children[1];
            var caption = (Microsoft.UI.Xaml.Controls.Border)root.Children[2];
            Require(image.Stretch == Microsoft.UI.Xaml.Media.Stretch.UniformToFill && caption.Visibility == Microsoft.UI.Xaml.Visibility.Visible, "Default tile does not crop/show caption");
            Require(caption.Background is Microsoft.UI.Xaml.Media.LinearGradientBrush gradient && gradient.GradientStops[0].Color.A == 0, "Caption lacks transparent gradient");
            Require(root.Height == 100 && root.Width == 250 && container.Padding.Left == 0 && container.Margin.Left == 1, "Tiles reserve caption space or excessive spacing");
            await WaitUntil(() => image.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage);
            var bitmap = (Microsoft.UI.Xaml.Media.Imaging.BitmapImage)image.Source;
            var scale = Math.Max(root.Width, root.Height) / Math.Min(bitmap.PixelWidth, bitmap.PixelHeight);
            Require(Math.Abs(image.Width - bitmap.PixelWidth * scale) < 0.01 && Math.Abs(image.Height - bitmap.PixelHeight * scale) < 0.01 &&
                image.HorizontalAlignment == Microsoft.UI.Xaml.HorizontalAlignment.Center && image.VerticalAlignment == Microsoft.UI.Xaml.VerticalAlignment.Center &&
                root.Clip is Microsoft.UI.Xaml.Media.RectangleGeometry clip && clip.Rect.Width == root.Width && clip.Rect.Height == root.Height,
                "Gallery framing does not match the carousel's centered square crop");
            await Task.Delay(50);
            var center = image.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(image.ActualWidth / 2, image.ActualHeight / 2));
            Require(Math.Abs(center.X - root.Width / 2) < 1 && Math.Abs(center.Y - root.Height / 2) < 1, "Gallery crop is not centered on the image");
            var hover = typeof(WinUiGalleryIsland).GetMethod("SetTileHover", BindingFlags.Static | BindingFlags.NonPublic)!;
            hover.Invoke(null, [root, true]);
            Require(image.Stretch == Microsoft.UI.Xaml.Media.Stretch.Uniform && caption.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed, "Hovered tile obscures/crops image");
            Require(image.Width == root.Width && image.Height == root.Height, "Hover did not restore the full aspect-ratio viewport");
            hover.Invoke(null, [root, false]);
            Require(image.Stretch == Microsoft.UI.Xaml.Media.Stretch.UniformToFill && caption.Visibility == Microsoft.UI.Xaml.Visibility.Visible, "Hover exit did not restore crop/caption");
            await Task.CompletedTask;
        });
        await Check("Pinned tree reflows Gallery tiles and unpin restores available width", async () =>
        {
            browse.SetFolderPinned(true);
            await Task.Delay(220);
            Require(Grid().Padding.Left >= 302, "Pinned tree covers Gallery tiles");
            var visible = (Microsoft.UI.Xaml.Controls.GridViewItem)Grid().ContainerFromItem(state.CurrentFile!);
            Require(visible is null || visible.TransformToVisual(Grid()).TransformPoint(new Windows.Foundation.Point()).X >= 300, "Tiles remain under pinned tree");
            browse.SetFolderPinned(false);
            Require(Math.Abs(Grid().Padding.Left - Grid().Padding.Right - 26) < 1, "Unpin retained reserved tree space");
        });
        await RunGalleryInputChecksAsync(state, browse, toolbar, navigation, Gallery);
        var performance = new Dictionary<string, object>
        {
            ["Scope"] = "Single-run Gallery scale smoke check; generated nonexistent paths, not an image-decoding or real-library benchmark",
            ["CapturedUtc"] = DateTimeOffset.UtcNow,
            ["Runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["OS"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ["ProcessorCount"] = Environment.ProcessorCount,
            ["DpiScale"] = System.Windows.Media.VisualTreeHelper.GetDpi(browse).DpiScaleX,
            ["ViewportWidth"] = Grid().ActualWidth, ["ViewportHeight"] = Grid().ActualHeight,
            ["FileCount"] = 10000
        };
        await Check("Gallery virtualizes a large file list and releases realized work on tab change", async () =>
        {
            var files = state.Files.ToArray();
            var publish = System.Diagnostics.Stopwatch.StartNew();
            state.ReplaceFiles(Enumerable.Range(0, 10000).Select(i => new FileEntry(System.IO.Path.Combine(directory, $"large-{i}.unknown"), $"large-{i}.unknown", 1, DateTime.UnixEpoch)));
            await WaitUntil(() => Grid().Items.Count == 10000 && Gallery().RealizedCount > 0);
            performance["PublishAndInitialRealizationMs"] = publish.Elapsed.TotalMilliseconds;
            performance["InitialRealizedTiles"] = Gallery().RealizedCount;
            Require(Gallery().RealizedCount < 1000, "Gallery realized the full collection");
            var selection = System.Diagnostics.Stopwatch.StartNew();
            typeof(WinUiGalleryIsland).GetMethod("HandleSelectionKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Gallery(), [Windows.System.VirtualKey.A, true]);
            Require(state.GallerySelection.Count == 10000 && Grid().SelectedItems.Count == 10000 && Gallery().RealizedCount < 1000,
                "Select All lost targets or realized the full collection");
            performance["SelectAllMs"] = selection.Elapsed.TotalMilliseconds;
            performance["SelectAllRealizedTiles"] = Gallery().RealizedCount;
            selection.Restart();
            typeof(WinUiGalleryIsland).GetMethod("HandleSelectionKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Gallery(), [Windows.System.VirtualKey.Escape, false]);
            performance["ClearSelectionMs"] = selection.Elapsed.TotalMilliseconds;
            var items = Grid().Items;
            Grid().ScrollIntoView(items[9000]);
            await Task.Delay(200);
            Require(Gallery().RealizedCount is > 0 and < 1000, "Scrolling retained an unbounded container set");
            Require(((System.Collections.IDictionary)Field(Gallery(), "_decodedIndex")!).Count <= 512 && (long)Field(Gallery(), "_decodedBytes")! <= 128L * 1024 * 1024,
                "Decoded thumbnail retention is not bounded");
            performance["RealizedTilesAfterDistantScroll"] = Gallery().RealizedCount;
            performance["DecodedCacheEntries"] = ((System.Collections.IDictionary)Field(Gallery(), "_decodedIndex")!).Count;
            performance["DecodedCacheBytes"] = (long)Field(Gallery(), "_decodedBytes")!;
            navigation.SelectDestination("Settings");
            await WaitUntil(() => !(bool)Field(Gallery(), "_active")!);
            var calls = thumbnails.Calls; await Task.Delay(100);
            Require(thumbnails.Calls == calls, "Inactive Gallery keeps scheduling thumbnail work");
            performance["ThumbnailRequestsWhileInactive100ms"] = thumbnails.Calls - calls;
            state.ReplaceFiles(files);
            navigation.SelectDestination("Gallery");
            await WaitUntil(() => Field(browse, "_gallery") is WinUiGalleryIsland island && Field(island, "_grid") is Microsoft.UI.Xaml.Controls.GridView { IsLoaded: true, Items.Count: 80 });
            Require(Grid().Items.Count == 80, "Gallery did not adopt the requested shared context");
        });
        await Check("Gallery defers retained-list replacements until native reattachment and survives repeated large returns", async () =>
        {
            var files = state.Files.ToArray();
            var source = Grid().ItemsSource;
            var large = Enumerable.Range(0, 10000).Select(i => new FileEntry(System.IO.Path.Combine(directory, $"return-{i}.unknown"), $"return-{i}.unknown", 1, DateTime.UnixEpoch)).ToArray();
            var returns = System.Diagnostics.Stopwatch.StartNew();
            var resets = 0; var individualChanges = 0;
            var notifications = (System.Collections.Specialized.INotifyCollectionChanged)source;
            System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, args) =>
            {
                if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++;
                else individualChanges++;
            };
            notifications.CollectionChanged += changed;
            try
            {
                for (var round = 0; round < 2; round++)
                {
                    navigation.SelectDestination("Settings"); await Task.Delay(80);
                    state.ReplaceFiles(large); navigation.SelectDestination("Gallery");
                    Require(Grid().Items.Count == files.Length, "Retained list mutated inside the native sidebar callback");
                    await WaitUntil(() => Grid().IsLoaded && Grid().Items.Count == large.Length && Gallery().RealizedCount > 0);
                    typeof(WinUiGalleryIsland).GetMethod("HandleSelectionKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Gallery(), [Windows.System.VirtualKey.A, true]);
                    typeof(WinUiGalleryIsland).GetMethod("HandleSelectionKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Gallery(), [Windows.System.VirtualKey.Escape, false]);
                    Grid().ScrollIntoView(Grid().Items[9000]); await Task.Delay(80);
                    navigation.SelectDestination("Home"); await Task.Delay(80);
                    state.ReplaceFiles(files); navigation.SelectDestination("Gallery");
                    Require(Grid().Items.Count == large.Length, "Large retained list mutated before the sidebar callback returned");
                    await WaitUntil(() => Grid().IsLoaded && Grid().Items.Count == files.Length && Gallery().RealizedCount > 0);
                    Require(ReferenceEquals(source, Grid().ItemsSource), "Large returns replaced the native source");
                }
                Require(resets == 4 && individualChanges == 0, "Disjoint listing replacement exposed intermediate native collection indices");
                performance["DisjointResetCount"] = resets;
                performance["DisjointIndividualNotifications"] = individualChanges;
                performance["TwoLargeReturnRoundsMsIncludingFixedWaits"] = returns.Elapsed.TotalMilliseconds;
                performance["RetainedItemsSource"] = ReferenceEquals(source, Grid().ItemsSource);
                var json = System.Text.Json.JsonSerializer.Serialize(performance, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(System.IO.Path.Combine(Root, "phase5-performance.json"), json);
                Console.WriteLine("Phase 5 scale evidence: " + System.IO.Path.Combine(Root, "phase5-performance.json"));
            }
            finally { notifications.CollectionChanged -= changed; }
        });
        await Check("Gallery selection hands current context back to Browse on request", async () =>
        {
            var selected = state.Files[20];
            Grid().SelectedItem = selected;
            await WaitUntil(() => ReferenceEquals(state.CurrentFile, selected));
            Require(!(bool)Field(host, "_active")!, "Selecting Gallery file loaded Browse");
            typeof(WinUiGalleryIsland).GetMethod("RequestOpen", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Gallery(), [selected]);
            await WaitUntil(() => !browse.IsGallery && browse.CanZoom && ReferenceEquals(Field(host, "_currentFile"), selected));
            Require(state.CurrentFilePosition == 21 && state.GallerySelection.Count == 0, "Browse handoff lost position or retained Gallery selection");
            Require(((Microsoft.UI.Xaml.Controls.Grid)Field(toolbar, "_galleryControls")!).Visibility == Microsoft.UI.Xaml.Visibility.Collapsed, "Sliders remain in Browse");
        });
        await Check("Carousel double click opens Gallery and highlights the clicked file", async () =>
        {
            var file = state.Files[22];
            typeof(BrowseView).GetMethod("HandleCarouselTileClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browse, [file, 2]);
            await WaitUntil(() => browse.IsGallery && ReferenceEquals(Grid().SelectedItem, file) && Grid().ContainerFromItem(file) is Microsoft.UI.Xaml.Controls.GridViewItem);
            var root = (Microsoft.UI.Xaml.Controls.Grid)((Microsoft.UI.Xaml.Controls.GridViewItem)Grid().ContainerFromItem(file)).ContentTemplateRoot;
            Require(ReferenceEquals(state.CurrentFile, file) && root.Children[3].Visibility == Microsoft.UI.Xaml.Visibility.Visible, "Carousel target is not highlighted in Gallery");
        });
        await Check("Unpinned tree has one full-width reveal area spanning sidebar and tree", async () =>
        {
            browse.SetFolderPinned(false);
            var probe = browse.FolderHoverPositionProbe;
            Point? pointer = null;
            void Hover(Point? point) { pointer = point; browse.UpdateShellFolderHover(point); }
            try
            {
                browse.FolderHoverPositionProbe = () => pointer;
                var overlay = (FrameworkElement)browse.FindName("FolderOverlay");
                var bounds = browse.FolderRevealBounds;
                Require(bounds.Left == -56 && bounds.Right == overlay.Width && bounds.Top == 0 && bounds.Bottom == browse.ActualHeight,
                    "Reveal area does not cover sidebar through the pinned tree width");
                Require(browse.FindName("LeftRevealTarget") is null, "Separate legacy edge hitbox remains");
                Hover(new Point(overlay.Width + 1, 200));
                Require(!(bool)Field(browse, "_folderOverlayVisible")!, "Tree revealed beyond its pinned width");
                Hover(new Point(overlay.Width - 1, 200));
                Require((bool)Field(browse, "_folderOverlayVisible")!, "Far edge of hidden tree did not reveal it");
                Hover(new Point(-30, 200));
                Require((bool)Field(browse, "_folderOverlayVisible")!, "Sidebar hover did not reveal tree");
                typeof(BrowseView).GetMethod("OnBrowseSurfaceMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browse,
                    [browse, new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)]);
                Require((bool)Field(browse, "_folderOverlayVisible")!, "Crossing into sidebar closed tree");
                Hover(new Point(150, 200));
                Require((bool)Field(browse, "_folderOverlayVisible")!, "Entering tree from sidebar closed it");
                Hover(new Point(overlay.Width + 1, 200));
                Require(!(bool)Field(browse, "_folderOverlayVisible")!, "Tree did not hide outside combined hover area");
                Hover(new Point(5, 200));
                Require((bool)Field(browse, "_folderOverlayVisible")!, "Left edge is disconnected from the unified reveal area");
                Hover(new Point(150, -1));
                Require(!(bool)Field(browse, "_folderOverlayVisible")!, "Reveal extends above the tree");
                Hover(new Point(150, browse.ActualHeight + 1));
                Require(!(bool)Field(browse, "_folderOverlayVisible")!, "Reveal extends below the tree");
                browse.SetFolderPinned(true); Hover(null);
                Require((bool)Field(browse, "_folderOverlayVisible")!, "Leaving hover hid a pinned tree");
                browse.SetFolderPinned(false); Hover(null);
                await WaitUntil(() =>
                {
                    typeof(BrowseView).GetMethod("OnVideoSurfaceRendering", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browse, [null, EventArgs.Empty]);
                    return GalleryNativeContains(Gallery(), 5, 200) && GalleryNativeContains(Gallery(), overlay.Width - 1, 200);
                });
            }
            finally { browse.FolderHoverPositionProbe = probe; }
        });
        await Check("Gallery fullscreen clipping preserves tree and shell reveal regions", async () =>
        {
            navigation.SelectDestination("Gallery");
            await WaitUntil(() => Field(browse, "_gallery") is WinUiGalleryIsland island && island.RealizedCount > 0);
            window.ToggleFullscreen(); ((System.Windows.Threading.DispatcherTimer)Field(window, "_fullscreenTimer")!).Stop();
            typeof(MediFiler3.MainWindow).GetMethod("SetFullscreenChrome", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [true, true]);
            await WaitUntil(() =>
            {
                typeof(BrowseView).GetMethod("OnVideoSurfaceRendering", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browse, [null, EventArgs.Empty]);
                var regions = (Rect[])Field(Gallery(), "_exclusions")!;
                return regions.Any(r => r.Width >= 300 && r.Height > 100) && regions.Any(r => r.Height == 48);
            });
            var cuts = (Rect[])Field(Gallery(), "_exclusions")!;
            Require(cuts.Any(r => r.Width >= 300 && r.Height > 100) && cuts.Any(r => r.Height == 48), "Gallery covers tree or shell reveal");
            Require(Field(Gallery(), "_regionKey") is not null, "Gallery HWND clipping was not applied");
            window.ToggleFullscreen(); navigation.SelectDestination("Browse");
        });
    }

    private static bool HasGalleryImage(Microsoft.UI.Xaml.DependencyObject element)
    {
        if (element is Microsoft.UI.Xaml.Controls.Image { Source: not null }) return true;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element); i++)
            if (HasGalleryImage(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i))) return true;
        return false;
    }
}
