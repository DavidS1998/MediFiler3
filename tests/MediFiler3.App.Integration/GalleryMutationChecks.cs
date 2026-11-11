using System.IO;
using System.Reflection;
using MediFiler3.Application.Navigation;
using MediFiler3.Interop;
using MediFiler3.Views;
using Microsoft.UI.Xaml.Controls;

internal static partial class Program
{
    private static async Task RunGalleryMutationChecksAsync(MediFiler3.MainWindow window, NavigationSessionState state,
        BrowseView browse, WinUiToolbarIsland toolbar)
    {
        var directory = Directory.CreateDirectory(Path.Combine(Root, "gallery-operations")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(directory, "target")).FullName;
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt" }) File.WriteAllText(Path.Combine(directory, name), name);
        File.WriteAllText(Path.Combine(target, "b.txt"), "blocker");
        await LoadRestartRootsAsync(window, [directory]);
        await WaitUntil(() => state.ActiveRoot?.Path == directory && state.Files.Count == 4 && state.Roots[0].Children.Any(folder => folder.Path == target));
        var navigation = (WinUiNavigationIsland)window.FindName("NavigationIsland");
        navigation.SelectDestination("Gallery");
        var dialogs = new CollisionDialogs(window.Dispatcher);
        var originalDialogs = browse.DialogService;
        browse.DialogService = dialogs;
        try
        {
            await Check("Gallery partial move retains survivors and supports one-group Undo Redo", async () =>
            {
                var files = state.Files.ToArray();
                var root = state.Roots[0];
                root.IsExpanded = false;
                state.GallerySelection.ReplaceSelection(state.Files, files.Take(3), files[1]);
                var destination = state.Roots[0].Children.Single(folder => folder.Path == target);
                await (Task)typeof(BrowseView).GetMethod("MoveCurrentFileToFolderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(browse, [destination])!;
                Require(File.Exists(Path.Combine(target, "a.txt")) && File.ReadAllText(Path.Combine(target, "b.txt")) == "blocker", "Partial move changed the wrong files");
                Require(state.GallerySelection.Count == 2 && state.Files.Count == 3, "Partial move lost surviving selection or listing");
                Require(destination.DirectFileCount == 2, "Move destination count did not update");
                await browse.UndoLastOperationAsync();
                Require(ReferenceEquals(root, state.Roots[0]) && !root.IsExpanded && destination.DirectFileCount == 1 && root.DirectFileCount == 4, "Gallery Undo lost nodes/expansion or stale counts");
                Require(File.Exists(Path.Combine(directory, "a.txt")) && state.GallerySelection.Count == 2, "Gallery grouped Undo failed or lost survivors");
                await browse.RedoLastOperationAsync();
                Require(!File.Exists(Path.Combine(directory, "a.txt")) && state.GallerySelection.Count == 2, "Gallery grouped Redo failed or lost survivors");
            });
            await Check("Browse move history and folder mutations retain unrelated tree nodes and expansion", async () =>
            {
                navigation.SelectDestination("Browse");
                var root = state.Roots[0]; var destination = root.Children.Single(folder => folder.Path == target);
                root.IsExpanded = false;
                await browse.UndoLastOperationAsync();
                Require(ReferenceEquals(root, state.Roots[0]) && ReferenceEquals(destination, root.Children.Single(folder => folder.Path == target)) && !root.IsExpanded,
                    "Browse Undo rebuilt the tree");
                Require(destination.DirectFileCount == 1 && root.DirectFileCount == 4, "Browse Undo left stale counters");
                dialogs.Name = "renamed-target";
                await browse.ExecuteFolderMenuActionAsync(destination, "rename-folder");
                Require(ReferenceEquals(root, state.Roots[0]) && !root.IsExpanded && root.Children.Any(folder => folder.Name == "renamed-target"), "Folder rename reset the root");
                await browse.UndoLastOperationAsync();
                Require(ReferenceEquals(root, state.Roots[0]) && !root.IsExpanded && root.Children.Any(folder => folder.Path == target), "Folder rename Undo reset tree");
                dialogs.ConfirmResult = true;
                await browse.ExecuteFolderMenuActionAsync(root.Children.Single(folder => folder.Path == target), "recycle-folder");
                Require(ReferenceEquals(root, state.Roots[0]) && !root.IsExpanded && !root.Children.Any(folder => folder.Path == target), "Folder recycle rebuilt tree or retained deleted child");
                await browse.UndoLastOperationAsync();
                Require(ReferenceEquals(root, state.Roots[0]) && !root.IsExpanded && root.Children.Any(folder => folder.Path == target), "Folder restore failed to reconcile tree");
                dialogs.ConfirmResult = false;
                navigation.SelectDestination("Gallery");
                state.GallerySelection.ReplaceSelection(state.Files, state.Files.Take(2), state.Files[0]);
            });
            await Check("Browse Undo selects restored files and Redo advances in natural order", async () =>
            {
                navigation.SelectDestination("Browse");
                var controller = (NavigationController)Field(browse, "_navigationController")!;
                var move = typeof(BrowseView).GetMethod("MoveCurrentFileToFolderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var destination = state.Roots[0].Children.Single(folder => folder.Path == target);
                foreach (var recycle in new[] { false, true })
                {
                    var selected = state.Files.Single(file => file.Name == "c.txt");
                    controller.SelectFile(selected);
                    if (recycle) await browse.SendCurrentFileToRecycleBinAsync();
                    else await (Task)move.Invoke(browse, [destination])!;
                    Require(state.CurrentFile?.Name == "d.txt", "Initial removal did not select the next file");
                    await browse.UndoLastOperationAsync();
                    Require(state.CurrentFile?.Name == "c.txt", "Undo did not select the restored file");
                    await browse.RedoLastOperationAsync();
                    Require(state.CurrentFile?.Name == "d.txt", "Redo selected the first file instead of the next file");
                    await browse.UndoLastOperationAsync();
                    Require(state.CurrentFile?.Name == "c.txt", "Second Undo did not restore current file");
                }
                controller.SelectFile(state.Files.Single(file => file.Name == "d.txt"));
                await (Task)move.Invoke(browse, [destination])!;
                await browse.UndoLastOperationAsync();
                Require(state.CurrentFile?.Name == "d.txt", "Undo last file did not select it");
                await browse.RedoLastOperationAsync();
                Require(state.CurrentFile?.Name == "c.txt", "Redo last file did not select its previous neighbor");
                await browse.UndoLastOperationAsync();
                navigation.SelectDestination("Gallery");
                state.GallerySelection.ReplaceSelection(state.Files, state.Files.Take(2), state.Files[0]);
            });
            await Check("Gallery recycle confirmation cancellation and stale mode reject mutations", async () =>
            {
                var before = state.Files.ToArray();
                await browse.SendCurrentFileToRecycleBinAsync();
                Require(before.All(file => File.Exists(file.FullPath)), "Cancelled confirmation recycled files");
                dialogs.ConfirmResult = true;
                dialogs.OnConfirm = () => navigation.SelectDestination("Browse");
                await browse.SendCurrentFileToRecycleBinAsync();
                Require(before.All(file => File.Exists(file.FullPath)), "Stale confirmation recycled files");
                dialogs.OnConfirm = null;
                navigation.SelectDestination("Gallery");
            });
            await Check("Gallery single selection rename and recycle bypass multi-file confirmation", async () =>
            {
                var file = state.Files[0];
                state.GallerySelection.Select(state.Files, file);
                dialogs.Name = "renamed-gallery";
                await browse.PromptRenameCurrentFileAsync();
                var renamed = state.Files.Single(item => item.Name == "renamed-gallery.txt");
                Require(state.GallerySelection.Count == 1 && state.GallerySelection.IsSelected(renamed), "Single rename lost selected file");
                var confirmations = 0;
                dialogs.OnConfirm = () => confirmations++;
                dialogs.ConfirmResult = false;
                await browse.SendCurrentFileToRecycleBinAsync();
                Require(confirmations == 0 && !File.Exists(renamed.FullPath), "Single recycle asked for confirmation or failed");
                await browse.UndoLastOperationAsync();
                Require(File.Exists(renamed.FullPath), "Single recycle restore failed");
                dialogs.OnConfirm = null; dialogs.ConfirmResult = true;
            });
            await Check("Gallery view shortcuts route independently of focused island", async () =>
            {
                var shortcut = typeof(MediFiler3.MainWindow).GetMethod("TryHandleViewShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!;
                foreach (var focus in new[] { (Microsoft.UI.Xaml.Controls.Control)Field(toolbar, "_deleteFileButton")!,
                    (Microsoft.UI.Xaml.Controls.Control)Field((WinUiGalleryIsland)Field(browse, "_gallery")!, "_grid")! })
                {
                    state.GallerySelection.Clear();
                    focus.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                    Require((bool)shortcut.Invoke(window, [System.Windows.Input.Key.A, System.Windows.Input.ModifierKeys.Control, false])!, "Ctrl+A ignored");
                    Require(state.GallerySelection.Count == state.Files.Count, "Ctrl+A followed focus instead of active Gallery");
                    Require((bool)shortcut.Invoke(window, [System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.None, false])!, "Escape ignored");
                    Require(state.GallerySelection.Count == 0, "Escape failed to clear Gallery");
                }
            });
            await Check("Gallery grouped recycling restores and replays selected files with root history", async () =>
            {
                var selected = state.Files.Take(2).ToArray();
                state.GallerySelection.ReplaceSelection(state.Files, selected, selected[0]);
                await browse.SendCurrentFileToRecycleBinAsync();
                Require(selected.All(file => !File.Exists(file.FullPath)) && state.GallerySelection.Count == 0, "Grouped recycle or selection pruning failed");
                await browse.UndoLastOperationAsync();
                Require(selected.All(file => File.Exists(file.FullPath)) && state.GallerySelection.Count == 0, "Grouped restore failed or reselected cleared files");
                await browse.RedoLastOperationAsync();
                Require(selected.All(file => !File.Exists(file.FullPath)), "Grouped recycle Redo failed");
                await browse.UndoLastOperationAsync();
                Require(selected.All(file => File.Exists(file.FullPath)), "Final restore failed");
            });
        }
        finally { browse.DialogService = originalDialogs; }
    }
}
