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
        var operations = new FileSystemFileOrganizationService(journal, new WindowsFileIdentityProvider());
        var availability = new FileSystemRootFolderAvailabilityService();
        var store = new SqliteApplicationStateStore(database);
        var settings = new ApplicationSettingsService(store);
        await settings.InitializeAsync();
        var state = new NavigationSessionState();
        var window = new MediFiler3.MainWindow(Microsoft.Extensions.Logging.Abstractions.NullLogger<MediFiler3.MainWindow>.Instance,
            new FileSystemFolderTreeService(), new FileSystemFileListingService(), state, new WicImageDecoder(Files), NativeThumbnails,
            operations, journal, new FileSystemTextContentService(Files), new External(), new Defaults(), settings,
            new RootFolderHistoryService(store, availability), availability)
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
        }
        finally { window.Close(); }
    }
}
