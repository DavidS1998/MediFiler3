using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Operations;
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
    private static async Task RunPhase4RestartChecksAsync()
    {
        var directory = Path.Combine(Root, "phase4-restart");
        Directory.CreateDirectory(directory);
        File.Copy(Fixtures[".png"].FullPath, Path.Combine(directory, "preview.png"));
        File.Copy(Fixtures[".wav"].FullPath, Path.Combine(directory, "audio.wav"));
        foreach (var stage in new[] { "seed", "reopen", "verify" })
            await Check("Phase 4 fresh-process acceptance: " + stage, async () =>
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(typeof(Program).Assembly.Location);
                start.ArgumentList.Add("--phase4-restart-worker"); start.ArgumentList.Add(directory); start.ArgumentList.Add(stage);
                using var child = Process.Start(start) ?? throw new InvalidOperationException("Restart worker did not start.");
                var output = child.StandardOutput.ReadToEndAsync(); var errors = child.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                try { await child.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); throw new TimeoutException("Restart worker exceeded 60 seconds: " + stage); }
                Require(child.ExitCode == 0, $"{stage} worker failed: {await output} {await errors}");
                Require((await output).Contains("RESTART ACCEPTED " + stage), "Worker exited without acceptance evidence");
            });
    }

    private static async Task RunPhase4RestartWorkerAsync(string directory, string stage)
    {
        // A fresh OS process, SQLite connections, WPF window, WinUI islands, cache and watcher.
        // Only runner-owned paths are supplied; this never uses the user's database or backups.
        directory = Path.GetFullPath(directory);
        var a = Path.Combine(directory, "A"); var b = Path.Combine(directory, "B");
        var db = new SqliteDatabase(Path.Combine(directory, "state.db"));
        var store = new SqliteApplicationStateStore(db);
        var settings = new ApplicationSettingsService(store); var layout = new WindowLayoutService(store);
        await settings.InitializeAsync(); await layout.InitializeAsync();
        var availability = new FileSystemRootFolderAvailabilityService();
        var history = new RootFolderHistoryService(store, availability);
        var journal = new SqliteFileOperationJournal(db); var identities = new WindowsFileIdentityProvider();
        var backups = new FileSystemOperationBackupStore(journal, identities, Path.Combine(directory, "UndoBackups"));
        var operations = new FileSystemFileOrganizationService(journal, identities, backupStore: backups);
        using var cache = new FileSystemThumbnailService(store, Path.Combine(directory, "ThumbnailCache"));
        if (stage != "seed")
        {
            Require(settings.Current == new ApplicationSettings(true, 0.37, 17, true, false), $"Settings lost across process exit: {settings.Current}");
            var saved = JsonSerializer.Deserialize<WindowLayout>(File.ReadAllText(Path.Combine(directory, "expected-layout.json")))!;
            Require(layout.Current == saved, "Captured normal bounds/state lost across process exit");
            Require((await history.GetListsAsync()).Favorites.Single().Path == a, "Favorite missing after restart");
            Require((await journal.GetIncompleteAsync()).Count == 0, "Restart found incomplete operation intents");
            Require(await ((IThumbnailCacheIndexStore)store).LoadAsync() is { Count: > 0 } || stage == "verify", "Thumbnail index not flushed");
        }
        var state = new NavigationSessionState();
        var window = new MediFiler3.MainWindow(Microsoft.Extensions.Logging.Abstractions.NullLogger<MediFiler3.MainWindow>.Instance,
            new FileSystemFolderTreeService(), new FileSystemFileListingService(), state, new WicImageDecoder(Files), cache,
            operations, journal, new FileSystemTextContentService(Files), new External(), new Defaults(), settings, history,
            availability, new FileSystemCollisionPreviewService(cache), backups, layout, new WindowsWindowPlacement(), new FileSystemFolderChangeMonitor())
        { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        // MainWindow's layout Closing handler precedes the production-equivalent service flush.
        window.Closing += (_, _) =>
        {
            settings.FlushAsync().GetAwaiter().GetResult(); layout.FlushAsync().GetAwaiter().GetResult();
            cache.FlushAsync().GetAwaiter().GetResult();
        };
        window.Show();
        var browse = (BrowseView)Field(window, "_browseView")!;
        var home = (ContentControl)window.FindName("PageContent");
        try
        {
            Require(home.Content is HomeView && state.Roots.Count == 0 && state.CurrentFile is null && !browse.PlaybackState.Playing,
                "Restart restored navigation or playback instead of Home");
            Require(Field(browse, "_watchStop") is null, "Home startup created filesystem watcher");
            if (stage == "seed")
            {
                Directory.CreateDirectory(a); Directory.CreateDirectory(b);
                File.Copy(Path.Combine(directory, "audio.wav"), Path.Combine(a, "0.wav"));
                File.WriteAllText(Path.Combine(a, "incoming.txt"), "new data");
                File.WriteAllText(Path.Combine(a, "target.txt"), "old data");
                File.WriteAllText(Path.Combine(b, "other.txt"), "unrelated");
                var settingsView = (SettingsView)Field(window, "_settingsView")!;
                typeof(MediFiler3.MainWindow).GetMethod("OnNavigationSelectionChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, ["Settings"]);
                var sliderIsland = (WinUiCarouselSettingsIsland)settingsView.FindName("CarouselSliderIsland");
                await WaitUntil(() => Field(sliderIsland, "_slider") is not null);
                ((Microsoft.UI.Xaml.Controls.Slider)Field(sliderIsland, "_slider")!).Value = 17;
                Require(settings.Current.CarouselIconCount == 17, "Settings slider did not update persistence state");
                ((Microsoft.UI.Xaml.Controls.ToggleSwitch)Field(sliderIsland, "_carouselToggle")!).IsOn = false;
                Require(!settings.Current.CarouselEnabled && !browse.CarouselEnabled, "Carousel toggle did not update browsing/persistence");
                browse.SetFolderPinned(true);
                await LoadRestartRootsAsync(window, [a, b]);
                await WaitUntil(() => browse.PlaybackState.Ready);
                Require(!browse.PlaybackState.Playing, "Seed media autoplayed");
                browse.SetPlaybackVolume(0.37);
                if (!browse.PlaybackState.Muted) browse.TogglePlaybackMute();
                Require(browse.PlaybackState.Muted, "Seed mute control did not apply");
                await history.SetFavoriteAsync(a, true);
                var scopedA = await operations.ForRootAsync(a);
                Require((await scopedA.WithCollisionResolver(new RestartOverwrite()).RenameFileAsync(Path.Combine(a, "incoming.txt"), "target.txt")).Succeeded, "Seed replacement failed");
                var scopedB = await operations.ForRootAsync(b);
                Require((await scopedB.RenameFileAsync(Path.Combine(b, "other.txt"), "renamed.txt")).Succeeded, "Seed unrelated history failed");
                await cache.GetThumbnailPngAsync(Entry(Path.Combine(directory, "preview.png")), 32, 32, default);
                window.Width = 1000; window.Height = 650;
            }
            else if (stage == "reopen")
            {
                Require(browse.CarouselNeighborCount == 8 && !browse.CarouselEnabled && (bool)Field(browse, "_folderOverlayPinned")!, "MainWindow did not apply restored settings");
                var lists = await history.GetListsAsync(); Require(lists.Recent.All(r => r.OpenCount == 1), "Startup counted visits");
                var cached = (await ((IThumbnailCacheIndexStore)store).LoadAsync()).Single(e => e.SourcePath == Path.Combine(directory, "preview.png"));
                Require(File.Exists(Path.Combine(directory, "ThumbnailCache", cached.CacheFileName)), "Cache PNG missing after restart");
                await cache.GetThumbnailPngAsync(Entry(Path.Combine(directory, "preview.png")), 32, 32, default);
                await LoadRestartRootsAsync(window, [a, b]);
                await WaitUntil(() => browse.PlaybackState.Ready);
                Require(browse.PlaybackState.Muted && Math.Abs(browse.PlaybackState.Volume - 0.37) < 0.001 && !browse.PlaybackState.Playing, "Restored audio not applied before first media");
                var scopedA = await operations.ForRootAsync(a); var scopedB = await operations.ForRootAsync(b);
                Require((await scopedA.UndoAsync()).Succeeded && File.ReadAllText(Path.Combine(a, "target.txt")) == "old data", "Replacement Undo failed after process restart");
                Require((await scopedA.RedoAsync()).Succeeded && File.ReadAllText(Path.Combine(a, "target.txt")) == "new data", "Replacement Redo failed after process restart");
                Require(File.Exists(Path.Combine(b, "renamed.txt")) && await scopedB.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo) is not null, "Root A replay changed root B history");
                var external = Path.Combine(a, "external.txt"); File.WriteAllText(external, "external change");
                await WaitUntil(() => state.Files.Any(f => f.FullPath == external));
                Require((await history.GetListsAsync()).Recent.All(r => r.OpenCount == 2), "Live watcher counted a visit");
                browse.SetViewerActive(false);
                Require(Field(browse, "_watchStop") is null, "Watcher survived leaving Browse");
                var journalCount = (await journal.GetAllAsync()).Count;
                var backupFiles = Directory.GetFiles(Path.Combine(directory, "UndoBackups"));
                Require(backupFiles.Length > 0, "Replacement backup missing before cache cleanup");
                await cache.MaintainAsync(clear: true);
                Require((await journal.GetAllAsync()).Count == journalCount && backupFiles.All(File.Exists), "Cache cleanup affected journal/backups");
                Require(await scopedA.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo) is not null && await scopedB.GetHistoryPreviewAsync(FileOrganizationHistoryDirection.Undo) is not null, "Cache cleanup retired operation history");
            }
            else if (stage == "verify")
            {
                Require((await cache.GetUsageAsync()).EntryCount == 0, "Cleared index returned after second restart");
                Require((await history.GetListsAsync()).Recent.All(r => r.OpenCount == 2), "Second startup counted root opens");
                Require(File.ReadAllText(Path.Combine(a, "target.txt")) == "new data" && File.Exists(Path.Combine(a, "external.txt")), "Restart/cache cleanup changed source files");
                Require((await (await operations.ForRootAsync(a)).UndoAsync()).Succeeded && File.ReadAllText(Path.Combine(a, "target.txt")) == "old data", "Cache clear broke replacement Undo across another restart");
                Require((await (await operations.ForRootAsync(b)).UndoAsync()).Succeeded && File.Exists(Path.Combine(b, "other.txt")), "Other root history did not survive cache clear/restart");
            }
            else throw new ArgumentException("Unknown restart stage");
        }
        finally
        {
            window.Close();
            await history.FlushAsync();
            if (stage == "seed") File.WriteAllText(Path.Combine(directory, "expected-layout.json"), JsonSerializer.Serialize(layout.Current));
        }
        Console.WriteLine("RESTART ACCEPTED " + stage);
    }

    private static Task LoadRestartRootsAsync(MediFiler3.MainWindow window, string[] roots) =>
        (Task)typeof(MediFiler3.MainWindow).GetMethod("LoadFolderPathsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [roots])!;

    private sealed class RestartOverwrite : IFileCollisionResolver
    {
        public Task<FileCollisionResolution> ResolveAsync(FileCollision collision, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileCollisionResolution(FileCollisionChoice.Overwrite));
    }
}
