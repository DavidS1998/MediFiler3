using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using MediFiler3.Application.Files;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Persistence;
using MediFiler3.Infrastructure.Files;
using MediFiler3.Infrastructure.Folders;
using MediFiler3.Infrastructure.Operations;
using MediFiler3.Infrastructure.Persistence;
using MediFiler3.Interop;
using MediFiler3.Services;
using MediFiler3.Views;

internal static partial class Program
{
    [DllImport("user32.dll", EntryPoint = "GetFocus")]
    private static extern nint GetFileQueryTestFocus();

    private static async Task RunFileQueryChecksAsync()
    {
        var directory = Path.Combine(Root, "file-query"); Directory.CreateDirectory(directory);
        for (var i = 0; i < 80; i++) File.Copy(Fixtures[".png"].FullPath, Path.Combine(directory, $"{i:D3}.png"));
        var db = new SqliteDatabase(Path.Combine(Root, "file-query.db")); var store = new SqliteApplicationStateStore(db);
        var settings = new ApplicationSettingsService(store); await settings.InitializeAsync();
        await store.SaveAsync(new WindowLayout(120, 100, 1100, 700, true, 300));
        var layout = new WindowLayoutService(store); await layout.InitializeAsync();
        var journal = new SqliteFileOperationJournal(db); var identities = new WindowsFileIdentityProvider();
        var backups = new FileSystemOperationBackupStore(journal, identities, Path.Combine(Root, "file-query-backups"));
        var operations = new FileSystemFileOrganizationService(journal, identities, backupStore: backups);
        var availability = new FileSystemRootFolderAvailabilityService(); var state = new NavigationSessionState();
        var thumbnails = new CountingThumbnails();
        var window = new MediFiler3.MainWindow(Microsoft.Extensions.Logging.Abstractions.NullLogger<MediFiler3.MainWindow>.Instance,
            new FileSystemFolderTreeService(), new FileSystemFileListingService(), state, new WicImageDecoder(Files), thumbnails,
            operations, journal, new FileSystemTextContentService(Files), new External(), new Defaults(), settings,
            new RootFolderHistoryService(store, availability), availability, new FileSystemCollisionPreviewService(thumbnails), backups, layout, new WindowsWindowPlacement())
        { Opacity = 0, ShowInTaskbar = false };
        window.Show();
        ((System.Windows.Threading.DispatcherTimer)Field(window, "_folderHoverTimer")!).Stop();
        var browse = (BrowseView)Field(window, "_browseView")!;
        var caption = (WinUiTitleBarIsland)window.FindName("TitleBarIsland");
        var navigation = (WinUiNavigationIsland)window.FindName("NavigationIsland");
        void Destination(string destination) => navigation.SelectDestination(destination);
        try
        {
            await Check("startup-maximized title supports native caption input", async () =>
            {
                var title = (WinUiTitleBarIsland)window.FindName("TitleBarIsland");
                await WaitUntil(() => window.WindowState == WindowState.Maximized && title.ActualWidth > 0 && title.ActualHeight > 0);
                var point = title.PointToScreen(new Point(title.ActualWidth / 2, title.ActualHeight / 2));
                var coordinates = (nint)(((int)point.X & 0xffff) | (((int)point.Y & 0xffff) << 16));
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                Require(SendLayoutMessage(hwnd, 0x0084, 0, coordinates) == 2, "Maximized title did not return HTCAPTION");
                SendLayoutMessage(hwnd, 0x00A3, 2, coordinates);
                await WaitUntil(() => window.WindowState == WindowState.Normal);
            });
            window.WindowState = WindowState.Normal;
            await LoadRestartRootsAsync(window, [directory]);
            await WaitUntil(() => state.Files.Count == 80 && browse.CanZoom);
            await Check("native search shares direct-folder results, prunes Gallery selection and persists filter history", async () =>
            {
                var search = (Microsoft.UI.Xaml.Controls.TextBox)Field(caption, "_fileSearch")!;
                await WaitUntil(() => search.IsLoaded && search.ActualWidth > 0);
                Require(search.Visibility == Microsoft.UI.Xaml.Visibility.Visible && search.ActualWidth >= search.MinWidth - 1,
                    $"Search was clipped or hidden in Browse. Width: {search.ActualWidth}.");
                var bounds = (Rect)Field(caption, "_searchBounds")!;
                var appWindow = (Microsoft.UI.Windowing.AppWindow)Field(window, "_appWindow")!;
                var filters = (Rect)Field(caption, "_filterBounds")!;
                var filterButton = (Microsoft.UI.Xaml.Controls.Button)Field(caption, "_fileFilterButton")!;
                Require(appWindow.TitleBar.PreferredHeightOption == Microsoft.UI.Windowing.TitleBarHeightOption.Tall &&
                    Math.Abs(bounds.Top + bounds.Height / 2 - caption.ActualHeight / 2) < 1,
                    "Title-bar search is not vertically centered at the tall native height");
                Require(filters.Left > bounds.Right &&
                    ((Microsoft.UI.Xaml.Controls.StackPanel)filterButton.Content).Children[0] is Microsoft.UI.Xaml.Controls.FontIcon,
                    "The filter icon button is not to the right of search");
                Require(search.Background is Microsoft.UI.Xaml.Media.SolidColorBrush searchFill && searchFill.Color.A >= 230,
                    "The search field is too translucent for a bright caption");
                var center = bounds.Left + bounds.Width / 2;
                var toolbar = (WinUiToolbarIsland)window.FindName("ToolbarIsland");
                await WaitUntil(() => toolbar.PrimaryCommandCenter is not null);
                var commands = (Microsoft.UI.Xaml.Controls.CommandBar)Field(toolbar, "_primaryCommands")!;
                var toolbarRoot = (Microsoft.UI.Xaml.Controls.Grid)Field(toolbar, "_rootElement")!;
                var commandCenter = commands.TransformToVisual(toolbarRoot)
                    .TransformPoint(new Windows.Foundation.Point(commands.ActualWidth / 2, 0)).X;
                var expectedCenter = toolbar.TranslatePoint(new Point(commandCenter, 0), window).X;
                var actualCenter = caption.TranslatePoint(new Point(center, 0), window).X;
                Require(Math.Abs(actualCenter - expectedCenter) < 2, "Search is not centered above the toolbar commands");
                nint HitCoordinates(Point local)
                {
                    var screen = caption.PointToScreen(local);
                    return (nint)(((int)screen.X & 0xffff) | (((int)screen.Y & 0xffff) << 16));
                }
                var searchPoint = HitCoordinates(new Point(center, bounds.Top + bounds.Height / 2));
                var passivePoint = HitCoordinates(new Point(5, bounds.Top + bounds.Height / 2));
                var owner = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                Require(SendLayoutMessage(caption.Handle, 0x0084, 0, searchPoint) == 1 &&
                    SendLayoutMessage(owner, 0x0084, 0, searchPoint) == 1 &&
                    SendLayoutMessage(owner, 0x0084, 0, HitCoordinates(new Point(filters.Left + 12, filters.Top + 12))) == 1 &&
                    SendLayoutMessage(owner, 0x0084, 0, passivePoint) == 2,
                    "Caption hit testing does not distinguish search from the draggable area");
                var outsideFilter = state.Files[79];
                state.SetCurrentFile(state.Files[22]);
                search.Text = "02";
                await WaitUntil(() => state.Files.Count == 11 && state.CurrentFile?.Name == "029.png");
                search.Text = "022.png";
                await WaitUntil(() => state.Files.Count == 1 && state.CurrentFile?.Name == "022.png");
                Require(state.ActiveFolder?.DirectFileCount == 80 &&
                    Field(caption, "_filterCountLabel") is Microsoft.UI.Xaml.Controls.TextBlock
                        { Text: "1 of 80", Visibility: Microsoft.UI.Xaml.Visibility.Visible }, "Physical and filtered counts were mixed");
                Destination("Gallery");
                Microsoft.UI.Xaml.Controls.GridView? Grid() => Field(browse, "_gallery") is WinUiGalleryIsland gallery
                    ? Field(gallery, "_grid") as Microsoft.UI.Xaml.Controls.GridView : null;
                await WaitUntil(() => Grid()?.Items.Count == 1 && HasGalleryImage(Grid()!));
                search.Text = "";
                await WaitUntil(() => Grid()?.Items.Count == 80 && HasGalleryImage(Grid()!));
                state.GallerySelection.SetSelected(state.Files, state.Files[22], true);
                state.GallerySelection.SetSelected(state.Files, outsideFilter, true);
                search.Text = "022.png";
                await WaitUntil(() => Grid()?.Items.Count == 1 && HasGalleryImage(Grid()!));
                Require(state.GallerySelection.Count == 1 && state.GallerySelection.IsSelected(state.Files[0]),
                    "A hidden Gallery item remained selected");
                Destination("Reader");
                await WaitUntil(() => state.Reader.Pages.Count == 1 && state.Reader.Anchor?.Path == state.Files[0].FullPath);
                search.Text = "043.png";
                await WaitUntil(() => state.Reader.Pages.Count == 1 && state.Reader.Anchor?.Path.EndsWith("043.png") == true);
                Require(state.CurrentFile?.Name == "043.png", "Reader did not follow the shared query");

                var filter = new SavedFileFilter(Guid.NewGuid(), "Page 43", new FileQuery("043.png"));
                settings.Update(current => current with { SavedFileFilters = [filter] });
                var select = typeof(MediFiler3.MainWindow).GetMethod("SelectFileFilter", BindingFlags.Instance | BindingFlags.NonPublic)!;
                select.Invoke(window, [filter.Id]);
                search.Text = "044.png";
                await WaitUntil(() => state.CurrentFile?.Name == "044.png");
                await settings.FlushAsync();
                var persisted = await ((IApplicationSettingsStore)store).LoadAsync();
                Require(persisted.SavedFileFilters?.Single() == filter && persisted.RecentFileFilters?.Single() == filter.Id,
                    "Temporary edits changed the saved definition or the recent filter was lost");
                var added = Path.Combine(directory, "new-044.png");
                File.Copy(Fixtures[".png"].FullPath, added);
                Require(state.Files.Count == 1, "The query enumerated the folder again");
                var refresh = typeof(MediFiler3.MainWindow).GetMethod("RefreshBrowseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await (Task)refresh.Invoke(window, null)!;
                await WaitUntil(() => state.Files.Count == 2 && state.Reader.Pages.Count == 2);
                Require(state.CurrentFile?.Name == "044.png", "Filtered refresh moved the surviving Reader anchor");
                Destination("Home");
                Require(search.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed &&
                    ((Microsoft.UI.Xaml.Controls.Grid)Field(caption, "_fileSearchLayout")!).Visibility ==
                    Microsoft.UI.Xaml.Visibility.Collapsed, "Search remained visible on Home");
                Destination("Browse");
                await WaitUntil(() => state.CurrentFile?.Name == "044.png" && browse.CanZoom);
                select.Invoke(window, [null]);
                await WaitUntil(() => state.Files.Count == 81);
            });
            await Check("filter flyout edits and clears the query directly", async () =>
            {
                settings.Update(current => current with
                {
                    SavedFileFilters = current.SavedFileFilters!.Concat(Enumerable.Range(1, 100)
                        .Select(index => new SavedFileFilter(Guid.NewGuid(), index == 1 ? "Short" : $"Tag with longer text {index:D3}", FileQuery.AllFiles))).ToArray()
                });
                typeof(MediFiler3.MainWindow).GetMethod("UpdateFileFilterToolbar", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, null);
                var button = (Microsoft.UI.Xaml.Controls.Button)Field(caption, "_fileFilterButton")!;
                var flyout = (Microsoft.UI.Xaml.Controls.Flyout)button.Flyout;
                var closed = false;
                flyout.Closed += (_, _) => closed = true;
                async Task<Microsoft.UI.Xaml.Controls.StackPanel> OpenEditor()
                {
                    closed = false;
                    flyout.ShowAt(button);
                    await WaitUntil(() => flyout.IsOpen && flyout.Content is Microsoft.UI.Xaml.Controls.ScrollViewer { ActualHeight: > 200 });
                    Require(!flyout.ShouldConstrainToRootBounds, "The editor is constrained to the toolbar island");
                    var scroll = (Microsoft.UI.Xaml.Controls.ScrollViewer)flyout.Content;
                    Microsoft.UI.Xaml.DependencyObject? ancestor = scroll;
                    while (ancestor is not null && ancestor is not Microsoft.UI.Xaml.Controls.FlyoutPresenter)
                        ancestor = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(ancestor);
                    Require(ancestor is Microsoft.UI.Xaml.Controls.FlyoutPresenter presenter &&
                        presenter.CornerRadius.TopLeft == 8 && presenter.ActualWidth <= 471,
                        "Filter flyout lost its rounded corners or compact width");
                    return (Microsoft.UI.Xaml.Controls.StackPanel)scroll.Content;
                }

                var panel = await OpenEditor();
                var fields = panel.Children.OfType<Microsoft.UI.Xaml.Controls.StackPanel>().First();
                var names = fields.Children.OfType<Microsoft.UI.Xaml.Controls.Grid>().First();
                names.Children.OfType<Microsoft.UI.Xaml.Controls.TextBox>().First().Text = "046";
                Require(fields.Children.OfType<Microsoft.UI.Xaml.Controls.StackPanel>().Single().Children
                    .OfType<Microsoft.UI.Xaml.Controls.RadioButton>().Count() == 4, "Sort options are not radio buttons");
                await WaitUntil(() => flyout.IsOpen && state.Files.Count == 1 && state.CurrentFile?.Name == "046.png");
                var tagList = panel.Children.OfType<Microsoft.UI.Xaml.Controls.ScrollViewer>().Single();
                var tags = (Microsoft.UI.Xaml.Controls.ItemsRepeater)tagList.Content;
                await WaitUntil(() => tags.TryGetElement(0) is Microsoft.UI.Xaml.Controls.StackPanel && tagList.ScrollableHeight > 0);
                var editorScroll = (Microsoft.UI.Xaml.Controls.ScrollViewer)flyout.Content;
                var rows = (System.Collections.IList)tags.ItemsSource;
                Require(tags.TryGetElement(rows.Count - 1) is null && editorScroll.ScrollableHeight <= 1 &&
                    editorScroll.ScrollableWidth <= 1 && tagList.ScrollableWidth <= 1,
                    "Large tag list lost virtualization or forced the editor itself to scroll");
                var tag = ((Microsoft.UI.Xaml.Controls.StackPanel)tags.TryGetElement(0)).Children
                    .OfType<Microsoft.UI.Xaml.Controls.Button>().First();
                InvokeFilterButton(tag);
                await WaitUntil(() => flyout.IsOpen && state.CurrentFile?.Name == "043.png");
                var chips = (Microsoft.UI.Xaml.Controls.StackPanel)Field(caption, "_recentFilterChips")!;
                var chip = chips.Children.OfType<Microsoft.UI.Xaml.Controls.Button>().Single();
                var accent = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
                Require(tag.Background is Microsoft.UI.Xaml.Media.SolidColorBrush tagBrush && tagBrush.Color == accent &&
                    chip.Background is Microsoft.UI.Xaml.Media.SolidColorBrush chipBrush &&
                    chipBrush.Color.R == accent.R && chipBrush.Color.G == accent.G && chipBrush.Color.B == accent.B &&
                    chipBrush.Color.A is >= 230 and < 255,
                    "The selected filter tag and toolbar chip did not share the accent color");
                var searchBox = (Microsoft.UI.Xaml.Controls.TextBox)Field(caption, "_fileSearch")!;
                foreach (var control in new Microsoft.UI.Xaml.Controls.Control[] { searchBox, button, chip })
                {
                    var prefix = ReferenceEquals(control, searchBox) ? "TextControl" : "Button";
                    foreach (var suffix in new[] { "", "PointerOver", "Pressed", "Focused", "Disabled" })
                        Require(control.Resources[$"{prefix}Background{suffix}"] is Microsoft.UI.Xaml.Media.SolidColorBrush
                            { Color.A: >= 230 and < 255 }, $"Title-bar {prefix} {suffix} lost its background opacity");
                }
                var labels = (Microsoft.UI.Xaml.Controls.StackPanel)((Microsoft.UI.Xaml.Controls.StackPanel)button.Content).Children[1];
                Require(labels.Children[0] is Microsoft.UI.Xaml.Controls.TextBlock { Text: "Filters" },
                    "The Filters button changed its label when a filter was selected");
                Require(Math.Abs(tag.CornerRadius.TopLeft - tag.ActualHeight * .45) < .5 &&
                    Math.Abs(chip.CornerRadius.TopLeft - chip.ActualHeight * .45) < .5,
                    "The flyout tag and toolbar tag are not pill-shaped");
                Require(Microsoft.UI.Xaml.VisualStateManager.GoToState(tag, "PointerOver", false) &&
                    Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(tag, 0) is Microsoft.UI.Xaml.Controls.ContentPresenter
                    { Background: Microsoft.UI.Xaml.Media.SolidColorBrush hover } && hover.Color == accent,
                    "Selected tag became grey in the native hover state");
                Microsoft.UI.Xaml.VisualStateManager.GoToState(tag, "Normal", false);
                var relativeWidth = ((Microsoft.UI.Xaml.Controls.StackPanel)tags.TryGetElement(0)).Children
                    .OfType<Microsoft.UI.Xaml.Controls.Button>().Select(item => item.Width).Distinct().Count();
                Require(relativeWidth > 1, "Tag widths did not respond to their text");
                InvokeFilterButton(tag);
                await WaitUntil(() => flyout.IsOpen && state.Files.Count == 81);
                InvokeFilterButton(panel.Children.OfType<Microsoft.UI.Xaml.Controls.Grid>().Single(), "Clear filter");
                await WaitUntil(() => closed && state.Files.Count == 81);
                InvokeFilterButton(chip);
                await WaitUntil(() => state.CurrentFile?.Name == "043.png");
                InvokeFilterButton(chip);
                await WaitUntil(() => state.Files.Count == 81);
            });
            await Check("toolbar tag slider persists and recent tags keep their positions", async () =>
            {
                var filters = settings.Current.SavedFileFilters!.Take(3).ToArray();
                settings.Update(current => current with { RecentFileFilters = filters.Select(filter => filter.Id).ToArray() });
                typeof(MediFiler3.MainWindow).GetMethod("UpdateFileFilterToolbar", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, null);
                var chips = (Microsoft.UI.Xaml.Controls.StackPanel)Field(caption, "_recentFilterChips")!;
                await WaitUntil(() => chips.Children.Count == 3);
                var positions = chips.Children.ToArray();
                InvokeFilterButton((Microsoft.UI.Xaml.Controls.Button)positions[2]);
                await WaitUntil(() => settings.Current.RecentFileFilters![0] == filters[2].Id);
                Require(chips.Children.SequenceEqual(positions), "Selecting an existing tag moved it on the toolbar");

                Destination("Settings");
                var settingsView = (SettingsView)Field(window, "_settingsView")!;
                var settingsIsland = settingsView.FindName("CarouselSliderIsland")!;
                await WaitUntil(() => Field(settingsIsland, "_toolbarFilterTagSlider") is Microsoft.UI.Xaml.Controls.Slider);
                var slider = (Microsoft.UI.Xaml.Controls.Slider)Field(settingsIsland, "_toolbarFilterTagSlider")!;
                Require(slider.Value == 3, "Toolbar tag slider did not default to three");
                slider.Value = 1;
                await settings.FlushAsync();
                Require((await ((IApplicationSettingsStore)store).LoadAsync()).ToolbarFilterTagCount == 1,
                    "Toolbar tag slider did not persist");
                Destination("Browse");
                await WaitUntil(() => chips.Children.Count == 1);
            });
            await Check("native search owns editor keys and Escape returns to the fullscreen viewer", async () =>
            {
                var previousForeground = GetLayoutForegroundWindow();
                try
                {
                    window.Activate();
                    window.ToggleFullscreen();
                    ((System.Windows.Threading.DispatcherTimer)Field(window, "_fullscreenTimer")!).Stop();
                    var shortcut = typeof(MediFiler3.MainWindow).GetMethod("TryHandleViewShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    bool Route(Key key, ModifierKeys modifiers) => (bool)shortcut.Invoke(window, [key, modifiers, false])!;
                    Require(Route(Key.F, ModifierKeys.Control), "Ctrl+F did not route to search");
                    await WaitUntil(() => caption.IsFileSearchFocused);
                    var search = (Microsoft.UI.Xaml.Controls.TextBox)Field(caption, "_fileSearch")!;
                    var debounce = (System.Windows.Threading.DispatcherTimer)Field(window, "_fileSearchDebounce")!;
                    debounce.Interval = TimeSpan.FromSeconds(30);
                    search.Text = "025.png";
                    await WaitUntil(() => debounce.IsEnabled);
                    Require(!Route(Key.Left, ModifierKeys.None), "Viewer navigation consumed the search cursor key");
                    Require(!Route(Key.A, ModifierKeys.Control), "Viewer selection consumed search Ctrl+A");
                    var input = GetFileQueryTestFocus();
                    Require(input != 0 && PostReaderKeyboardMessage(input, 0x0100, 0x0D, 1) &&
                        PostReaderKeyboardMessage(input, 0x0101, 0x0D, unchecked((nint)0xC0000001)),
                        "Could not queue native Enter");
                    await WaitUntil(() => !debounce.IsEnabled);
                    await WaitUntil(() => state.Files.Count == 1 && state.CurrentFile?.Name == "025.png");
                    Require(PostReaderKeyboardMessage(input, 0x0100, 0x1B, 1) &&
                        PostReaderKeyboardMessage(input, 0x0101, 0x1B, unchecked((nint)0xC0000001)),
                        "Could not queue native Escape");
                    await WaitUntil(() => !caption.IsFileSearchFocused);
                    Require(search.Text == "025.png" && window.IsFullscreen &&
                        ((ViewerHostView)browse.FindName("ViewerHost")).IsKeyboardFocusWithin,
                        "Escape lost the query, exited fullscreen, or failed to focus the viewer");
                }
                finally
                {
                    if (window.IsFullscreen) window.ToggleFullscreen();
                    if (previousForeground != 0) SetLayoutForegroundWindow(previousForeground);
                }
            });
        }
        finally { window.Close(); }
    }

    private static void InvokeFilterButton(Microsoft.UI.Xaml.Controls.Panel panel, string label)
    {
        var button = panel.Children.OfType<Microsoft.UI.Xaml.Controls.Button>().Single(item => Equals(item.Content, label));
        InvokeFilterButton(button);
    }

    private static void InvokeFilterButton(Microsoft.UI.Xaml.Controls.Button button)
    {
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)
            .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
    }
}
