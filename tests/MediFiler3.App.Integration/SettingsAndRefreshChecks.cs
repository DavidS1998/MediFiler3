using System.IO;
using System.Reflection;
using System.Windows;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Viewing;
using MediFiler3.Infrastructure.Files;
using MediFiler3.Infrastructure.Folders;
using MediFiler3.Services;
using MediFiler3.Views;

internal static partial class Program
{
    private static async Task RunSettingsAndRefreshChecksAsync()
    {
        var folder = Path.Combine(Root, "refresh-root");
        var nested = Path.Combine(folder, "nested");
        Directory.CreateDirectory(nested);
        foreach (var name in new[] { "1.txt", "2.txt", "3.txt" })
            File.WriteAllText(Path.Combine(folder, name), "test");
        File.WriteAllText(Path.Combine(nested, "a.txt"), "nested");
        File.WriteAllText(Path.Combine(nested, "b.txt"), "nested");
        var state = new NavigationSessionState();
        var historyDatabase = new MediFiler3.Infrastructure.Persistence.SqliteDatabase(Path.Combine(Root, "root-history.db"));
        var rootHistory = new MediFiler3.Application.Folders.RootFolderHistoryService(
            new MediFiler3.Infrastructure.Persistence.SqliteApplicationStateStore(historyDatabase),
            new FileSystemRootFolderAvailabilityService());
        var browse = new BrowseView(new FileSystemFolderTreeService(), new FileSystemFileListingService(), state,
            new WicImageDecoder(Files), NativeThumbnails, null!, new FileSystemTextContentService(Files),
            new External(), new Defaults(), new MediaAudioSettings(0.73, true), rootHistory);
        var window = new Window { Width = 1280, Height = 800, Opacity = 0, ShowInTaskbar = false,
            ShowActivated = false, Content = browse };
        window.Show();
        try
        {
            await Check("Settings slider owns an opaque matching background", async () =>
            {
                var settings = new SettingsView(5);
                var settingsWindow = new Window { Width = 700, Height = 300, Opacity = 0,
                    ShowInTaskbar = false, ShowActivated = false, Content = settings };
                settingsWindow.Show();
                try
                {
                    await Task.Delay(50);
                    var island = (MediFiler3.Interop.WinUiCarouselSettingsIsland)settings.FindName("CarouselSliderIsland");
                    var source = (Microsoft.UI.Xaml.Hosting.DesktopWindowXamlSource)Field(island, "_xamlSource")!;
                    var grid = (Microsoft.UI.Xaml.Controls.Grid)source.Content;
                    var color = ((Microsoft.UI.Xaml.Media.SolidColorBrush)grid.Background).Color;
                    Require(color.A == 255 && color.R == 11 && color.G == 12 && color.B == 14,
                        "Slider background is transparent or mismatched");
                }
                finally { settingsWindow.Content = null; settingsWindow.Close(); }
            });
            await Check("restored settings apply to Browse before any media", async () =>
            {
                browse.SetCarouselNeighborCount(9);
                browse.SetFolderPinned(true);
                Require(browse.CarouselNeighborCount == 9, "Carousel setting not restored");
                Require((bool)Field(browse, "_folderOverlayPinned")!, "Folder pin not restored");
                var mediaFolder = Path.Combine(Root, "restored-audio");
                Directory.CreateDirectory(mediaFolder);
                File.Copy(Fixtures[".wav"].FullPath, Path.Combine(mediaFolder, "test.wav"));
                await browse.LoadRootsAsync([mediaFolder]);
                await WaitUntil(() => browse.PlaybackState.Ready);
                Require(browse.PlaybackState.Muted && Math.Abs(browse.PlaybackState.Volume - 0.73) < 0.001,
                    "Audio settings not applied to first player");
                Require(!browse.PlaybackState.Playing, "Restoration started playback");
            });
            await Check("refresh retains selected file after external insertion", async () =>
            {
                await browse.LoadRootsAsync([folder]);
                await WaitUntil(() => state.Files.Count == 3);
                state.SetCurrentFile(state.Files[1]);
                File.WriteAllText(Path.Combine(folder, "0.txt"), "inserted externally");
                await browse.RefreshAsync();
                await WaitUntil(() => state.Files.Count == 4 && state.CurrentFile?.Name == "2.txt");
                Require(state.CurrentFilePosition == 3, "Same path was not retained after sorting changed");
            });
            await Check("refresh retains nearest position after external removal", async () =>
            {
                File.Delete(Path.Combine(folder, "2.txt"));
                await browse.RefreshAsync();
                await WaitUntil(() => state.Files.Count == 3 && state.CurrentFile?.Name == "3.txt");
                Require(state.CurrentFilePosition == 3, "Deleted selection did not retain bounded index");
            });
            await Check("refresh restores nested folder and file", async () =>
            {
                await WaitUntil(() => state.Roots[0].Children.Count > 0);
                var activate = typeof(BrowseView).GetMethod("ActivateFolderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await (Task)activate.Invoke(browse, [state.Roots[0].Children[0], null, 0])!;
                state.SetCurrentFile(state.Files[1]);
                File.WriteAllText(Path.Combine(nested, "0.txt"), "external");
                await browse.RefreshAsync();
                await WaitUntil(() => state.ActiveFolder?.Path == nested && state.Files.Count == 3 && state.CurrentFile?.Name == "b.txt");
            });
            await Check("refresh falls back when nested folder is externally removed", async () =>
            {
                foreach (var name in new[] { "0.txt", "a.txt", "b.txt" }) File.Delete(Path.Combine(nested, name));
                Directory.Delete(nested);
                await browse.RefreshAsync();
                try { await WaitUntil(() => state.ActiveFolder?.Path == folder && state.Files.Count == 3); }
                catch (TimeoutException exception)
                {
                    throw new InvalidOperationException($"Expected fallback to {folder} with three files. Actual folder: {state.ActiveFolder?.Path}, files: {state.Files.Count}.", exception);
                }
            });
            await Check("refresh clears selection when root is externally removed", async () =>
            {
                foreach (var name in new[] { "0.txt", "1.txt", "3.txt" }) File.Delete(Path.Combine(folder, name));
                Directory.Delete(folder);
                await browse.RefreshAsync();
                await WaitUntil(() => state.Roots.Count == 0 && state.ActiveFolder is null && state.CurrentFile is null);
            });
            await Check("explicit root loads count aliases once and refresh/subfolder selection never count", async () =>
            {
                var visits = Path.Combine(Root, "visits-root");
                var child = Path.Combine(visits, "child");
                Directory.CreateDirectory(child);
                File.WriteAllText(Path.Combine(visits, "root.txt"), "root");
                File.WriteAllText(Path.Combine(child, "child.txt"), "child");
                await browse.LoadRootsAsync([visits]);
                var first = (await rootHistory.GetListsAsync()).Recent.Single(r => r.Path == visits);
                Require(first.OpenCount == 1, "First root load not counted");
                await browse.LoadRootsAsync([visits.ToUpperInvariant(), visits + Path.DirectorySeparatorChar]);
                var second = (await rootHistory.GetListsAsync()).Recent.Single(r =>
                    string.Equals(r.Path, visits, StringComparison.OrdinalIgnoreCase));
                Require(second.OpenCount == 2 && second.LastOpenedUtc >= first.LastOpenedUtc,
                    "Equivalent root load did not update shared usage");
                await browse.RefreshAsync();
                await WaitUntil(() => state.Roots[0].Children.Count > 0);
                var activate = typeof(BrowseView).GetMethod("ActivateFolderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await (Task)activate.Invoke(browse, [state.Roots[0].Children[0], null, 0])!;
                var lists = await rootHistory.GetListsAsync();
                Require(lists.Recent.Single(r => string.Equals(r.Path, visits, StringComparison.OrdinalIgnoreCase)).OpenCount == 2,
                    "Refresh/subfolder activation added a visit");
                Require(!lists.Recent.Any(r => string.Equals(r.Path, child, StringComparison.OrdinalIgnoreCase)),
                    "Subfolder navigation created persistent history");
                await browse.LoadRootsAsync([Path.Combine(Root, "absent-root")]);
                Require(!(await rootHistory.GetListsAsync()).Recent.Any(r => r.Path.EndsWith("absent-root")),
                    "Failed root load was recorded");
                await rootHistory.FlushAsync();
            });
        }
        finally { browse.StopBackgroundFolderCounts(); window.Content = null; window.Close(); }
    }
}
