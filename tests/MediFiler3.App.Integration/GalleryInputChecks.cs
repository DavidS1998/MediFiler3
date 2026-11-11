using System.Reflection;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;
using MediFiler3.Interop;
using MediFiler3.Views;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

internal static partial class Program
{
    private static async Task RunGalleryInputChecksAsync(NavigationSessionState state, BrowseView browse,
        WinUiToolbarIsland toolbar, WinUiNavigationIsland navigation, Func<WinUiGalleryIsland> gallery)
    {
        GridView Grid() => (GridView)Field(gallery(), "_grid")!;
        bool Key(VirtualKey key, bool control = false) => (bool)typeof(WinUiGalleryIsland)
            .GetMethod("HandleSelectionKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(gallery(), [key, control])!;
        MenuFlyout Menu(FileEntry? file) => (MenuFlyout)typeof(WinUiGalleryIsland)
            .GetMethod("CreateSelectionMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(gallery(), [file])!;
        static void Invoke(MenuFlyoutItem item) => ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item)
            .GetPattern(PatternInterface.Invoke)).Invoke();
        var selection = state.GallerySelection;
        var files = state.Files.ToArray();

        await Check("Gallery native accessibility selection supports adding removing and named selected borders", async () =>
        {
            gallery().RevealCurrentFile(files[0]);
            await WaitUntil(() => Grid().ContainerFromItem(files[2]) is GridViewItem);
            Require(Grid().SelectionMode == ListViewSelectionMode.Extended, "Native modifier/range selection is disabled");
            var gridPeer = (GridViewAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(Grid());
            var provider = (ISelectionProvider)gridPeer.GetPattern(PatternInterface.Selection);
            Require(provider.CanSelectMultiple && !provider.IsSelectionRequired, "Gallery exposes incorrect accessibility selection scope");
            ItemAutomationPeer Peer(int index) => gridPeer.GetChildren().OfType<ItemAutomationPeer>().Single(peer => Equals(peer.Item, files[index]));
            ISelectionItemProvider Item(int index) => (ISelectionItemProvider)Peer(index)
                .GetPattern(PatternInterface.SelectionItem);
            Item(0).Select(); Item(2).AddToSelection();
            await WaitUntil(() => selection.Count == 2 && ReferenceEquals(state.CurrentFile, files[2]));
            Require(selection.GetSelectedFiles(state.Files).SequenceEqual(new[] { files[0], files[2] }), "Accessible selection lost command ordering");
            foreach (var index in new[] { 0, 2 })
            {
                var container = (GridViewItem)Grid().ContainerFromItem(files[index]);
                var border = (Border)((Grid)container.ContentTemplateRoot).Children[3];
                Require(border.Visibility == Microsoft.UI.Xaml.Visibility.Visible, "A selected tile lost its border");
                Require(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(container) == files[index].Name &&
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(container).Contains(files[index].FullPath), "Accessible file information missing");
                Require(Peer(index).GetName() == files[index].Name,
                    "Accessible data-item name is not the filename");
            }
            Item(0).RemoveFromSelection();
            Require(selection.Count == 1 && selection.IsSelected(files[2]), "Accessible deselection failed to update session state");
        });
        await Check("Gallery session ranges and focus-only navigation synchronize native selection without loading Browse", async () =>
        {
            selection.Select(state.Files, files[5]);
            selection.Select(state.Files, files[2], range: true);
            selection.Select(state.Files, files[0], toggle: true, range: true);
            Require(Grid().SelectedItems.Count == 6 && selection.AnchorPath == files[5].FullPath, "Reverse/additive range was not synchronized");
            selection.Focus(state.Files, files[7]);
            await WaitUntil(() => ReferenceEquals(state.CurrentFile, files[7]));
            Require(Grid().SelectedItems.Count == 6 && !selection.IsSelected(files[7]), "Focus-only navigation changed selected files");
            Require(!(bool)Field(browse.FindName("ViewerHost"), "_active")!, "Gallery focus loaded Browse content");
        });
        await Check("Gallery Select All and Escape update native selection count without reselection on refresh", async () =>
        {
            Require(Key(VirtualKey.A, true) && Grid().SelectedItems.Count == files.Length, "Ctrl+A did not select all files");
            Require(((TextBlock)Field(gallery(), "_selectionStatus")!).Text == $"{files.Length} selected", "Selection status did not update");
            Require(Key(VirtualKey.Escape) && selection.Count == 0 && Grid().SelectedItems.Count == 0, "Escape did not clear selection");
            gallery().SetFiles(state.Files, state.CurrentFile);
            Require(selection.Count == 0 && Grid().SelectedItems.Count == 0, "Presentation refresh reselected a cleared file");
            Require(!Key(VirtualKey.Left) && !Key(VirtualKey.Space), "Native navigation keys were intercepted");
            await Task.CompletedTask;
        });
        await Check("Gallery refresh retains selected survivors and current metadata while pruning removed paths", async () =>
        {
            selection.Select(state.Files, files[1]); selection.Select(state.Files, files[3], toggle: true);
            var changed = files[3] with { Size = files[3].Size + 1 };
            state.ReplaceFiles(files.Where(file => file != files[1]).Select(file => file == files[3] ? changed : file));
            // Simulate a native click before the deferred listing update reaches the grid.
            Grid().SelectedItems.Add(files[1]);
            await WaitUntil(() => Grid().Items.Count == files.Length - 1 && Grid().SelectedItems.Contains(changed));
            Require(ReferenceEquals(selection.GetSelectedFiles(state.Files).Single(), changed) && Grid().SelectedItems.Count == 1,
                "Refresh did not resolve selected paths to current records");
            state.ReplaceFiles(files);
            await WaitUntil(() => Grid().Items.Count == files.Length);
        });
        await Check("Gallery Home and Settings preserve selection while Browse mode clears it", async () =>
        {
            selection.Select(state.Files, files[0]); selection.Select(state.Files, files[2], toggle: true);
            foreach (var destination in new[] { "Home", "Settings" })
            {
                navigation.SelectDestination(destination); await Task.Delay(50);
                navigation.SelectDestination("Gallery"); await Task.Delay(50);
                Require(selection.Count == 2 && Grid().SelectedItems.Count == 2, "Workspace resume replaced Gallery selection");
            }
            navigation.SelectDestination("Browse");
            Require(selection.Count == 0, "Browse retained mode-local multi-selection");
            navigation.SelectDestination("Gallery");
            Require(selection.Count == 1 && selection.IsSelected(state.CurrentFile!), "New Gallery mode entry did not seed current-file selection");
        });
        await Check("Gallery context commands scope selected files and reject stale menu actions", async () =>
        {
            selection.Select(state.Files, files[0]); selection.Select(state.Files, files[2], toggle: true);
            var menu = Menu(files[0]);
            Require(selection.Count == 2 && selection.FocusedPath == files[0].FullPath, "Right click discarded an existing multi-selection");
            Require(((MenuFlyoutItem)menu.Items[0]).IsEnabled && menu.Items.Count == 4, "Context menu includes unimplemented mutations or lacks opening");
            var blank = Menu(null);
            Require(!((MenuFlyoutItem)blank.Items[0]).IsEnabled, "Blank-space menu could open the shared Browse current file");
            navigation.SelectDestination("Settings");
            Invoke((MenuFlyoutItem)menu.Items[3]); Invoke((MenuFlyoutItem)menu.Items[2]); Invoke((MenuFlyoutItem)menu.Items[0]);
            await Task.Delay(50);
            Require(selection.Count == 2 && !(bool)Field(browse, "_viewerActive")!, "Stale menu acted in a hidden workspace");
            navigation.SelectDestination("Gallery");
            var obsolete = Menu(files[0]);
            var changed = files[3] with { Size = files[3].Size + 1 };
            state.ReplaceFiles(files.Select(file => file == files[3] ? changed : file));
            await WaitUntil(() => Grid().Items.Contains(changed));
            Invoke((MenuFlyoutItem)obsolete.Items[3]);
            Require(selection.Count == 2, "A stale context-menu command changed a refreshed selection");
            state.ReplaceFiles(files);
            await WaitUntil(() => !Grid().Items.Contains(changed));
            var fresh = Menu(files[4]);
            Require(selection.Count == 1 && selection.IsSelected(files[4]), "Right click on an unselected file kept unrelated targets");
            Invoke((MenuFlyoutItem)fresh.Items[0]);
            await WaitUntil(() => !browse.IsGallery && ReferenceEquals(state.CurrentFile, files[4]));
            navigation.SelectDestination("Gallery");
        });
        await Check("Gallery Enter opens the focused file and mutation guards retain their scope", async () =>
        {
            selection.Select(state.Files, files[0]); selection.Select(state.Files, files[2], toggle: true);
            selection.Focus(state.Files, files[6]);
            await browse.PromptRenameCurrentFileAsync(); await browse.SendCurrentFileToRecycleBinAsync();
            await browse.UndoLastOperationAsync(); await browse.RedoLastOperationAsync();
            Require(!((AppBarButton)Field(toolbar, "_renameFileButton")!).IsEnabled && !((AppBarButton)Field(toolbar, "_deleteFileButton")!).IsEnabled,
                "Gallery enabled single-current-file mutation buttons");
            Require(state.Files.SequenceEqual(files) && Key(VirtualKey.Enter), "Mutation guards changed files or Enter was ignored");
            await WaitUntil(() => !browse.IsGallery && ReferenceEquals(state.CurrentFile, files[6]));
            Require(selection.Count == 0, "Enter retained Gallery command targets in Browse");
            navigation.SelectDestination("Gallery");
        });
    }
}
