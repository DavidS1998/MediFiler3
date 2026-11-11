using System.IO;
using System.Windows;
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
    private static async Task RunGalleryResumeChecksAsync()
    {
        var directory = Path.Combine(Root, "gallery-resume"); Directory.CreateDirectory(directory);
        for (var i = 0; i < 150; i++) File.Copy(Fixtures[".png"].FullPath, Path.Combine(directory, $"{i:D3}.png"));
        var database = new SqliteDatabase(Path.Combine(Root, "gallery-resume.db"));
        var store = new SqliteApplicationStateStore(database);
        var settings = new ApplicationSettingsService(store); await settings.InitializeAsync();
        var layout = new WindowLayoutService(store); await layout.InitializeAsync();
        var journal = new SqliteFileOperationJournal(database); var identities = new WindowsFileIdentityProvider();
        var backups = new FileSystemOperationBackupStore(journal, identities, Path.Combine(Root, "gallery-resume-backups"));
        var operations = new FileSystemFileOrganizationService(journal, identities, backupStore: backups);
        var availability = new FileSystemRootFolderAvailabilityService();
        var history = new RootFolderHistoryService(store, availability);
        var state = new NavigationSessionState(); var monitor = new RecordingResumeMonitor();
        using var thumbnails = new FileSystemThumbnailService(cachePath: Path.Combine(Root, "gallery-resume-cache"));
        var window = new MediFiler3.MainWindow(Microsoft.Extensions.Logging.Abstractions.NullLogger<MediFiler3.MainWindow>.Instance,
            new FileSystemFolderTreeService(), new FileSystemFileListingService(), state, new WicImageDecoder(Files), thumbnails,
            operations, journal, new FileSystemTextContentService(Files), new External(), new Defaults(), settings,
            history, availability, new FileSystemCollisionPreviewService(thumbnails), backups, layout, new WindowsWindowPlacement(), monitor)
            { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        window.Show();
        ((System.Windows.Threading.DispatcherTimer)Field(window, "_folderHoverTimer")!).Stop();
        var browse = (BrowseView)Field(window, "_browseView")!;
        var navigation = (WinUiNavigationIsland)window.FindName("NavigationIsland");
        Microsoft.UI.Xaml.Controls.GridView Grid() => (Microsoft.UI.Xaml.Controls.GridView)Field(Field(browse, "_gallery")!, "_grid")!;
        Microsoft.UI.Xaml.Media.ImageSource? Preview(string path)
        {
            var file = state.Files.FirstOrDefault(f => f.FullPath == path);
            if (file is null || Grid().ContainerFromItem(file) is not Microsoft.UI.Xaml.Controls.GridViewItem item ||
                item.ContentTemplateRoot is not Microsoft.UI.Xaml.Controls.Grid root) return null;
            return ((Microsoft.UI.Xaml.Controls.Image)root.Children[1]).Source;
        }
        try
        {
            await LoadRestartRootsAsync(window, [directory]);
            await WaitUntil(() => state.Files.Count == 150 && monitor.InitialReconciliations >= 1);
            await Check("First Gallery entry via Home reveals the distant Browse file after native loading", async () =>
            {
                var target = state.Files[125];
                ((NavigationController)Field(browse, "_navigationController")!).SelectFile(target);
                navigation.SelectDestination("Home");
                navigation.SelectDestination("Gallery");
                await WaitUntil(() => Field(browse, "_gallery") is not null && Field(Field(browse, "_gallery")!, "_grid") is not null && ResumeTargetVisible(Grid(), target));
                Require(ReferenceEquals(state.CurrentFile, target) && ReferenceEquals(Grid().SelectedItem, target), "Initial Gallery entry lost the Browse file");
            });
            navigation.SelectDestination("Browse");
            ((NavigationController)Field(browse, "_navigationController")!).SelectFile(state.Files[0]);
            navigation.SelectDestination("Gallery");
            var first = Path.Combine(directory, "000.png"); var other = Path.Combine(directory, "001.png");
            await WaitUntil(() => Field(browse, "_gallery") is not null && Field(Field(browse, "_gallery")!, "_grid") is not null && Preview(first) is not null && Preview(other) is not null);
            foreach (var destination in new[] { "Home", "Settings" })
                await Check($"Gallery return from {destination} keeps unchanged previews after real watcher startup", async () =>
                {
                    var image = Preview(first); var otherImage = Preview(other); var selected = state.CurrentFile;
                    var listing = Grid().ItemsSource; var completed = monitor.InitialReconciliations;
                    navigation.SelectDestination(destination);
                    navigation.SelectDestination("Gallery");
                    await WaitUntil(() => monitor.InitialReconciliations > completed);
                    await Task.Delay(100); // Let any incorrect invalidation's asynchronous thumbnail work publish.
                    Require(ReferenceEquals(image, Preview(first)) && ReferenceEquals(otherImage, Preview(other)), "Routine watcher startup invalidated unchanged Gallery previews");
                    Require(ReferenceEquals(listing, Grid().ItemsSource) && ReferenceEquals(selected, state.CurrentFile), "Routine return replaced unchanged context");
                });
            await Check("Gallery return reconciles offline edits insertions and removals without flushing survivors", async () =>
            {
                var survivor = Preview(first); var changed = Preview(other); var completed = monitor.InitialReconciliations;
                var visits = (await history.GetListsAsync()).Frequent.Sum(r => r.OpenCount);
                navigation.SelectDestination("Home");
                File.WriteAllBytes(other, File.ReadAllBytes(other));
                File.SetLastWriteTimeUtc(other, state.Files.Single(f => f.FullPath == other).LastModifiedUtc.AddSeconds(10));
                var added = Path.Combine(directory, "999.png"); File.Copy(Fixtures[".png"].FullPath, added);
                var removed = Path.Combine(directory, "004.png"); File.Delete(removed);
                navigation.SelectDestination("Gallery");
                await WaitUntil(() => monitor.InitialReconciliations > completed && state.Files.Any(f => f.FullPath == added) && !state.Files.Any(f => f.FullPath == removed));
                await WaitUntil(() => Preview(other) is not null && !ReferenceEquals(changed, Preview(other)));
                Require(ReferenceEquals(survivor, Preview(first)), "Offline change flushed an unrelated preview");
                Require((await history.GetListsAsync()).Frequent.Sum(r => r.OpenCount) == visits, "Reconciliation counted another root visit");
            });
            foreach (var destination in new[] { "Home", "Settings" })
                await Check($"Gallery return via {destination} reveals a distant Browse selection after layout and watcher startup", async () =>
                {
                    navigation.SelectDestination("Browse");
                    var target = state.Files[125];
                    ((NavigationController)Field(browse, "_navigationController")!).SelectFile(target);
                    navigation.SelectDestination(destination);
                    var completed = monitor.InitialReconciliations;
                    navigation.SelectDestination("Gallery");
                    await WaitUntil(() => monitor.InitialReconciliations > completed);
                    await WaitUntil(() => ResumeTargetVisible(Grid(), target));
                    Require(ReferenceEquals(Grid().SelectedItem, target) && ReferenceEquals(state.CurrentFile, target), "Gallery lost the Browse selection");
                });
            await Check("Pending Gallery reveal yields to a newer user selection", async () =>
            {
                var gallery = (WinUiGalleryIsland)Field(browse, "_gallery")!;
                gallery.RevealCurrentFile(state.Files[125]);
                var chosen = state.Files[2];
                Grid().SelectedItem = chosen;
                Grid().ScrollIntoView(chosen);
                await WaitUntil(() => ReferenceEquals(state.CurrentFile, chosen) && ResumeTargetVisible(Grid(), chosen));
                await Task.Delay(100);
                Require(ReferenceEquals(Grid().SelectedItem, chosen) && ResumeTargetVisible(Grid(), chosen), "An obsolete deferred reveal overrode user navigation");
            });
            foreach (var destination in new[] { "Home", "Settings" })
                await Check($"Pinned tree remains rendered and reachable in Gallery after {destination}", async () =>
                {
                    navigation.SelectDestination("Browse");
                    browse.SetFolderPinned(true);
                    await Task.Delay(250);
                    navigation.SelectDestination(destination);
                    await Task.Delay(250);
                    navigation.SelectDestination("Gallery");
                    await Task.Delay(250);
                    window.UpdateLayout();
                    var tree = (FrameworkElement)browse.FindName("FolderOverlay");
                    var transform = (System.Windows.Media.TranslateTransform)browse.FindName("FolderOverlayTransform");
                    Require((bool)Field(browse, "_folderOverlayPinned")! && (bool)Field(browse, "_folderOverlayVisible")! && tree.IsHitTestVisible && Math.Abs(transform.X) < 1,
                        $"Pinned tree's WPF presentation was lost: visible={Field(browse, "_folderOverlayVisible")}, X={transform.X}");
                    Require(Grid().Padding.Left >= tree.ActualWidth, "Pinned Gallery tree space was lost");
                    var gallery = (WinUiGalleryIsland)Field(browse, "_gallery")!;
                    Require(!GalleryNativeContains(gallery, 150, 100), "Native Gallery covers the pinned WPF tree");
                    Require(GalleryNativeContains(gallery, 500, 100), "Native Gallery content is incorrectly hidden");
                    browse.SetFolderPinned(false);
                });
        }
        finally { window.Close(); }
    }

    private static bool GalleryNativeContains(WinUiGalleryIsland gallery, double x, double y)
    {
        var region = CreateGalleryTestRegion(0, 0, 0, 0);
        try
        {
            var kind = GetGalleryTestRegion(gallery.Handle, region);
            if (kind == 0) return true; // No window region means the entire native surface is present.
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(gallery);
            return GalleryTestPointInRegion(region, (int)(x * dpi.DpiScaleX), (int)(y * dpi.DpiScaleY));
        }
        finally { DeleteGalleryTestRegion(region); }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll", EntryPoint = "CreateRectRgn")]
    private static extern nint CreateGalleryTestRegion(int left, int top, int right, int bottom);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowRgn")]
    private static extern int GetGalleryTestRegion(nint window, nint region);
    [System.Runtime.InteropServices.DllImport("gdi32.dll", EntryPoint = "PtInRegion")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GalleryTestPointInRegion(nint region, int x, int y);
    [System.Runtime.InteropServices.DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DeleteGalleryTestRegion(nint region);

    private static bool ResumeTargetVisible(Microsoft.UI.Xaml.Controls.GridView grid, MediFiler3.Domain.Files.FileEntry file)
    {
        if (grid.ContainerFromItem(file) is not Microsoft.UI.Xaml.Controls.GridViewItem item || item.ActualHeight <= 0) return false;
        var top = item.TransformToVisual(grid).TransformPoint(new Windows.Foundation.Point()).Y;
        return top >= -1 && top + item.ActualHeight <= grid.ActualHeight + 1;
    }

    private sealed class RecordingResumeMonitor : IFolderChangeMonitor
    {
        public int InitialReconciliations;
        public Task RunAsync(IReadOnlyList<string> roots, Func<string?> activeFolder,
            Func<FolderChangeBatch, CancellationToken, Task> reconcile, CancellationToken token) =>
            new FileSystemFolderChangeMonitor().RunAsync(roots, activeFolder, async (batch, ct) =>
            {
                await reconcile(batch, ct);
                if (batch.ReconcileRoots is { Count: > 0 }) Interlocked.Increment(ref InitialReconciliations);
            }, token);
    }
}
