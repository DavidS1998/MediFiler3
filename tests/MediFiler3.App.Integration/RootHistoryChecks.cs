using System.IO;
using System.Reflection;
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
    private static async Task RunRootHistoryChecksAsync()
    {
        var a = Directory.CreateDirectory(Path.Combine(Root, "history-A")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(Root, "history-B")).FullName;
        File.WriteAllText(Path.Combine(a, "a.txt"), "root A");
        File.WriteAllText(Path.Combine(b, "b.txt"), "root B");
        var database = new SqliteDatabase(Path.Combine(Root, "scoped-journal.db"));
        var journal = new SqliteFileOperationJournal(database);
        var backupStore = new FileSystemOperationBackupStore(journal, new WindowsFileIdentityProvider(), Path.Combine(Root, "app-managed", "UndoBackups"));
        var operations = new FileSystemFileOrganizationService(journal, new WindowsFileIdentityProvider(), backupStore: backupStore);
        var availability = new FileSystemRootFolderAvailabilityService();
        var store = new SqliteApplicationStateStore(database);
        var settings = new ApplicationSettingsService(store);
        await settings.InitializeAsync();
        var state = new NavigationSessionState();
        var window = new MediFiler3.MainWindow(Microsoft.Extensions.Logging.Abstractions.NullLogger<MediFiler3.MainWindow>.Instance,
            new FileSystemFolderTreeService(), new FileSystemFileListingService(), state, new WicImageDecoder(Files), NativeThumbnails,
            operations, journal, new FileSystemTextContentService(Files), new External(), new Defaults(), settings,
            new RootFolderHistoryService(store, availability), availability, new FileSystemCollisionPreviewService(NativeThumbnails), backupStore)
        { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        window.Show();
        var browse = (BrowseView)Field(window, "_browseView")!;
        var toolbar = (WinUiToolbarIsland)window.FindName("ToolbarIsland");
        var activate = typeof(BrowseView).GetMethod("ActivateFolderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        bool Enabled(string name) => ((Microsoft.UI.Xaml.Controls.AppBarButton?)Field(toolbar, name))?.IsEnabled == true;
        async Task SelectRoot(string path) => await (Task)activate.Invoke(browse,
            [state.Roots.Single(r => r.Path == path), null, 0])!;
        try
        {
            await Task.Delay(50);
            Require(!Enabled("_undoButton") && !Enabled("_redoButton"), "History enabled with no active root on Home");
            var load = typeof(MediFiler3.MainWindow).GetMethod("LoadFolderPathsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)load.Invoke(window, [new[] { a, b }])!;
            await WaitUntil(() => state.ActiveRoot?.Path == a && state.Files.Count == 1);
            var scopedA = await operations.ForRootAsync(a);
            await scopedA.RenameFileAsync(Path.Combine(a, "a.txt"), "renamed.txt");
            await browse.RefreshAsync();
            await WaitUntil(() => Enabled("_undoButton"));
            await Check("root switch disables unrelated Undo including Browse command execution", async () =>
            {
                await SelectRoot(b);
                await Task.Delay(100);
                Require(!Enabled("_undoButton") && !Enabled("_redoButton"), "Other root history remained enabled");
                await browse.UndoLastOperationAsync();
                Require(File.Exists(Path.Combine(a, "renamed.txt")), "Inactive root was undone");
                await SelectRoot(a);
                await WaitUntil(() => Enabled("_undoButton"));
                var tip = (Microsoft.UI.Xaml.Controls.ToolTip)Microsoft.UI.Xaml.Controls.ToolTipService.GetToolTip(
                    (Microsoft.UI.Xaml.Controls.AppBarButton)Field(toolbar, "_undoButton")!);
                var previewText = ((Microsoft.UI.Xaml.Controls.TextBlock)tip.Content).Text;
                Require(previewText.Contains(a) && !previewText.Contains(b), "Preview crossed roots");
                await browse.UndoLastOperationAsync();
                await WaitUntil(() => Enabled("_redoButton"));
                Require(File.Exists(Path.Combine(a, "a.txt")), "Active root undo failed");
            });
            await Check("root switch isolates Redo and returning restores its own stack", async () =>
            {
                await SelectRoot(b);
                await Task.Delay(100);
                Require(!Enabled("_redoButton"), "Inactive root redo remained enabled");
                await browse.RedoLastOperationAsync();
                Require(File.Exists(Path.Combine(a, "a.txt")), "Inactive root was redone");
                await SelectRoot(a);
                await WaitUntil(() => Enabled("_redoButton"));
                await browse.RedoLastOperationAsync();
                Require(File.Exists(Path.Combine(a, "renamed.txt")), "Returning root lost redo");
            });
            var source = Path.Combine(a, "collision-source.txt");
            var target = Path.Combine(a, "collision-target.txt");
            File.WriteAllText(source, "incoming collision data");
            File.WriteAllText(target, "existing collision data");
            await browse.RefreshAsync();
            var dialogs = new CollisionDialogs(window.Dispatcher) { Name = Path.GetFileNameWithoutExtension(target) };
            browse.DialogService = dialogs;
            await Check("Browse collision Cancel preserves both items through the UI resolver", async () =>
            {
                state.SetCurrentFile(state.Files.Single(f => f.FullPath == source));
                dialogs.Resolution = new(MediFiler3.Application.Operations.FileCollisionChoice.Cancel);
                await browse.PromptRenameCurrentFileAsync();
                Require(dialogs.Collision?.DestinationPath == target && dialogs.Collision.CanOverwrite, "Collision details not forwarded");
                Require(dialogs.ResolvedOnDispatcher, "Dialog requested off the UI thread");
                Require(dialogs.InitialName == "collision-source", "File rename exposes the extension");
                Require(File.ReadAllText(source) == "incoming collision data" && File.ReadAllText(target) == "existing collision data",
                    "Cancel changed files");
            });
            var renamed = Path.Combine(a, "collision-kept.txt");
            await Check("Browse collision Rename retains the chosen actual file selection", async () =>
            {
                dialogs.Resolution = new(MediFiler3.Application.Operations.FileCollisionChoice.Rename, Path.GetFileName(renamed));
                await browse.PromptRenameCurrentFileAsync();
                Require(File.Exists(renamed) && state.CurrentFile?.FullPath == renamed, "Chosen collision name/selection not applied");
                Require(File.ReadAllText(target) == "existing collision data", "Rename replaced the destination");
            });
            await Check("Browse collision Overwrite is reversed and replayed as one root-scoped group", async () =>
            {
                dialogs.Resolution = new(MediFiler3.Application.Operations.FileCollisionChoice.Overwrite);
                await browse.PromptRenameCurrentFileAsync();
                Require(File.ReadAllText(target) == "incoming collision data", "Overwrite did not install incoming file");
                await browse.UndoLastOperationAsync();
                Require(File.ReadAllText(target) == "existing collision data" && File.Exists(renamed), "Undo lost an overwritten version");
                await browse.RedoLastOperationAsync();
                Require(File.ReadAllText(target) == "incoming collision data", "Redo replacement failed");
            });
            var previewSource = Path.Combine(a, "Test.png");
            var previewTarget = Path.Combine(b, "Test.png");
            File.Copy(Fixtures[".png"].FullPath, previewSource);
            File.Copy(Fixtures[".png"].FullPath, previewTarget);
            var folderTarget = Directory.CreateDirectory(Path.Combine(b, "collision-folder")).FullName;
            foreach (var compatible in new[] { true, false })
                await Check(compatible ? "native collision dialog offers Rename Overwrite Cancel" : "native collision dialog disables incompatible Overwrite", async () =>
                {
                    Exception? failure = null;
                    var clicked = false;
                    var started = DateTime.UtcNow;
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
                    timer.Tick += (_, _) =>
                    {
                        var dialog = System.Windows.Application.Current.Windows.Cast<System.Windows.Window>()
                            .FirstOrDefault(w => w.Title == "Name collision");
                        if (dialog is null) return;
                        try
                        {
                            if (DateTime.UtcNow - started > TimeSpan.FromSeconds(5)) throw new TimeoutException("Dialog did not initialize.");
                            var root = dialog.Content?.GetType().GetProperty("RootElement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                ?.GetValue(dialog.Content) as Microsoft.UI.Xaml.Controls.Grid;
                            if (root is null || root.Children.Count == 0) return;
                            var border = (Microsoft.UI.Xaml.Controls.Border)root.Children[0];
                            var content = (Microsoft.UI.Xaml.Controls.StackPanel)border.Child;
                            Require(content.Children.Count == 2, "Collision explanation text is still displayed");
                            var buttons = ((Microsoft.UI.Xaml.Controls.StackPanel)content.Children.Last()).Children
                                .Cast<Microsoft.UI.Xaml.Controls.Button>().ToArray();
                            Require(buttons.Select(button => button.Content.ToString()).SequenceEqual(new[] { "Rename", "Overwrite", "Cancel" }), "Collision choices differ");
                            Require(buttons[1].IsEnabled == compatible, "Overwrite eligibility differs");
                            Require(dialog.Owner == window && dialog.Opacity == 0, "Dialog owner/hidden probe surface");
                            root.UpdateLayout();
                            var bottom = ((Microsoft.UI.Xaml.Controls.StackPanel)content.Children.Last()).TransformToVisual(root)
                                .TransformPoint(new Windows.Foundation.Point()).Y + ((Microsoft.UI.Xaml.Controls.StackPanel)content.Children.Last()).ActualHeight;
                            Require(root.ActualHeight - bottom is >= 20 and <= 28, "Excess space below collision buttons");
                            var comparison = (Microsoft.UI.Xaml.Controls.Grid)content.Children[0];
                            Require(comparison.Children.Count == 2, "Missing side-by-side cards");
                            for (var index = 0; index < 2; index++)
                            {
                                var card = (Microsoft.UI.Xaml.Controls.StackPanel)((Microsoft.UI.Xaml.Controls.Border)comparison.Children[index]).Child;
                                var preview = (Microsoft.UI.Xaml.Controls.Grid)card.Children[1];
                                var image = (Microsoft.UI.Xaml.Controls.Image)preview.Children[1];
                                var size = (Microsoft.UI.Xaml.Controls.TextBlock)card.Children[3];
                                if (!compatible && index == 1) { Require(size.Text == "Folder", "Folder size misrepresented"); continue; }
                                if (image.Source is null || size.Text == "Loading size…") return;
                                Require(size.Text == MediFiler3.Application.Viewing.ViewerMetadataFormatter.Format(
                                    new MediFiler3.Application.Viewing.ViewerMetadata(new FileInfo(previewSource).Length)), "File size missing or incorrect");
                                Require(((Microsoft.UI.Xaml.Media.Imaging.BitmapImage)image.Source).PixelWidth > 0, "Empty thumbnail");
                            }
                            timer.Stop(); clicked = true;
                            var chosen = compatible ? buttons[1] : buttons[2];
                            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(chosen)
                                .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                        }
                        catch (Exception exception) { failure = exception; timer.Stop(); dialog.Close(); }
                    };
                    timer.Start();
                    try
                    {
                        var answer = await toolbar.ResolveCollisionAsync(new(previewSource, compatible ? previewTarget : folderTarget, false, !compatible, compatible));
                        if (failure is not null) throw failure;
                        Require(clicked && answer.Choice == (compatible ? MediFiler3.Application.Operations.FileCollisionChoice.Overwrite
                            : MediFiler3.Application.Operations.FileCollisionChoice.Cancel), "Wrong collision dialog result");
                    }
                    finally { timer.Stop(); }
                });
            await Check("native collision Rename applies numbered name without another dialog", async () =>
            {
                Exception? failure = null;
                var renamed = false;
                var started = DateTime.UtcNow;
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
                timer.Tick += (_, _) =>
                {
                    var dialog = System.Windows.Application.Current.Windows.Cast<System.Windows.Window>()
                        .FirstOrDefault(w => w.Title is "Name collision" or "Choose a different name");
                    if (dialog is null) return;
                    try
                    {
                        if (DateTime.UtcNow - started > TimeSpan.FromSeconds(5)) throw new TimeoutException("Rename did not initialize");
                        var root = dialog.Content?.GetType().GetProperty("RootElement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?.GetValue(dialog.Content) as Microsoft.UI.Xaml.Controls.Grid;
                        if (root is null || root.Children.Count == 0) return;
                        var content = (Microsoft.UI.Xaml.Controls.StackPanel)((Microsoft.UI.Xaml.Controls.Border)root.Children[0]).Child;
                        var buttons = (Microsoft.UI.Xaml.Controls.StackPanel)content.Children.Last();
                        Require(dialog.Title == "Name collision", "Rename opened an extra dialog");
                        var chosen = (Microsoft.UI.Xaml.Controls.Button)buttons.Children[0];
                        Require(chosen.IsEnabled, "Automatic Rename is disabled");
                        renamed = true;
                        timer.Stop();
                        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(chosen)
                            .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                    }
                    catch (Exception exception) { failure = exception; timer.Stop(); dialog.Close(); }
                };
                timer.Start();
                try
                {
                    var answer = await toolbar.ResolveCollisionAsync(new(previewSource, previewTarget, false, false, true, SuggestedName: "Test (2).png"));
                    if (failure is not null) throw failure;
                    Require(renamed && answer.NewName == "Test (2).png", "Extension was lost or duplicated");
                }
                finally { timer.Stop(); }
            });
            typeof(MediFiler3.MainWindow).GetMethod("OnNavigationSelectionChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, ["Settings"]);
            var settingsView = (SettingsView)Field(window, "_settingsView")!;
            foreach (var confirm in new[] { false, true })
                await Check(confirm ? "Settings confirmed cleanup removes backups and retires replacement history" :
                    "Settings cancelled cleanup preserves backups and history", async () =>
                {
                    var stored = (await journal.GetAllAsync()).Where(e => e.Operation.Kind is MediFiler3.Domain.Operations.FileOperationKind.Rename &&
                        System.Text.Json.JsonSerializer.Deserialize<MediFiler3.Domain.Operations.PathOperationPayload>(e.Operation.PayloadJson ?? "{}")?.IsReplacementBackup == true)
                        .Select(e => e.Operation.DestinationPath!).Where(File.Exists).ToArray();
                    Require(stored.Length > 0, "No backup fixture for Settings cleanup");
                    Exception? failure = null;
                    var clicked = false;
                    var started = DateTime.UtcNow;
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
                    timer.Tick += (_, _) =>
                    {
                        var dialog = System.Windows.Application.Current.Windows.Cast<System.Windows.Window>()
                            .FirstOrDefault(w => w.Title == "Clear overwrite backups");
                        if (dialog is null) return;
                        try
                        {
                            if (DateTime.UtcNow - started > TimeSpan.FromSeconds(5)) throw new TimeoutException("Cleanup confirmation did not initialize");
                            var root = dialog.Content?.GetType().GetProperty("RootElement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                ?.GetValue(dialog.Content) as Microsoft.UI.Xaml.Controls.Grid;
                            if (root is null || root.Children.Count == 0) return;
                            var content = (Microsoft.UI.Xaml.Controls.StackPanel)((Microsoft.UI.Xaml.Controls.Border)root.Children[0]).Child;
                            Require(((Microsoft.UI.Xaml.Controls.TextBlock)content.Children[0]).Text.Contains("Undo and Redo"), "Cleanup lacks history warning");
                            var buttons = (Microsoft.UI.Xaml.Controls.StackPanel)content.Children.Last();
                            var chosen = (Microsoft.UI.Xaml.Controls.Button)buttons.Children[confirm ? 1 : 0];
                            timer.Stop(); clicked = true;
                            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(chosen)
                                .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                        }
                        catch (Exception exception) { failure = exception; timer.Stop(); dialog.Close(); }
                    };
                    timer.Start();
                    var callback = settingsView.ClearBackupsRequested!;
                    Task? request = null;
                    settingsView.ClearBackupsRequested = () => request = callback();
                    var clearButton = (System.Windows.Controls.Button)settingsView.FindName("ClearBackupsButton");
                    try
                    {
                        clearButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        await request!;
                        if (failure is not null) throw failure;
                        Require(clicked, "Cleanup confirmation never appeared");
                        Require(stored.All(path => File.Exists(path) != confirm), "Cleanup cancellation/acceptance changed the wrong files");
                        await WaitUntil(() => clearButton.IsEnabled);
                        Require(File.ReadAllText(target) == "incoming collision data", "Cleanup touched the current destination");
                        if (confirm)
                        {
                            Require((await journal.GetAllAsync()).Any(e => e.State == MediFiler3.Domain.Operations.FileOperationEventKind.HistoryDiscarded), "History was not retired");
                            Require(((System.Windows.Controls.TextBlock)settingsView.FindName("BackupCleanupStatus")).Text.Contains("Cleared"), "Cleanup status missing");
                        }
                    }
                    finally { timer.Stop(); settingsView.ClearBackupsRequested = callback; }
                });
        }
        finally { window.Close(); }
    }

    private sealed class CollisionDialogs(System.Windows.Threading.Dispatcher dispatcher) : IWinUiDialogService
    {
        public string Name = "";
        public string? InitialName;
        public MediFiler3.Application.Operations.FileCollisionResolution Resolution = new(MediFiler3.Application.Operations.FileCollisionChoice.Cancel);
        public MediFiler3.Application.Operations.FileCollision? Collision;
        public bool ResolvedOnDispatcher;
        public Task<string?> PromptTextAsync(string title, string message, string initialValue = "", string primaryAction = "OK")
        { InitialName = initialValue; return Task.FromResult<string?>(Name); }
        public Task<bool> ConfirmAsync(string title, string message, string confirmAction) => Task.FromResult(false);
        public Task<MediFiler3.Application.Operations.FileCollisionResolution> ResolveCollisionAsync(MediFiler3.Application.Operations.FileCollision collision)
        { Collision = collision; ResolvedOnDispatcher = dispatcher.CheckAccess(); return Task.FromResult(Resolution); }
    }
}
