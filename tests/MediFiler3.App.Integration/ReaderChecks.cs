using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
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
    private static async Task RunReaderChecksAsync()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Root, "reader")).FullName;
        for (var i = 1; i <= 12; i++) File.Copy(Fixtures[".png"].FullPath, Path.Combine(directory, $"page {i}.png"));
        File.WriteAllText(Path.Combine(directory, "z-note.txt"), "not a reader page");
        File.WriteAllText(Path.Combine(directory, "z-video.mp4"), "not a reader page");
        var database = new SqliteDatabase(Path.Combine(Root, "reader.db")); var store = new SqliteApplicationStateStore(database);
        var settings = new ApplicationSettingsService(store); await settings.InitializeAsync();
        var layout = new WindowLayoutService(store); await layout.InitializeAsync();
        var journal = new SqliteFileOperationJournal(database); var identities = new WindowsFileIdentityProvider();
        var backups = new FileSystemOperationBackupStore(journal, identities, Path.Combine(Root, "reader-backups"));
        var operations = new FileSystemFileOrganizationService(journal, identities, new WindowsRecycleBinRestoreService(identities), backupStore: backups);
        var availability = new FileSystemRootFolderAvailabilityService(); var state = new NavigationSessionState();
        var readerImages = new RecordingReaderImages();
        var readerExternal = new RecordingReaderExternal();
        var monitor = new ManualMonitor();
        var window = new MediFiler3.MainWindow(Microsoft.Extensions.Logging.Abstractions.NullLogger<MediFiler3.MainWindow>.Instance,
            new FileSystemFolderTreeService(), new FileSystemFileListingService(), state, new WicImageDecoder(Files), NativeThumbnails,
            operations, journal, new FileSystemTextContentService(Files), readerExternal, new Defaults(), settings,
            new RootFolderHistoryService(store, availability), availability, new FileSystemCollisionPreviewService(NativeThumbnails), backups,
            layout, new WindowsWindowPlacement(), changeMonitor: monitor, readerImages: readerImages) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        window.Show();
        var browse = (BrowseView)Field(window, "_browseView")!;
        var navigation = (WinUiNavigationIsland)window.FindName("NavigationIsland");
        var toolbar = (WinUiToolbarIsland)window.FindName("ToolbarIsland");
        WinUiReaderIsland Reader() => (WinUiReaderIsland)Field(browse, "_reader")!;
        Microsoft.UI.Xaml.Controls.ScrollViewer Scroll() => (Microsoft.UI.Xaml.Controls.ScrollViewer)Field(Reader(), "_scroll")!;
        void SelectScaling(bool height)
        {
            var button = (Microsoft.UI.Xaml.Controls.AppBarToggleButton)Field(toolbar, height ? "_readerHeightButton" : "_readerWidthButton")!;
            if (button.IsChecked == true) return;
            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(button);
            ((Microsoft.UI.Xaml.Automation.Provider.IToggleProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle)).Toggle();
        }
        bool LoadedImage() => ((System.Collections.IDictionary)Field(Reader(), "_rows")!).Values.Cast<object>().Any(row =>
            ((Microsoft.UI.Xaml.Controls.Image)row.GetType().GetProperty("Image")!.GetValue(row)!).Source is not null);
        try
        {
            await LoadRestartRootsAsync(window, [directory]); await WaitUntil(() => state.Files.Count == 14);
            var files = state.Files.ToArray(); var root = state.ActiveRoot; var folder = state.ActiveFolder;
            var current = files.Single(file => file.Name == "page 5.png");
            ((NavigationController)Field(browse, "_navigationController")!).SelectFile(current);
            await Check("Reader mode projects images without rescan and suppresses inactive Browse", async () =>
            {
                navigation.SelectDestination("Reader");
                await WaitUntil(() => Field(browse, "_reader") is WinUiReaderIsland reader && reader.RealizedCount > 0 && LoadedImage());
                Require(state.ViewingMode == ViewingMode.Reader && state.Reader.Pages.Count == 12 && ReferenceEquals(root, state.ActiveRoot) &&
                    ReferenceEquals(folder, state.ActiveFolder) && files.SequenceEqual(state.Files), "Reader replaced the shared workspace");
                Require(state.Reader.Pages.Select(file => file.Name).SequenceEqual(Enumerable.Range(1, 12).Select(i => $"page {i}.png")), "Reader lost natural order or included media/text");
                Require(!(bool)Field(browse.FindName("ViewerHost"), "_active")! && ((FrameworkElement)browse.FindName("CarouselOverlay")).Visibility == Visibility.Collapsed,
                    "Reader started Browse presentation");
                Require(((Microsoft.UI.Xaml.Controls.AppBarButton)Field(toolbar, "_deleteFileButton")!).IsEnabled &&
                    ((Microsoft.UI.Xaml.Controls.AppBarButton)Field(toolbar, "_renameFileButton")!).Visibility == Microsoft.UI.Xaml.Visibility.Visible,
                    "Reader page actions unavailable");
                await WaitUntil(() => state.CurrentFile?.Name == "page 5.png" &&
                    !(bool)Field(Reader(), "_restore")! && !(bool)Field(Reader(), "_queued")!);
                await Task.Delay(100); // Allow native layout to apply the newly discovered aspect ratio.
                var row = ((System.Collections.IDictionary)Field(Reader(), "_rows")!).Values.Cast<object>().First(row =>
                    ((Microsoft.UI.Xaml.Controls.Image)row.GetType().GetProperty("Image")!.GetValue(row)!).Source is not null);
                var capture = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                await capture.RenderAsync((Microsoft.UI.Xaml.UIElement)row.GetType().GetProperty("Root")!.GetValue(row)!, 128, 64);
                var pixels = (await capture.GetPixelsAsync()).ToArray();
                var center = ((capture.PixelHeight / 2) * capture.PixelWidth + capture.PixelWidth / 2) * 4;
                Require(pixels[center + 1] > 40 && pixels[center + 3] > 200, "Reader assigned an image object but did not render source pixels");
            });
            await Check("Reader preserves file fraction through resize and Home return and stops inactive decoding", async () =>
            {
                await Task.Delay(200);
                var index = state.Reader.Pages.ToList().FindIndex(file => file.Name == "page 6.png");
                Scroll().ChangeView(null, state.Reader.Top(index) + state.Reader.Height(index) * .4, null, true);
                await WaitUntil(() => state.Reader.Anchor?.Path.EndsWith("page 6.png") == true);
                await Task.Delay(200); var anchor = state.Reader.Anchor!;
                window.Width = 1000; await Task.Delay(300);
                await WaitUntil(() => state.Reader.Anchor?.Path == anchor.Path && Math.Abs(state.Reader.Anchor.Fraction - anchor.Fraction) < .03);
                navigation.SelectDestination("Home"); await WaitUntil(() => !Reader().IsActive);
                var calls = readerImages.Calls; await Task.Delay(150); Require(readerImages.Calls == calls && Reader().RealizedCount == 0, "Inactive reader kept decoding/realizing");
                navigation.SelectDestination("Reader");
                try { await WaitUntil(() => Reader().RealizedCount > 0 && LoadedImage() && !(bool)Field(Reader(), "_restore")! &&
                    !(bool)Field(Reader(), "_queued")! && state.Reader.Anchor?.Path == anchor.Path && Math.Abs(state.Reader.Anchor.Fraction - anchor.Fraction) < .03); }
                catch (TimeoutException exception) { throw new InvalidOperationException(
                    $"Reader return did not settle: expected={anchor}; actual={state.Reader.Anchor}; current={state.CurrentFile?.Name}; offset={Scroll().VerticalOffset}; restore={Field(Reader(), "_restore")}; queued={Field(Reader(), "_queued")}", exception); }
                Require(state.Reader.Anchor?.Path == anchor.Path && Math.Abs(state.Reader.Anchor.Fraction - anchor.Fraction) < .03, "Reader return lost its anchor");
            });
            await Check("Reader source decoder handles supplied image formats as static first-frame pages", async () =>
            {
                var decoder = new FileSystemReaderImageService(new FileSystemFileContentService());
                foreach (var extension in new[] { ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" })
                {
                    if (!Fixtures.TryGetValue(extension, out var file)) continue;
                    var decoded = await decoder.DecodeAsync(file, 320, default);
                    Require(decoded.Width > 0 && decoded.Height > 0 && decoded.OriginalWidth > 0 && decoded.Pixels.Length == decoded.Width * decoded.Height * 4,
                        "Reader source decode failed for " + extension);
                }
            });
            await Check("Reader corrupt image keeps a recoverable placeholder without blocking other pages", async () =>
            {
                var bad = Path.Combine(directory, "page 99.png"); File.WriteAllText(bad, "invalid image");
                var entry = Entry(bad); state.ReplaceFiles(files.Concat([entry])); state.SetCurrentFile(entry);
                navigation.SelectDestination("Browse"); navigation.SelectDestination("Reader");
                await WaitUntil(() => ((System.Collections.IDictionary)Field(Reader(), "_rows")!).Values.Cast<object>().Any(row =>
                    ((FileEntry)row.GetType().GetProperty("File")!.GetValue(row)!).FullPath == bad &&
                    ((Microsoft.UI.Xaml.Controls.TextBlock)row.GetType().GetProperty("Status")!.GetValue(row)!).Text.StartsWith("Cannot display")));
                state.ReplaceFiles(files);
                await WaitUntil(() => LoadedImage());
            });
            await Check("Reader handles a large image sequence lazily and rejects obsolete decode publication", async () =>
            {
                state.ReplaceFiles(Enumerable.Range(0, 10000).Select(i => new FileEntry(Path.Combine(directory, $"missing-{i}.png"), $"missing-{i}.png", 1, DateTime.UnixEpoch)));
                await WaitUntil(() => state.Reader.Pages.Count == 10000 && Reader().RealizedCount > 0);
                Scroll().ChangeView(null, state.Reader.Top(9000), null, true);
                await Task.Delay(200);
                Require(Reader().RealizedCount is > 0 and < 30 && Reader().DecodedBytes <= WinUiReaderIsland.CacheLimit && readerImages.MaximumConcurrent <= 2, "Reader realization/decoding/memory is unbounded");
                navigation.SelectDestination("Settings"); await WaitUntil(() => !Reader().IsActive);
                var calls = readerImages.Calls; await Task.Delay(150); Require(calls == readerImages.Calls, "Cancelled reader work opened more files");
                state.ReplaceFiles(files); navigation.SelectDestination("Reader");
                await WaitUntil(() => state.Reader.Pages.Count == 12 && LoadedImage());
                Require(Reader().RealizedCount < 30, "Old sequence retained realized pages");
            });
            await Check("Reader renders owned pixels through pointer input resize invalidation and cache reuse without image tooltips", async () =>
            {
                var stress = Directory.CreateDirectory(Path.Combine(Root, "reader-render-stress")).FullName;
                var pixels = new byte[1536 * 2048 * 4];
                for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 1] = 200; pixels[i + 3] = 255; }
                var encoder = new System.Windows.Media.Imaging.BmpBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(
                    1536, 2048, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 1536 * 4)));
                var first = Path.Combine(stress, "page1.bmp");
                using (var stream = File.Create(first)) encoder.Save(stream);
                for (var i = 2; i <= 4; i++) File.Copy(first, Path.Combine(stress, $"page{i}.bmp"));
                state.ReplaceFiles(Directory.GetFiles(stress).OrderBy(path => path).Select(Entry));
                var bridge = (nint)Field(Reader(), "_handle")!;
                var input = bridge;
                while (ReaderInputChild(input, 5) is var child && child != 0) input = child;
                var point = (nint)((160 << 16) | 200);
                for (var pass = 0; pass < 10; pass++)
                {
                    window.Width = pass % 2 == 0 ? 1000 : 1240;
                    await Task.Delay(60);
                    Scroll().ChangeView(null, state.Reader.Top(pass % 4), null, true);
                    await WaitUntil(LoadedImage);
                    // Send only to our hidden test island. Do not move the user's pointer or focus.
                    foreach (var (message, buttons) in new[] { (0x200, 0), (0x201, 1), (0x200, 1), (0x202, 0),
                        (0x203, 1), (0x202, 0), (0x204, 2), (0x205, 0) })
                        SendReaderPointerMessage(input, message, (nint)buttons, point);
                    GC.Collect(); GC.WaitForPendingFinalizers();
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    var renderedPage = false;
                    var detail = "no settled visible image";
                    while (!renderedPage && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(40);
                        if ((bool)Field(Reader(), "_restore")! || (bool)Field(Reader(), "_queued")!) continue;
                        var rows = ((System.Collections.IDictionary)Field(Reader(), "_rows")!).Values.Cast<object>();
                        var row = rows.FirstOrDefault(row =>
                        {
                            var image = (Microsoft.UI.Xaml.Controls.Image)row.GetType().GetProperty("Image")!.GetValue(row)!;
                            var root = (Microsoft.UI.Xaml.FrameworkElement)row.GetType().GetProperty("Root")!.GetValue(row)!;
                            var top = Microsoft.UI.Xaml.Controls.Canvas.GetTop(root);
                            return image.Source is not null && root.IsLoaded && root.ActualWidth > 0 && root.ActualHeight > 0 &&
                                top < Scroll().VerticalOffset + Scroll().ViewportHeight && top + root.ActualHeight > Scroll().VerticalOffset;
                        });
                        if (row is null) continue;
                        var visual = (Microsoft.UI.Xaml.FrameworkElement)row.GetType().GetProperty("Root")!.GetValue(row)!;
                        Require(Microsoft.UI.Xaml.Controls.ToolTipService.GetToolTip(visual) is null, "Reader image still has a tooltip");
                        var top = Microsoft.UI.Xaml.Controls.Canvas.GetTop(visual);
                        var visibleTop = Math.Max(0, Scroll().VerticalOffset - top);
                        var visibleBottom = Math.Min(visual.ActualHeight, Scroll().VerticalOffset + Scroll().ViewportHeight - top);
                        var fraction = (visibleTop + visibleBottom) / (2 * visual.ActualHeight);
                        var capture = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                        try { await capture.RenderAsync(visual, 96, 128); }
                        catch (System.Runtime.InteropServices.COMException exception) when (exception.HResult == unchecked((int)0x80004004)) { continue; }
                        var rendered = (await capture.GetPixelsAsync()).ToArray();
                        // Native rendering/virtualization can finish between snapshot selection and capture.
                        if (!((System.Collections.IDictionary)Field(Reader(), "_rows")!).Values.Cast<object>().Any(current => ReferenceEquals(current, row))) continue;
                        var y = Math.Clamp((int)(capture.PixelHeight * fraction), 0, capture.PixelHeight - 1);
                        var sample = (y * capture.PixelWidth + capture.PixelWidth / 2) * 4;
                        detail = $"pass={pass}, size={capture.PixelWidth}x{capture.PixelHeight}, sample={y}, G={rendered[sample + 1]}, A={rendered[sample + 3]}";
                        renderedPage = rendered[sample + 1] > 100 && rendered[sample + 3] > 200;
                    }
                    Require(renderedPage, "Reader lost visible rendered pixels after input/collection: " + detail);
                    Require(Reader().DecodedBytes <= WinUiReaderIsland.CacheLimit, "Repeated resize exceeded decoded cache bound");
                    if (pass % 3 == 0) Reader().Invalidate([stress]);
                    if (pass % 3 == 1) { navigation.SelectDestination("Home"); navigation.SelectDestination("Reader"); }
                }
                Require(Directory.GetFiles(stress).Length == 4 && state.ViewingMode == ViewingMode.Reader,
                    "Reader pointer gestures changed files or viewing mode");
                state.ReplaceFiles(files); await WaitUntil(LoadedImage);
            });
            await Check("Reader native settings persist scaling spacing preload and counter while inactive and preserve the image anchor", async () =>
            {
                var portraits = Directory.GetFiles(Path.Combine(Root, "reader-render-stress")).OrderBy(path => path).Select(Entry).ToArray();
                state.ReplaceFiles(portraits);
                await WaitUntil(() => LoadedImage() && !(bool)Field(Reader(), "_restore")! && !(bool)Field(Reader(), "_queued")!);
                SelectScaling(true);
                await WaitUntil(() => !(bool)Field(Reader(), "_restore")! && !(bool)Field(Reader(), "_queued")!);
                Scroll().ChangeView(null, state.Reader.Top(1) + state.Reader.Height(1) * .3, null, true);
                await WaitUntil(() => state.Reader.Anchor?.Path == portraits[1].FullPath && Math.Abs(state.Reader.Anchor.Fraction - .3) < .03);
                await Task.Delay(100); var anchor = state.Reader.Anchor!;
                navigation.SelectDestination("Settings"); await Task.Delay(150);
                var calls = readerImages.Calls;
                var settingsView = (SettingsView)Field(window, "_settingsView")!;
                var controls = (WinUiCarouselSettingsIsland)settingsView.FindName("CarouselSliderIsland");
                Require(controls.GetType().GetField("_readerScaling", BindingFlags.Instance | BindingFlags.NonPublic) is null,
                    "Reader scaling still appears in Settings");
                ((Microsoft.UI.Xaml.Controls.Slider)Field(controls, "_readerSpacing")!).Value = 24;
                ((Microsoft.UI.Xaml.Controls.Slider)Field(controls, "_readerPreload")!).Value = 0;
                ((Microsoft.UI.Xaml.Controls.ToggleSwitch)Field(controls, "_readerIndicator")!).IsOn = false;
                var expected = new ReaderOptions(ReaderScalingMode.FitHeight, 24, 0, false);
                await settings.FlushAsync(); await Task.Delay(150);
                Require(readerImages.Calls == calls && Reader().RealizedCount == 0, "Inactive settings changes decoded pages");
                Require(state.Reader.Options == expected && settings.Current.GetReaderOptions() == expected, "Reader controls failed to update the application state");
                var restarted = new ApplicationSettingsService(new SqliteApplicationStateStore(new SqliteDatabase(database.DatabasePath)));
                await restarted.InitializeAsync(); Require(restarted.Current == settings.Current, "Reader settings did not survive SQLite reopen");
                navigation.SelectDestination("Reader");
                await WaitUntil(() => LoadedImage() && !(bool)Field(Reader(), "_restore")! && !(bool)Field(Reader(), "_queued")!);
                Require(state.Reader.Anchor?.Path == anchor.Path && Math.Abs(state.Reader.Anchor.Fraction - anchor.Fraction) < .03, "Reader options lost file/fraction anchor");
                for (var i = 0; i < portraits.Length; i++)
                {
                    Require(state.Reader.Height(i) <= Scroll().ViewportHeight + .1 && state.Reader.Width(i) <= Scroll().ViewportWidth,
                        "Fit height overflowed the viewport");
                    if (i + 1 < portraits.Length) Require(Math.Abs(state.Reader.Top(i + 1) - state.Reader.Top(i) - state.Reader.Height(i) - 24) < .1, "Reader spacing was not applied");
                }
                Require(((Microsoft.UI.Xaml.Controls.Grid)Field(Reader(), "_indicatorHost")!).Visibility == Microsoft.UI.Xaml.Visibility.Collapsed,
                    "Reader ignored the counter visibility setting");
            });
            await Check("Reader counter matches Gallery outlined bottom-right status and toggles without reloading images", async () =>
            {
                await WaitUntil(() => ((System.Collections.IDictionary)Field(Reader(), "_rows")!).Values.Cast<object>().All(row =>
                    ((Microsoft.UI.Xaml.Controls.Image)row.GetType().GetProperty("Image")!.GetValue(row)!).Source is not null));
                var row = ((System.Collections.IDictionary)Field(Reader(), "_rows")!).Values.Cast<object>().First();
                var image = (Microsoft.UI.Xaml.Controls.Image)row.GetType().GetProperty("Image")!.GetValue(row)!;
                var original = image.Source; var calls = readerImages.Calls;
                var controls = (WinUiCarouselSettingsIsland)((SettingsView)Field(window, "_settingsView")!).FindName("CarouselSliderIsland");
                ((Microsoft.UI.Xaml.Controls.ToggleSwitch)Field(controls, "_readerIndicator")!).IsOn = true;
                await Task.Delay(150);
                var host = (Microsoft.UI.Xaml.Controls.Grid)Field(Reader(), "_indicatorHost")!;
                var text = (Microsoft.UI.Xaml.Controls.TextBlock)Field(Reader(), "_indicator")!;
                var outlines = (List<Microsoft.UI.Xaml.Controls.TextBlock>)Field(Reader(), "_indicatorOutlines")!;
                Require(host.Visibility == Microsoft.UI.Xaml.Visibility.Visible && host.HorizontalAlignment == Microsoft.UI.Xaml.HorizontalAlignment.Right &&
                    host.VerticalAlignment == Microsoft.UI.Xaml.VerticalAlignment.Bottom && host.Margin == new Microsoft.UI.Xaml.Thickness(8, 8, 24, 8) && !host.IsHitTestVisible,
                    "Reader counter placement differs from Gallery");
                Require(text.FontWeight.Weight == Microsoft.UI.Text.FontWeights.SemiBold.Weight &&
                    ((Microsoft.UI.Xaml.Media.SolidColorBrush)text.Foreground).Color == Microsoft.UI.Colors.White && outlines.Count == 8 &&
                    outlines.All(outline => outline.Text == text.Text && ((Microsoft.UI.Xaml.Media.SolidColorBrush)outline.Foreground).Color == Microsoft.UI.Colors.Black &&
                        Microsoft.UI.Xaml.Automation.AutomationProperties.GetAccessibilityView(outline) == Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw),
                    $"Reader counter lost its shared outline styling/accessibility (outline count: {outlines.Count})");
                Require(ReferenceEquals(image.Source, original) && readerImages.Calls == calls, "Counter toggle restarted page decoding");
                browse.SetReaderOptions(new()); state.ReplaceFiles(files); await WaitUntil(LoadedImage);
            });
            await Check("Reader toolbar scopes scaling and a persisted centered 10-100 percent width without losing the anchor", async () =>
            {
                SelectScaling(false);
                var widthButton = (Microsoft.UI.Xaml.Controls.AppBarToggleButton)Field(toolbar, "_readerWidthButton")!;
                var heightButton = (Microsoft.UI.Xaml.Controls.AppBarToggleButton)Field(toolbar, "_readerHeightButton")!;
                var slider = (Microsoft.UI.Xaml.Controls.Slider)Field(toolbar, "_readerWidthSlider")!;
                var widthControls = (Microsoft.UI.Xaml.Controls.StackPanel)Field(toolbar, "_readerWidthControls")!;
                var group = (Microsoft.UI.Xaml.Controls.StackPanel)Field(toolbar, "_readerControls")!;
                Require(((Microsoft.UI.Xaml.Controls.FontIcon)widthButton.Icon).Glyph == "\uE8AB" &&
                    ((Microsoft.UI.Xaml.Controls.FontIcon)heightButton.Icon).Glyph == "\uE8CB" && widthButton.IsChecked == true && heightButton.IsChecked != true &&
                    ReferenceEquals(group.Children[0], widthControls), "Reader sizing glyphs, active mode or slider order are incorrect");
                var sidebar = (Microsoft.UI.Xaml.Controls.NavigationView)Field(navigation, "_navigationView")!;
                Require(sidebar.MenuItems[1] is Microsoft.UI.Xaml.Controls.NavigationViewItemSeparator &&
                    sidebar.MenuItems.OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>().Select(item => (string)item.Tag).SequenceEqual(new[] { "Home", "Browse", "Gallery", "Reader" }),
                    "Sidebar separator changed the view grouping or destinations");
                Require(((Microsoft.UI.Xaml.Controls.NavigationViewItemSeparator)sidebar.MenuItems[1]).ActualHeight > 0, "Sidebar separator has no visible height");
                var readerItem = sidebar.MenuItems.OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>().Single(item => Equals(item.Tag, "Reader"));
                Require(readerItem.Icon is Microsoft.UI.Xaml.Controls.FontIcon reading && reading.Glyph == "\uE736", "Reader sidebar lost its ReadingMode glyph");
                Require(group.Visibility == Microsoft.UI.Xaml.Visibility.Visible && group.HorizontalAlignment == Microsoft.UI.Xaml.HorizontalAlignment.Right &&
                    slider.Minimum == 10 && slider.Maximum == 100 && slider.Value == 100, "Reader width toolbar has incorrect context/default/range");
                SelectScaling(false);
                Require(browse.JumpReaderPage(3), "Could not prepare the width-reflow anchor page");
                await WaitUntil(() => LoadedImage() && state.CurrentFile == state.Reader.Pages[3] &&
                    !(bool)Field(Reader(), "_restore")! && !(bool)Field(Reader(), "_queued")!);
                Scroll().ChangeView(null, state.Reader.Top(3) + state.Reader.Height(3) * .2, null, true);
                try { await WaitUntil(() => state.Reader.Anchor?.Path == state.Reader.Pages[3].FullPath && Math.Abs(state.Reader.Anchor.Fraction - .2) < .03); }
                catch (TimeoutException exception) { throw new InvalidOperationException($"Reader width probe scroll did not settle: anchor={state.Reader.Anchor}; offset={Scroll().VerticalOffset}; target={state.Reader.Top(3) + state.Reader.Height(3) * .2}; extent={Scroll().ScrollableHeight}; restore={Field(Reader(), "_restore")}; queued={Field(Reader(), "_queued")}", exception); }
                await Task.Delay(100); var anchor = state.Reader.Anchor!;
                slider.Value = 50;
                await WaitUntil(() => LoadedImage() && !(bool)Field(Reader(), "_restore")! && !(bool)Field(Reader(), "_queued")!);
                Require(state.Reader.Anchor?.Path == anchor.Path && Math.Abs(state.Reader.Anchor.Fraction - anchor.Fraction) < .03,
                    "Reader width reflow lost the image anchor");
                slider.Value = 10;
                await WaitUntil(() => LoadedImage() && !(bool)Field(Reader(), "_restore")! && !(bool)Field(Reader(), "_queued")!);
                var available = (double)Field(Reader(), "_width")!;
                Require(Math.Abs(state.Reader.Width(0) - available * .1) < .1, "Reader minimum width is not ten percent");
                foreach (var row in ((System.Collections.IDictionary)Field(Reader(), "_rows")!).Values.Cast<object>())
                {
                    var visual = (Microsoft.UI.Xaml.Controls.Border)row.GetType().GetProperty("Root")!.GetValue(row)!;
                    var left = Microsoft.UI.Xaml.Controls.Canvas.GetLeft(visual);
                    Require(Math.Abs(left - ((double)Field(Reader(), "_inset")! + 16 + (available - visual.Width) / 2)) < .1,
                        "Reader narrowed images are not centered");
                }
                slider.Value = 37; SelectScaling(true);
                await WaitUntil(() => LoadedImage() && !(bool)Field(Reader(), "_restore")! && !(bool)Field(Reader(), "_queued")!);
                Require(widthButton.IsChecked != true && heightButton.IsChecked == true &&
                    widthControls.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed && state.Reader.Options.WidthPercent == 37,
                    "Height mode lost the saved width or left the width slider visible");
                SelectScaling(false);
                await WaitUntil(() => widthControls.Visibility == Microsoft.UI.Xaml.Visibility.Visible && LoadedImage());
                Require(slider.Value == 37 && ((Microsoft.UI.Xaml.Controls.TextBlock)Field(toolbar, "_readerWidthLabel")!).Text == "37%",
                    "Width mode failed to restore its percentage");
                await settings.FlushAsync();
                var restarted = new ApplicationSettingsService(new SqliteApplicationStateStore(new SqliteDatabase(database.DatabasePath)));
                await restarted.InitializeAsync(); Require(restarted.Current.ReaderWidthPercent == 37 && restarted.Current.ReaderScaling == ReaderScalingMode.FitWidth,
                    "Toolbar width/scaling did not survive SQLite reopen");
                navigation.SelectDestination("Settings");
                Require(group.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed, "Reader controls leaked into Settings");
                var controls = (WinUiCarouselSettingsIsland)((SettingsView)Field(window, "_settingsView")!).FindName("CarouselSliderIsland");
                ((Microsoft.UI.Xaml.Controls.Slider)Field(controls, "_readerSpacing")!).Value = 16;
                Require(settings.Current.ReaderWidthPercent == 37 && state.Reader.Options.WidthPercent == 37,
                    "Settings used a stale snapshot and overwrote toolbar width");
                navigation.SelectDestination("Reader");
                Require(group.Visibility == Microsoft.UI.Xaml.Visibility.Visible && slider.Value == 37, "Reader return reset width controls");
                slider.Value = 100;
            });
            await Check("F3 toggles persisted tree pin across Browse Gallery and Reader with focus repeat modal and stale-context guards", async () =>
            {
                var shortcut = typeof(MediFiler3.MainWindow).GetMethod("TryHandleViewShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!;
                bool Key(System.Windows.Input.ModifierKeys modifiers = System.Windows.Input.ModifierKeys.None, bool repeat = false) =>
                    (bool)shortcut.Invoke(window, [System.Windows.Input.Key.F3, modifiers, repeat])!;
                bool Pinned() => (bool)Field(browse, "_folderOverlayPinned")!;
                foreach (var mode in new[] { "Browse", "Gallery", "Reader" })
                {
                    navigation.SelectDestination(mode); await Task.Delay(100);
                    var before = Pinned();
                    if (mode == "Reader") Scroll().Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                    else ((System.Windows.Controls.Primitives.ToggleButton)browse.FindName("FolderPinButton")).Focus();
                    Require(Key(), $"F3 was ignored in {mode}"); await WaitUntil(() => Pinned() != before);
                    Require(settings.Current.FolderTreePinned == Pinned() &&
                        ((System.Windows.Controls.Primitives.ToggleButton)browse.FindName("FolderPinButton")).IsChecked == Pinned(), "Pin shortcut bypassed the existing button/persistence path");
                    Require(Key(repeat: true), "Held F3 was not consumed"); await Task.Delay(50);
                    Require(Pinned() != before, "Key repeat toggled the pin again");
                    Require(!Key(System.Windows.Input.ModifierKeys.Control) && !Key(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift),
                        "Tree pin shortcut accepted the wrong modifiers");
                    Require(Key(), "Second F3 was ignored"); await WaitUntil(() => Pinned() == before);
                }
                Require(!(bool)shortcut.Invoke(window, [System.Windows.Input.Key.T, System.Windows.Input.ModifierKeys.Control, false])!, "Old Ctrl+T shortcut is still active");
                var initial = Pinned();
                window.IsEnabled = false;
                try { Require(!Key() && Pinned() == initial, "Disabled/dialog owner accepted F3"); }
                finally { window.IsEnabled = true; }
                var owned = new Window { Owner = window, Width = 100, Height = 100, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                try { owned.Show(); Require(!Key() && Pinned() == initial, "Owned dialog did not guard F3"); }
                finally { owned.Close(); }
                Require(Key(), "Stale-context shortcut was not queued"); navigation.SelectDestination("Settings");
                await Task.Delay(100); Require(Pinned() == initial && !Key(), "Queued F3 changed the hidden tree after switching pages");
                navigation.SelectDestination("Home"); Require(!Key() && Pinned() == initial, "Home accepted a tree-view shortcut");
                navigation.SelectDestination("Reader");
                Require(Key(), "Reader return did not restore F3"); await WaitUntil(() => Pinned() != initial);
                await settings.FlushAsync();
                var restarted = new ApplicationSettingsService(new SqliteApplicationStateStore(new SqliteDatabase(database.DatabasePath)));
                await restarted.InitializeAsync(); Require(restarted.Current.FolderTreePinned == Pinned(), "Pin shortcut did not persist across SQLite reopen");
                Require(Key(), "Could not restore original pin state"); await WaitUntil(() => Pinned() == initial);
            });
            await Check("Forwarded Browse shortcuts safely handle F4 text mode zoom and playback with a routed event", async () =>
            {
                navigation.SelectDestination("Browse");
                var controller = (NavigationController)Field(browse, "_navigationController")!;
                var viewer = (ViewerHostView)browse.FindName("ViewerHost");
                var shortcut = typeof(MediFiler3.MainWindow).GetMethod("TryHandleViewShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!;
                bool Key(System.Windows.Input.Key key) => (bool)shortcut.Invoke(window, [key, System.Windows.Input.ModifierKeys.None, false])!;
                controller.SelectFile(files.Single(file => file.Extension == ".txt"));
                await WaitUntil(() => viewer.Capabilities.HasFlag(MediFiler3.Application.Viewing.ViewerCapabilities.DocumentScroll));
                var text = state.CurrentFile;
                for (var i = 0; i < 4; i++)
                {
                    ((Microsoft.UI.Xaml.Controls.AppBarButton)Field(toolbar, "_refreshButton")!).Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                    var previous = viewer.IsTextInteraction;
                    Require(Key(System.Windows.Input.Key.F4) && viewer.IsTextInteraction != previous && state.CurrentFile == text,
                        "Forwarded F4 failed to toggle text mode without changing files");
                }
                controller.SelectFile(files.First(file => file.Extension == ".png")); await WaitUntil(() => viewer.CanZoom);
                Require(Key(System.Windows.Input.Key.Add) && viewer.CurrentZoomLevel > 1, "Forwarded zoom key did not safely mark its event handled");
                var wave = Path.Combine(directory, "shortcut-wave.wav"); File.Copy(Fixtures[".wav"].FullPath, wave);
                var entry = Entry(wave); state.ReplaceFiles(files.Concat([entry])); controller.SelectFile(entry);
                browse.SetPlaybackVolume(0);
                await WaitUntil(() => viewer.Capabilities.HasFlag(MediFiler3.Application.Viewing.ViewerCapabilities.Playback));
                Require(Key(System.Windows.Input.Key.Space), "Forwarded playback key did not safely mark its event handled");
                viewer.SetActive(false); state.ReplaceFiles(files); navigation.SelectDestination("Reader"); await WaitUntil(LoadedImage);
            });
            await Check("Reader Go is a single bounded number field even for 10000 pages", async () =>
            {
                var pages = Enumerable.Range(1, 10000).Select(i => new FileEntry(Path.Combine(directory, $"pick-{i}.png"), $"pick-{i}.png", 1, DateTime.UnixEpoch)).ToArray();
                int? selected = null;
                await DriveContentDialogAsync("Go to Reader page", async () => selected = await toolbar.PromptReaderPageAsync(pages, 5000), async (_, dialog) =>
                {
                    var panel = (Microsoft.UI.Xaml.Controls.StackPanel)dialog.Content;
                    Require(panel.Children.OfType<Microsoft.UI.Xaml.Controls.GridView>().Count() == 0 && panel.Children.OfType<Microsoft.UI.Xaml.Controls.ListView>().Count() == 0,
                        "Reader Go still creates a page picker");
                    var input = panel.Children.OfType<Microsoft.UI.Xaml.Controls.TextBox>().Single();
                    Require(input.Text == "5001" && input.MaxLength == 5, "Initial page or digit bound changed");
                    input.Text = "123"; await WaitUntil(() => dialog.IsPrimaryButtonEnabled && input.Text == "123");
                    input.Text = "10001"; await Task.Delay(50); Require(input.Text == "123", "Reader page entry accepted a value beyond the page count");
                    input.Text = "0"; await Task.Delay(50); Require(input.Text == "123", "Reader page entry accepted zero");
                    input.Text = ""; await WaitUntil(() => !dialog.IsPrimaryButtonEnabled);
                    input.Text = "pick-77.png"; await Task.Delay(50); Require(input.Text == "" && !dialog.IsPrimaryButtonEnabled, "Reader Go accepted nonnumeric text");
                    input.Text = "10000"; await WaitUntil(() => dialog.IsPrimaryButtonEnabled);
                    InvokeNativeButton(NativeDialogButton(dialog, "PrimaryButton")!);
                });
                Require(selected == 9999, "Reader Go did not accept the last available page");
            });
            await Check("Reader navigation uses focus-independent keys and accessible toolbar without crossing folders", async () =>
            {
                browse.SetReaderOptions(new()); state.ReplaceFiles(files);
                await WaitUntil(() => LoadedImage() && !(bool)Field(Reader(), "_restore")! && !(bool)Field(Reader(), "_queued")!);
                var shortcut = typeof(MediFiler3.MainWindow).GetMethod("TryHandleViewShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!;
                bool Key(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers = System.Windows.Input.ModifierKeys.None, bool repeat = false) =>
                    (bool)shortcut.Invoke(window, [key, modifiers, repeat])!;
                var external = Field(toolbar, "_externalToolbar")!;
                var defaultOpen = (Microsoft.UI.Xaml.Controls.AppBarButton)external.GetType().GetProperty("OpenButton")!.GetValue(external)!;
                var chooseApp = (Microsoft.UI.Xaml.Controls.AppBarButton)external.GetType().GetProperty("ChooseButton")!.GetValue(external)!;
                Require(defaultOpen.Visibility == Microsoft.UI.Xaml.Visibility.Visible && chooseApp.Visibility == Microsoft.UI.Xaml.Visibility.Visible &&
                    ((Microsoft.UI.Xaml.Controls.AppBarButton)Field(toolbar, "_readerBrowse")!).Visibility == Microsoft.UI.Xaml.Visibility.Visible,
                    "Reader Open buttons are missing");
                var originalFolder = state.ActiveFolder;
                Require(browse.JumpReaderPage(4), "Explicit Reader jump was rejected");
                await WaitUntil(() => state.CurrentFile == state.Reader.Pages[4] && !(bool)Field(Reader(), "_restore")!);
                ((Microsoft.UI.Xaml.Controls.Slider)Field(toolbar, "_readerWidthSlider")!).Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                Require(Key(System.Windows.Input.Key.Right), "Reader Right ignored native focus");
                await WaitUntil(() => state.CurrentFile == state.Reader.Pages[5]);
                ((System.Windows.Controls.Primitives.ToggleButton)browse.FindName("FolderPinButton")).Focus();
                Require(Key(System.Windows.Input.Key.Left), "Reader Left ignored WPF focus"); await WaitUntil(() => state.CurrentFile == state.Reader.Pages[4]);
                Require(Key(System.Windows.Input.Key.Right, repeat: true), "Reader navigation ignored repeated arrows"); await WaitUntil(() => state.CurrentFile == state.Reader.Pages[5]);
                Require(Key(System.Windows.Input.Key.Down), "Reader Down was ignored");
                await WaitUntil(() => !(bool)Field(Reader(), "_restore")!); var offset = Scroll().VerticalOffset;
                Require(Key(System.Windows.Input.Key.Space), "Reader Space was ignored"); await WaitUntil(() => Scroll().VerticalOffset > offset + 100);
                Require(Key(System.Windows.Input.Key.Space, System.Windows.Input.ModifierKeys.Shift), "Shift+Space was ignored");
                await WaitUntil(() => Math.Abs(Scroll().VerticalOffset - offset) < 2);
                Require(Key(System.Windows.Input.Key.PageDown) && Key(System.Windows.Input.Key.PageUp), "Reader viewport keys were ignored");
                Require(Key(System.Windows.Input.Key.End), "Reader End was ignored"); await WaitUntil(() => Math.Abs(Scroll().VerticalOffset - Scroll().ScrollableHeight) < 2);
                Require(Key(System.Windows.Input.Key.Home), "Reader Home was ignored"); await WaitUntil(() => Scroll().VerticalOffset < 2);
                Require(browse.JumpReaderPage(0), "Could not jump to the first Reader image");
                await WaitUntil(() => state.CurrentFile == state.Reader.Pages[0]);
                Require(Key(System.Windows.Input.Key.Left) && ReferenceEquals(originalFolder, state.ActiveFolder), "Reader escaped the folder at the start");
                InvokeReaderToolbarButton((Microsoft.UI.Xaml.Controls.AppBarButton)Field(toolbar, "_readerNext")!);
                await WaitUntil(() => state.CurrentFile == state.Reader.Pages[1]);
                Require(browse.JumpReaderPage(state.Reader.Pages.Count - 1), "Could not jump to the last page");
                await WaitUntil(() => state.CurrentFile == state.Reader.Pages[^1]);
                Require(!((Microsoft.UI.Xaml.Controls.AppBarButton)Field(toolbar, "_readerNext")!).IsEnabled && Key(System.Windows.Input.Key.Right) &&
                    ReferenceEquals(originalFolder, state.ActiveFolder) && state.Files.SequenceEqual(files), "Reader crossed a folder or enabled next beyond the sequence");
                Require(!Key(System.Windows.Input.Key.Right, System.Windows.Input.ModifierKeys.Control), "Reader claimed an undefined shortcut");
                Require(Key(System.Windows.Input.Key.Enter), "Reader Enter was ignored");
                await WaitUntil(() => browse.IsBrowse && state.CurrentFile == state.Reader.Pages[^1]);
                Require(defaultOpen.Visibility == Microsoft.UI.Xaml.Visibility.Visible && chooseApp.Visibility == Microsoft.UI.Xaml.Visibility.Visible,
                    "Reader changed external buttons in Browse");
                navigation.SelectDestination("Reader"); await WaitUntil(LoadedImage);
                var fresh = Enumerable.Range(0, 6).Select(i => Path.Combine(directory, $"fresh-tail-{i}.png")).ToArray();
                foreach (var path in fresh) File.Copy(Fixtures[".png"].FullPath, path);
                state.ReplaceFiles(fresh.Select(Entry));
                Require(Key(System.Windows.Input.Key.End), "Reader End rejected an unmeasured sequence");
                await WaitUntil(() => !(bool)Field(Reader(), "_restore")! && !(bool)Field(Reader(), "_queued")! && LoadedImage() &&
                    Math.Abs(Scroll().VerticalOffset - Scroll().ScrollableHeight) < 2);
                await Task.Delay(150); Require(Math.Abs(Scroll().VerticalOffset - Scroll().ScrollableHeight) < 2, "Late image dimensions moved Reader away from End");
                state.ReplaceFiles(files); await WaitUntil(LoadedImage);
                var image = ((System.Collections.IDictionary)Field(Reader(), "_rows")!).Values.Cast<object>()
                    .Select(row => (Microsoft.UI.Xaml.Controls.Image)row.GetType().GetProperty("Image")!.GetValue(row)!).First(image => image.Source is not null);
                var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(image);
                Require(peer is not null && peer.GetName().StartsWith("Page ") && peer.GetName().Contains(".png") &&
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAccessibilityView(image) == Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content,
                    "Reader images do not expose page/filename accessibility");
                browse.JumpReaderPage(0); await WaitUntil(() => state.CurrentFile == state.Reader.Pages[0]);
                var renamedPath = Path.Combine(directory, "reader-accessible-name.png"); File.Copy(Fixtures[".png"].FullPath, renamedPath);
                var replacement = Entry(renamedPath); Reader().MapPaths([(files[0].FullPath, renamedPath)]); state.ReplaceFiles(new[] { replacement }.Concat(files.Skip(1)));
                await WaitUntil(() => state.CurrentFile == replacement && !(bool)Field(Reader(), "_restore")!);
                Require(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName((Microsoft.UI.Xaml.Controls.TextBlock)Field(Reader(), "_indicator")!).Contains(replacement.Name),
                    "Reader counter retained an old filename when page number/count were unchanged");
                state.ReplaceFiles(files); await WaitUntil(LoadedImage);
            });
            await Check("Reader jump dialog accepts only page numbers and rejects cancellation stale inputs and inactive views", async () =>
            {
                var jump = typeof(MediFiler3.MainWindow).GetMethod("PromptReaderJumpAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Task Prompt() => (Task)jump.Invoke(window, null)!;
                await DriveContentDialogAsync("Go to Reader page", Prompt, async (_, dialog) =>
                {
                    var input = ((Microsoft.UI.Xaml.Controls.StackPanel)dialog.Content).Children.OfType<Microsoft.UI.Xaml.Controls.TextBox>().Single();
                    input.Text = ""; await WaitUntil(() => !dialog.IsPrimaryButtonEnabled);
                    input.Text = "999999"; await WaitUntil(() => !dialog.IsPrimaryButtonEnabled); Require(!dialog.IsPrimaryButtonEnabled, "Jump dialog accepted an out-of-range page");
                    input.Text = "missing.png"; Require(!dialog.IsPrimaryButtonEnabled, "Jump dialog accepted an absent image");
                    input.Text = "page 7.png"; await WaitUntil(() => !dialog.IsPrimaryButtonEnabled);
                    input.Text = "7"; await WaitUntil(() => dialog.IsPrimaryButtonEnabled); Require(dialog.IsPrimaryButtonEnabled, "Jump dialog rejected a page number");
                    InvokeNativeButton(NativeDialogButton(dialog, "PrimaryButton")!);
                });
                await WaitUntil(() => state.CurrentFile?.Name == "page 7.png" && !(bool)Field(Reader(), "_restore")!);
                var previous = state.CurrentFile;
                await DriveContentDialogAsync("Go to Reader page", Prompt, (_, dialog) => InvokeNativeButton(NativeDialogButton(dialog, "CloseButton")!));
                Require(state.CurrentFile == previous, "Cancelled Reader jump changed the current file");
                var shortcut = typeof(MediFiler3.MainWindow).GetMethod("TryHandleViewShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var visited = false;
                await DriveContentDialogAsync("Go to Reader page", async () =>
                {
                    Require((bool)shortcut.Invoke(window, [System.Windows.Input.Key.G, System.Windows.Input.ModifierKeys.Control, false])!, "Ctrl+G was ignored");
                    await WaitUntil(() => visited && !(bool)Field(window, "_readerJumpOpen")!);
                }, (_, dialog) =>
                {
                    visited = true;
                    Require(!(bool)shortcut.Invoke(window, [System.Windows.Input.Key.Right, System.Windows.Input.ModifierKeys.None, false])!, "Reader shortcut escaped an owned jump dialog");
                    ((Microsoft.UI.Xaml.Controls.StackPanel)dialog.Content).Children.OfType<Microsoft.UI.Xaml.Controls.TextBox>().Single().Text = "2";
                    InvokeNativeButton(NativeDialogButton(dialog, "PrimaryButton")!);
                });
                await WaitUntil(() => state.CurrentFile?.Name == "page 2.png");
                await DriveContentDialogAsync("Go to Reader page", Prompt, (_, dialog) =>
                {
                    ((Microsoft.UI.Xaml.Controls.StackPanel)dialog.Content).Children.OfType<Microsoft.UI.Xaml.Controls.TextBox>().Single().Text = "10";
                    state.ReplaceFiles(files.Take(3));
                    InvokeNativeButton(NativeDialogButton(dialog, "PrimaryButton")!);
                });
                await Task.Delay(100); Require(state.CurrentFile?.Name != "page 10.png" && state.Reader.Pages.Count == 3, "Stale dialog published a removed image");
                state.ReplaceFiles(files); await WaitUntil(() => LoadedImage() && !(bool)Field(Reader(), "_restore")! && !(bool)Field(Reader(), "_queued")! &&
                    browse.ReaderPageIndex >= 0 && state.CurrentFile == state.Reader.Pages[browse.ReaderPageIndex]); previous = state.CurrentFile;
                await DriveContentDialogAsync("Go to Reader page", Prompt, (_, dialog) =>
                {
                    ((Microsoft.UI.Xaml.Controls.StackPanel)dialog.Content).Children.OfType<Microsoft.UI.Xaml.Controls.TextBox>().Single().Text = "8";
                    navigation.SelectDestination("Home"); InvokeNativeButton(NativeDialogButton(dialog, "PrimaryButton")!);
                });
                await Task.Delay(100); Require(!Reader().IsActive && state.CurrentFile == previous, "Jump result escaped into an inactive view");
                navigation.SelectDestination("Reader"); await WaitUntil(LoadedImage);
            });
            await Check("Reader failed pages expose guarded retry and Browse recovery without flushing surviving pixels", async () =>
            {
                var path = Path.Combine(directory, "slice4-corrupt.png"); File.WriteAllText(path, "bad image");
                var file = Entry(path); state.ReplaceFiles(files.Concat([file]));
                Require(browse.JumpReaderPage(state.Reader.Pages.Count - 1), "Could not target corrupt Reader page");
                object? ErrorRow() => ((System.Collections.IDictionary)Field(Reader(), "_rows")!).Values.Cast<object>().FirstOrDefault(row =>
                    Equals(row.GetType().GetProperty("File")!.GetValue(row), file) && ((Microsoft.UI.Xaml.Controls.TextBlock)row.GetType().GetProperty("Status")!.GetValue(row)!).Text.StartsWith("Cannot display"));
                await WaitUntil(() => ErrorRow() is not null);
                var row = ErrorRow()!; var actions = (Microsoft.UI.Xaml.Controls.StackPanel)row.GetType().GetProperty("ErrorActions")!.GetValue(row)!;
                Require(actions.Visibility == Microsoft.UI.Xaml.Visibility.Visible && actions.Children.Count == 2 &&
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(actions.Children[0]).Contains(file.Name), "Reader failure actions are inaccessible");
                var warm = ((System.Collections.IDictionary)Field(Reader(), "_rows")!).Values.Cast<object>().FirstOrDefault(item =>
                    !Equals(item.GetType().GetProperty("File")!.GetValue(item), file) && ((Microsoft.UI.Xaml.Controls.Image)item.GetType().GetProperty("Image")!.GetValue(item)!).Source is not null);
                var warmImage = warm is null ? null : (Microsoft.UI.Xaml.Controls.Image)warm.GetType().GetProperty("Image")!.GetValue(warm)!;
                var original = warmImage?.Source;
                File.Copy(Fixtures[".png"].FullPath, path, true);
                InvokeNativeButton((Microsoft.UI.Xaml.Controls.Button)actions.Children[0]);
                await WaitUntil(() => ((System.Collections.IDictionary)Field(Reader(), "_rows")!).Values.Cast<object>().Any(item =>
                    Equals(item.GetType().GetProperty("File")!.GetValue(item), file) && ((Microsoft.UI.Xaml.Controls.Image)item.GetType().GetProperty("Image")!.GetValue(item)!).Source is not null));
                if (warmImage is not null) Require(ReferenceEquals(original, warmImage.Source), "Retry flushed an unrelated realized image");
                File.WriteAllText(path, "broken again");
                Require(browse.HandleViewShortcut(System.Windows.Input.Key.R, System.Windows.Input.ModifierKeys.Control), "Ctrl+R did not retry the current page");
                await WaitUntil(() => ErrorRow() is not null);
                actions = (Microsoft.UI.Xaml.Controls.StackPanel)ErrorRow()!.GetType().GetProperty("ErrorActions")!.GetValue(ErrorRow())!;
                InvokeNativeButton((Microsoft.UI.Xaml.Controls.Button)actions.Children[1]); navigation.SelectDestination("Home");
                await Task.Delay(100); Require(!browse.IsBrowse && !Reader().IsActive, "Stale error action opened Browse after leaving Reader");
                navigation.SelectDestination("Reader"); await WaitUntil(() => ErrorRow() is not null);
                actions = (Microsoft.UI.Xaml.Controls.StackPanel)ErrorRow()!.GetType().GetProperty("ErrorActions")!.GetValue(ErrorRow())!;
                InvokeNativeButton((Microsoft.UI.Xaml.Controls.Button)actions.Children[1]);
                await WaitUntil(() => browse.IsBrowse && state.CurrentFile == file);
                Require(File.ReadAllText(path) == "broken again", "Reader recovery mutated the source");
                state.ReplaceFiles(files); navigation.SelectDestination("Reader"); await WaitUntil(LoadedImage);
            });
            await Check("Reader rapid distant jumps and small-page explicit selection stay lazy and reject inactive work", async () =>
            {
                state.ReplaceFiles(Enumerable.Range(0, 10000).Select(i => new FileEntry(Path.Combine(directory, $"jump-{i}.png"), $"jump-{i}.png", 1, DateTime.UnixEpoch)));
                Require(browse.JumpReaderPage(9000) && browse.JumpReaderPage(250) && browse.JumpReaderPage(9999), "Rapid Reader jumps failed");
                await WaitUntil(() => state.CurrentFile?.Name == "jump-9999.png" && !(bool)Field(Reader(), "_restore")!);
                Require(Reader().RealizedCount < 30 && Reader().DecodedBytes <= WinUiReaderIsland.CacheLimit && readerImages.MaximumConcurrent <= 2,
                    "Distant navigation escaped Reader resource bounds");
                Require(!browse.JumpReaderPage(-1) && !browse.JumpReaderPage(10000), "Reader accepted an out-of-range page");
                navigation.SelectDestination("Settings"); var calls = readerImages.Calls;
                await Task.Delay(150); Require(readerImages.Calls == calls && Reader().RealizedCount == 0 && !browse.JumpReaderPage(4), "Inactive jumps started Reader work");
                state.ReplaceFiles(files); navigation.SelectDestination("Reader");
                browse.SetReaderOptions(new(WidthPercent: 10));
                Require(browse.JumpReaderPage(1), "Small-page jump failed"); await WaitUntil(() => state.CurrentFile == state.Reader.Pages[1]);
                Require(browse.ReaderPageIndex == 1, "Reading line overrode the explicit small-page target");
                await WaitUntil(() => !(bool)Field(Reader(), "_restore")! && !(bool)Field(Reader(), "_queued")!);
                typeof(WinUiReaderIsland).GetMethod("OnReaderInput", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Reader(), null);
                Require(browse.ReaderPageIndex == 1, "Non-scrolling input changed the explicit small-page target");
                browse.SetReaderOptions(new());
            });
            await Check("Reader empty image projection keeps unsupported files and releases native image work", async () =>
            {
                state.ReplaceFiles(files.Where(file => !ReaderSessionState.Supports(file)));
                await WaitUntil(() => state.Reader.Pages.Count == 0 && Reader().RealizedCount == 0 &&
                    ((Microsoft.UI.Xaml.Controls.TextBlock)Field(Reader(), "_empty")!).Visibility == Microsoft.UI.Xaml.Visibility.Visible);
                Require(state.Files.Count == 2 && ((Microsoft.UI.Xaml.Controls.TextBlock)Field(Reader(), "_empty")!).Visibility == Microsoft.UI.Xaml.Visibility.Visible,
                    "Empty reader filtered the shared folder or lacks an empty state");
                navigation.SelectDestination("Browse"); Require(!Reader().IsActive && browse.IsBrowse, "Browse handoff retained active reader");
            });
            await RunFolderClickChecksAsync(window, state, browse, navigation);
            await Check("Reader external toolbar and shortcuts open only the active page without Browse work", async () =>
            {
                navigation.SelectDestination("Reader"); browse.JumpReaderPage(1);
                await WaitUntil(() => state.CurrentFile == state.Reader.Pages[1] && !(bool)Field(Reader(), "_restore")!);
                var expected = state.CurrentFile!;
                var group = Field(toolbar, "_externalToolbar")!;
                var open = (Microsoft.UI.Xaml.Controls.AppBarButton)group.GetType().GetProperty("OpenButton")!.GetValue(group)!;
                var choose = (Microsoft.UI.Xaml.Controls.AppBarButton)group.GetType().GetProperty("ChooseButton")!.GetValue(group)!;
                await WaitUntil(() => open.IsEnabled && choose.IsEnabled);
                var before = readerExternal.Calls.Count;
                InvokeReaderToolbarButton(open); await WaitUntil(() => readerExternal.Calls.Count == before + 1 && !browse.ExternalOpenState.Busy);
                InvokeReaderToolbarButton(choose); await WaitUntil(() => readerExternal.Calls.Count == before + 2 && !browse.ExternalOpenState.Busy);
                var shortcut = typeof(MediFiler3.MainWindow).GetMethod("TryHandleViewShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Require((bool)shortcut.Invoke(window, [System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Control, false])!, "Ctrl+Enter ignored in Reader");
                await WaitUntil(() => readerExternal.Calls.Count == before + 3);
                Require((bool)shortcut.Invoke(window, [System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, false])!, "Ctrl+Shift+Enter ignored in Reader");
                await WaitUntil(() => readerExternal.Calls.Count == before + 4);
                Require(readerExternal.Calls.Skip(before).All(call => call.Path == expected.FullPath && call.Owner != 0) &&
                    readerExternal.Calls.Skip(before).Select(call => call.Action).SequenceEqual(new[] { ExternalFileAction.DefaultApplication,
                        ExternalFileAction.ChooseApplication, ExternalFileAction.DefaultApplication, ExternalFileAction.ChooseApplication }) &&
                    !(bool)Field(browse.FindName("ViewerHost"), "_active")!, "Reader launched another file or resumed Browse");
                readerExternal.Pending = new TaskCompletionSource<ExternalFileResult>();
                InvokeReaderToolbarButton(choose); await WaitUntil(() => browse.ExternalOpenState.Busy && !open.IsEnabled && !choose.IsEnabled);
                var count = readerExternal.Calls.Count;
                await browse.OpenExternallyAsync(ExternalFileAction.DefaultApplication);
                Require(readerExternal.Calls.Count == count, "Reader allowed a duplicate launch");
                navigation.SelectDestination("Settings");
                readerExternal.Pending.SetResult(new(ExternalFileOutcome.Cancelled, "stale external result")); readerExternal.Pending = null;
                await Task.Delay(100); Require(!Reader().IsActive && !browse.ExternalOpenState.Visible, "External completion reactivated Reader");
                navigation.SelectDestination("Reader"); await WaitUntil(() => !browse.ExternalOpenState.Busy);
            });
            await RunReaderMutationChecksAsync(window, state, browse, navigation, toolbar, monitor);
        }
        finally { window.Close(); }
    }
    private static void InvokeReaderToolbarButton(Microsoft.UI.Xaml.Controls.AppBarButton button) =>
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(button)
            .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendReaderPointerMessage(nint window, int message, nint wParam, nint lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindow")]
    private static extern nint ReaderInputChild(nint window, uint command);

    private sealed class RecordingReaderExternal : IExternalFileService
    {
        public List<(string Path, ExternalFileAction Action, nint Owner)> Calls { get; } = [];
        public TaskCompletionSource<ExternalFileResult>? Pending;
        public Task<ExternalFileResult> OpenAsync(string path, ExternalFileAction action, nint ownerWindow, CancellationToken cancellationToken = default)
        {
            Calls.Add((path, action, ownerWindow));
            return Pending?.Task ?? Task.FromResult(new ExternalFileResult(ExternalFileOutcome.Opened, "Opened by test service"));
        }
    }

    private sealed class RecordingReaderImages : IReaderImageService
    {
        private readonly FileSystemReaderImageService _actual = new(new FileSystemFileContentService());
        public int Calls, MaximumConcurrent; private int _active;
        public async Task<ReaderImage> DecodeAsync(FileEntry file, int width, CancellationToken token)
        {
            Interlocked.Increment(ref Calls); var active = Interlocked.Increment(ref _active); MaximumConcurrent = Math.Max(MaximumConcurrent, active);
            try { return await _actual.DecodeAsync(file, width, token); }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
