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
        var history = new RootFolderHistoryService(store, availability);
        var thumbnails = new CountingThumbnails();
        var window = new MediFiler3.MainWindow(Microsoft.Extensions.Logging.Abstractions.NullLogger<MediFiler3.MainWindow>.Instance,
            new FileSystemFolderTreeService(), new FileSystemFileListingService(), state, new WicImageDecoder(Files), thumbnails,
            operations, journal, new FileSystemTextContentService(Files), new External(), new Defaults(), settings,
            history, availability, new FileSystemCollisionPreviewService(thumbnails), backups, layout, new WindowsWindowPlacement())
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
                Require(appWindow.TitleBar.PreferredHeightOption == Microsoft.UI.Windowing.TitleBarHeightOption.Standard &&
                    Math.Abs(bounds.Top + bounds.Height / 2 - caption.ActualHeight / 2) < 1,
                    "Title-bar search is not vertically centered at the native height");
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
                Require(fields.Children.OfType<Microsoft.UI.Xaml.Controls.StackPanel>().SelectMany(group => group.Children)
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
                var labelBox = (Microsoft.UI.Xaml.Controls.Viewbox)((Microsoft.UI.Xaml.Controls.StackPanel)button.Content).Children[1];
                var label = (Microsoft.UI.Xaml.Controls.TextBlock)labelBox.Child;
                caption.SetFileResultCount("1 of 30000");
                Require(label.Text == "1 of 30000" && labelBox.MaxWidth <= 55 &&
                    labelBox.StretchDirection == Microsoft.UI.Xaml.Controls.StretchDirection.DownOnly,
                    "The Filters button did not fit the full count on one line");
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
                    Require(!Route(Key.D1, ModifierKeys.None), "A view shortcut consumed a digit typed in search");
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
            await Check("number shortcuts switch to each view in sidebar order", async () =>
            {
                var shortcut = typeof(MediFiler3.MainWindow).GetMethod("TryHandleViewShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var page = (System.Windows.Controls.ContentControl)window.FindName("PageContent");
                var destinations = new[] { (Key.D1, "_homeView"), (Key.D2, "_browseView"),
                    (Key.D3, "_browseView"), (Key.D4, "_browseView"), (Key.D5, "_settingsView") };
                for (var index = 0; index < destinations.Length; index++)
                {
                    var (key, field) = destinations[index];
                    Require((bool)shortcut.Invoke(window, [key, ModifierKeys.None, false])!, $"Missing view shortcut {key}");
                    await WaitUntil(() => ReferenceEquals(page.Content, Field(window, field)) && (index is 0 or 4 ||
                        state.ViewingMode == (ViewingMode)(index - 1)));
                }
                Destination("Browse");
            });
            await Check("recursive results share ordering and support grouped moves across source folders", async () =>
            {
                var scope = System.IO.Directory.CreateDirectory(Path.Combine(Root, "recursive-query")).FullName;
                var second = System.IO.Directory.CreateDirectory(Path.Combine(scope, "Folder 2")).FullName;
                var tenth = System.IO.Directory.CreateDirectory(Path.Combine(scope, "Folder 10")).FullName;
                var direct = Path.Combine(scope, "B.png");
                var firstChild = Path.Combine(second, "A1.png");
                var secondChild = Path.Combine(tenth, "A2.png");
                foreach (var path in new[] { direct, firstChild, secondChild }) File.Copy(Fixtures[".png"].FullPath, path);

                var apply = typeof(MediFiler3.MainWindow).GetMethod("ApplyEditedFileFilter", BindingFlags.Instance | BindingFlags.NonPublic)!;
                void Apply(FileQuery query) => apply.Invoke(window, [query]);
                Apply(FileQuery.AllFiles);
                await LoadRestartRootsAsync(window, [scope, second]);
                await WaitUntil(() => state.ActiveFolder?.Path == scope && state.Files.Count == 1);
                var owner = state.ActiveRoot!;
                var filterButton = (Microsoft.UI.Xaml.Controls.Button)Field(caption, "_fileFilterButton")!;
                var editor = (Microsoft.UI.Xaml.Controls.Flyout)filterButton.Flyout;
                await WaitUntil(() => filterButton.IsLoaded && filterButton.ActualWidth > 0 &&
                    ((Microsoft.UI.Xaml.Controls.Grid)Field(caption, "_fileSearchLayout")!).Visibility == Microsoft.UI.Xaml.Visibility.Visible);
                await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                editor.ShowAt(filterButton);
                await WaitUntil(() => editor.IsOpen);
                var panel = (Microsoft.UI.Xaml.Controls.StackPanel)((Microsoft.UI.Xaml.Controls.ScrollViewer)editor.Content).Content;
                var fields = panel.Children.OfType<Microsoft.UI.Xaml.Controls.StackPanel>().First();
                var scopeOptions = fields.Children.OfType<Microsoft.UI.Xaml.Controls.StackPanel>().First();
                var include = scopeOptions.Children.OfType<Microsoft.UI.Xaml.Controls.CheckBox>().First();
                var groupBySubfolder = scopeOptions.Children.OfType<Microsoft.UI.Xaml.Controls.CheckBox>().Last();
                Require(Equals(groupBySubfolder.Content, "Group files by subfolder") && groupBySubfolder.IsChecked == true,
                    "Recursive results should be grouped by subfolder by default");
                include.IsChecked = true;
                await WaitUntil(() => state.Files.Select(file => file.FullPath).SequenceEqual(new[] { direct, firstChild, secondChild }));
                Require(state.ActiveFolder?.DirectFileCount == 1 && ReferenceEquals(state.ActiveRoot, owner),
                    "Recursive results changed physical counts or ownership for overlapping roots");
                groupBySubfolder.IsChecked = false;
                await WaitUntil(() => state.Files.Select(file => file.FullPath).SequenceEqual(new[] { firstChild, secondChild, direct }));
                editor.Hide();
                await WaitUntil(() => !editor.IsOpen);

                var controller = (NavigationController)Field(browse, "_navigationController")!;
                controller.SelectFile(state.Files[^1]);
                for (var attempt = 0; attempt < 6; attempt++) controller.NavigateByOneFile(1);
                Require(state.ActiveFolder?.Path == scope, "Recursive overscroll crossed into another folder");

                Destination("Reader");
                await WaitUntil(() => state.Reader.Pages.Select(file => file.FullPath).SequenceEqual(new[] { firstChild, secondChild, direct }));
                Destination("Gallery");
                await WaitUntil(() => Field(browse, "_gallery") is WinUiGalleryIsland gallery && gallery.RealizedCount > 0);
                state.GallerySelection.SelectAll(state.Files);
                var move = typeof(BrowseView).GetMethod("RunGalleryFileCommandAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await (Task)move.Invoke(browse, [owner, null])!;
                Require(File.Exists(direct) && !File.Exists(firstChild) && !File.Exists(secondChild) &&
                    File.Exists(Path.Combine(scope, "A1.png")) && File.Exists(Path.Combine(scope, "A2.png")),
                    "Recursive move did not handle both physical source folders and a same-folder item");
                await browse.UndoLastOperationAsync();
                Require(File.Exists(firstChild) && File.Exists(secondChild) && File.Exists(direct) &&
                    state.Files.Any(file => file.FullPath == firstChild) && state.Files.Any(file => file.FullPath == secondChild),
                    "Grouped Undo did not restore the physical source paths and recursive results");
                await browse.RedoLastOperationAsync();
                Require(!File.Exists(firstChild) && !File.Exists(secondChild) && File.Exists(direct),
                    "Grouped Redo did not replay both real source paths");
                var savedRecursive = new SavedFileFilter(Guid.NewGuid(), "Recursive combined", new FileQuery(IncludeSubfolders: true, CombineFolders: true));
                settings.Update(current => current with { SavedFileFilters = (current.SavedFileFilters ?? []).Append(savedRecursive).ToArray() });
                await settings.FlushAsync();
                Require((await ((IApplicationSettingsStore)store).LoadAsync()).SavedFileFilters!.Contains(savedRecursive),
                    "Saved recursive criteria did not survive a settings reload");
                Apply(FileQuery.AllFiles);
                await WaitUntil(() => state.Files.Count == 3 && state.Files.All(file => Path.GetDirectoryName(file.FullPath) == scope));
                var overlappingRoot = state.Roots.Single(root => StringComparer.OrdinalIgnoreCase.Equals(root.Path, second));
                var activate = typeof(BrowseView).GetMethod("ActivateFolderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await (Task)activate.Invoke(browse, [overlappingRoot, null, 0])!;
                await browse.RefreshAsync();
                Require(ReferenceEquals(state.ActiveRoot, overlappingRoot) && ReferenceEquals(state.ActiveFolder, overlappingRoot),
                    "F5 switched an overlapping root to its ancestor's ownership");
            });
            await Check("F7 and F6 change one prefix on Browse and Gallery files", async () =>
            {
                var folder = Directory.CreateDirectory(Path.Combine(Root, "prefix-shortcuts")).FullName;
                var first = Path.Combine(folder, "first.png");
                var second = Path.Combine(folder, "second.png");
                File.Copy(Fixtures[".png"].FullPath, first);
                File.Copy(Fixtures[".png"].FullPath, second);
                await LoadRestartRootsAsync(window, [folder]);
                await WaitUntil(() => state.CurrentFile?.FullPath == first && (int)Field(browse, "_reconciliationDeferrals")! == 0);
                browse.FocusActiveViewer();
                await WaitUntil(() => !caption.IsFileSearchFocused && !caption.IsFileFilterEditorOpen);
                var shortcut = typeof(MediFiler3.MainWindow).GetMethod("TryHandleViewShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!;
                void Press(Key key) => Require((bool)shortcut.Invoke(window, [key, ModifierKeys.None, false])!, $"Missing prefix shortcut {key}");
                Press(Key.F7);
                await WaitUntil(() => state.CurrentFile?.Name == "+first.png" && Field(browse, "_galleryCommandCancellation") is null);
                Destination("Gallery");
                await WaitUntil(() => Field(browse, "_gallery") is WinUiGalleryIsland gallery && gallery.RealizedCount > 0);
                state.GallerySelection.SelectAll(state.Files);
                Press(Key.F7);
                await WaitUntil(() => state.Files.Any(file => file.Name == "++first.png") &&
                    state.Files.Any(file => file.Name == "+second.png") && Field(browse, "_galleryCommandCancellation") is null);
                state.GallerySelection.SelectAll(state.Files);
                Press(Key.F6);
                await WaitUntil(() => state.Files.Any(file => file.Name == "+first.png") &&
                    state.Files.Any(file => file.Name == "second.png") && Field(browse, "_galleryCommandCancellation") is null);
                Destination("Reader");
                Require(!(bool)shortcut.Invoke(window, [Key.F7, ModifierKeys.None, false])!, "F7 was enabled in Reader");
                Destination("Browse");
            });
            await Check("sidebar quick folders mirror favorites, replace roots in Browse, and support moves", async () =>
            {
                var source = Directory.CreateDirectory(Path.Combine(Root, "quick-source")).FullName;
                var destination = Directory.CreateDirectory(Path.Combine(Root, "Art & Books")).FullName;
                var original = Path.Combine(source, "sample.png");
                var moved = Path.Combine(destination, "sample.png");
                File.Copy(Fixtures[".png"].FullPath, original);
                await LoadRestartRootsAsync(window, [source]);
                await WaitUntil(() => state.CurrentFile?.FullPath == original);
                var initialRoot = state.ActiveRoot;
                await history.RecordOpenedRootsAsync([destination]);
                await history.SetFavoriteAsync(destination, true);
                var sidebar = (Microsoft.UI.Xaml.Controls.NavigationView)Field(navigation, "_navigationView")!;
                var quickButtons = (Microsoft.UI.Xaml.Controls.StackPanel)Field(navigation, "_quickFolderButtons")!;
                var scroll = (Microsoft.UI.Xaml.Controls.ScrollViewer)Field(navigation, "_quickFolderScroll")!;
                bool IsCentered(Microsoft.UI.Xaml.Controls.Button button) => Math.Abs(
                    button.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point(button.ActualWidth / 2, 0)).X
                    - scroll.ActualWidth / 2) < 1;
                await WaitUntil(() => quickButtons.Children.OfType<Microsoft.UI.Xaml.Controls.Button>().Any(entry => Equals(entry.Tag, destination)));
                var item = quickButtons.Children.OfType<Microsoft.UI.Xaml.Controls.Button>().Single(entry => Equals(entry.Tag, destination));
                Require(Equals(item.Content, "ABo") &&
                    ((string)Microsoft.UI.Xaml.Controls.ToolTipService.GetToolTip(item)).Contains(destination) &&
                    sidebar.MenuItems.Count == 5, "Quick folder caption, tooltip or placement was incorrect");
                await WaitUntil(() => item.ActualWidth > 0 && scroll.ActualHeight > 0);
                Require(scroll.ScrollableHeight == 0 && IsCentered(item), "Quick folder was off-center in a list that fits");

                var move = (Action<string>)Field(navigation, "QuickFolderMoveRequested")!;
                move(destination);
                await WaitUntil(() => File.Exists(moved) && state.Files.Count == 0);
                Require(state.Roots.Count == 1 && ReferenceEquals(state.ActiveRoot, initialRoot) && !File.Exists(original),
                    "Moving to the quick folder changed the workspace or lost the source journal context");
                await browse.UndoLastOperationAsync();
                await WaitUntil(() => state.CurrentFile?.FullPath == original && !File.Exists(moved));

                File.Copy(Fixtures[".png"].FullPath, moved);
                await LoadRestartRootsAsync(window, [source, destination]);
                await WaitUntil(() => state.Roots.Count == 2 && state.CurrentFile?.FullPath == original);
                Destination("Reader");
                await WaitUntil(() => browse.IsReader);
                var open = (Action<string>)Field(navigation, "QuickFolderOpenRequested")!;
                open(destination);
                await WaitUntil(() => state.ActiveRoot?.Path == destination && state.CurrentFile?.FullPath == moved);
                Require(state.Roots.Count == 1 && state.ViewingMode == ViewingMode.Browse,
                    "Opening an already loaded Quick folder did not replace all roots and switch to Browse");
                var accent = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
                Require(item.Background is Microsoft.UI.Xaml.Media.SolidColorBrush active && active.Color == accent &&
                    item.Resources["ButtonBackgroundPointerOver"] is Microsoft.UI.Xaml.Media.SolidColorBrush hover && hover.Color == accent,
                    "The active Quick folder did not use the accent color");
                open(source);
                await WaitUntil(() => state.ActiveRoot?.Path == source && state.CurrentFile?.FullPath == original);
                Require(state.Roots.Count == 1, "Opening a Quick folder outside the workspace did not replace the existing root");
                Require(item.Background is not Microsoft.UI.Xaml.Media.SolidColorBrush cleared || cleared.Color != accent,
                    "The previous Quick folder stayed highlighted");
                await history.SetFavoriteAsync(destination, false);
                await WaitUntil(() => !quickButtons.Children.Contains(item));
                navigation.SetQuickFolders(new[] { "lower-case", "Book 5", "A Big Collection 7", "---" }
                    .Select(name => Path.Combine(Root, name))
                    .Concat(Enumerable.Range(1, 30).Select(index => Path.Combine(Root, $"Favorite {index}"))));
                Require(quickButtons.Children.OfType<Microsoft.UI.Xaml.Controls.Button>().Take(4)
                    .Select(button => (string)button.Content).SequenceEqual(new[] { "low", "B5", "ABC", "" }),
                    "Quick folder initials did not follow the naming rules");
                var reader = (Microsoft.UI.Xaml.Controls.NavigationViewItem)Field(navigation, "_readerItem")!;
                var settingsItem = (Microsoft.UI.Xaml.Controls.NavigationViewItem)Field(navigation, "_settingsItem")!;
                var root = (Microsoft.UI.Xaml.Controls.Grid)Field(navigation, "_sidebarRoot")!;
                await WaitUntil(() => scroll.ScrollableHeight > 0 &&
                    Field(navigation, "_quickFolderScrollBar") is Microsoft.UI.Xaml.Controls.Primitives.ScrollBar);
                var bar = (Microsoft.UI.Xaml.Controls.Primitives.ScrollBar)Field(navigation, "_quickFolderScrollBar")!;
                var first = (Microsoft.UI.Xaml.Controls.Button)quickButtons.Children[0];
                var setHover = typeof(MediFiler3.Interop.WinUiNavigationIsland).GetMethod(
                    "SetQuickFolderScrollHovered", BindingFlags.Instance | BindingFlags.NonPublic)!;
                setHover.Invoke(navigation, [false]);
                Require(IsCentered(first) && bar.Opacity == 0 && !bar.IsHitTestVisible, "The idle scrollbar affected centered buttons");
                setHover.Invoke(navigation, [true]);
                Require(IsCentered(first) && bar.Opacity > 0 && bar.IsHitTestVisible, "Hover did not reveal the scrollbar over centered buttons");
                setHover.Invoke(navigation, [false]);
                Require(bar.Opacity == 0 && !bar.IsHitTestVisible, "The scrollbar stayed visible after leaving");
                double Top(Microsoft.UI.Xaml.FrameworkElement element) => element.TransformToVisual(root)
                    .TransformPoint(new Windows.Foundation.Point()).Y;
                var readerTop = Top(reader);
                var settingsTop = Top(settingsItem);
                Require(Top(scroll) >= readerTop + reader.ActualHeight - 1 &&
                    Top(scroll) + scroll.ActualHeight <= settingsTop + 1, "Quick folders overlap pinned navigation");
                scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
                await WaitUntil(() => scroll.VerticalOffset > 0);
                Require(Math.Abs(Top(reader) - readerTop) < 1 && Math.Abs(Top(settingsItem) - settingsTop) < 1,
                    "Scrolling Quick folders moved the pinned navigation");
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
