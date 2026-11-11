using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Controls;
using MediFiler3.Application.Navigation;
using MediFiler3.Interop;
using MediFiler3.Views;

internal static partial class Program
{
    private static async Task RunGalleryDragChecksAsync(MediFiler3.MainWindow window, NavigationSessionState state, BrowseView browse)
    {
        var directory = Directory.CreateDirectory(Path.Combine(Root, "gallery-drag")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(directory, "target")).FullName;
        foreach (var name in new[] { "one.txt", "two.txt", "three.txt" }) File.WriteAllText(Path.Combine(directory, name), name);
        await LoadRestartRootsAsync(window, [directory]);
        await WaitUntil(() => state.ActiveRoot?.Path == directory && state.Files.Count == 3 && state.Roots[0].Children.Count == 1);
        var navigation = (WinUiNavigationIsland)window.FindName("NavigationIsland"); navigation.SelectDestination("Gallery");
        var tree = (TreeView)browse.FindName("FolderTree");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        async Task<string> Begin(IReadOnlyList<MediFiler3.Domain.Files.FileEntry> files)
        {
            var gallery = (WinUiGalleryIsland)Field(browse, "_gallery")!;
            await WaitUntil(() => (bool)Field(gallery, "_active")! &&
                files.All(file => ((Microsoft.UI.Xaml.Controls.GridView)Field(gallery, "_grid")!).Items.OfType<MediFiler3.Domain.Files.FileEntry>().Contains(file)));
            typeof(WinUiGalleryIsland).GetField("_pressedDragFiles", flags)!.SetValue(gallery, files.ToArray());
            var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
            Require((bool)typeof(WinUiGalleryIsland).GetMethod("PrepareMoveDrag", flags)!.Invoke(gallery, [data])!, "Native source refused valid drag");
            Require(data.RequestedOperation == Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move &&
                ((Microsoft.UI.Xaml.Controls.TextBlock)Field(gallery, "_selectionStatus")!).Text.Contains($"Moving {files.Count}"), "Native drag has no move indication");
            return await data.GetView().GetTextAsync();
        }
        void End() => typeof(WinUiGalleryIsland).GetMethod("FinishMoveDrag", flags)!.Invoke(Field(browse, "_gallery"), null);
        TreeViewItem Target()
        {
            state.Roots[0].IsExpanded = true; tree.UpdateLayout();
            var root = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(state.Roots[0]); root.UpdateLayout();
            return (TreeViewItem)root.ItemContainerGenerator.ContainerFromItem(state.Roots[0].Children[0]);
        }
        DragEventArgs Drag(string token, RoutedEvent routedEvent, UIElement targetElement)
        {
            var data = new DataObject(); data.SetText(token, TextDataFormat.UnicodeText);
            var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, targetElement, new Point()], null)!;
            args.RoutedEvent = routedEvent;
            targetElement.RaiseEvent(args); return args;
        }
        await Check("Gallery native drag support and tree drop cues use one-file unselected origin", async () =>
        {
            var files = state.Files.ToArray();
            state.GallerySelection.ReplaceSelection(state.Files, files.Take(2), files[0]);
            var only = state.GallerySelection.GetDraggedFiles(state.Files, files[2]);
            var token = await Begin(only); await Task.Delay(220);
            Require(browse.IsGalleryMoveDragActive && (bool)Field(browse, "_folderOverlayVisible")!, "Drag did not reveal the tree");
            var gallery = (WinUiGalleryIsland)Field(browse, "_gallery")!;
            var grid = (Microsoft.UI.Xaml.Controls.GridView)Field(gallery, "_grid")!;
            await WaitUntil(() => grid.ContainerFromItem(grid.Items[0]) is Microsoft.UI.Xaml.Controls.GridViewItem item && item.ContentTemplateRoot is Microsoft.UI.Xaml.Controls.Grid);
            Require(!((Microsoft.UI.Xaml.Controls.Grid)((Microsoft.UI.Xaml.Controls.GridViewItem)grid.ContainerFromItem(grid.Items[0])).ContentTemplateRoot).CanDrag && !grid.CanDragItems, "Automatic tile dragging can steal normal clicks");
            var originFile = grid.Items.OfType<MediFiler3.Domain.Files.FileEntry>().Single(file => file.FullPath == only[0].FullPath);
            await WaitUntil(() => grid.ContainerFromItem(originFile) is Microsoft.UI.Xaml.Controls.GridViewItem);
            var origin = (Microsoft.UI.Xaml.Controls.Grid)((Microsoft.UI.Xaml.Controls.GridViewItem)grid.ContainerFromItem(originFile)).ContentTemplateRoot;
            var picture = (Microsoft.UI.Xaml.Controls.Image)origin.Children[1];
            var originalSource = picture.Source;
            var wide = new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap(80, 20);
            var green = new byte[80 * 20 * 4];
            for (var i = 0; i < green.Length; i += 4) { green[i + 1] = 255; green[i + 3] = 255; }
            using (var stream = wide.PixelBuffer.AsStream()) stream.Write(green, 0, green.Length);
            wide.Invalidate(); picture.Source = wide;
            var hover = typeof(WinUiGalleryIsland).GetMethod("SetTileHover", BindingFlags.Static | BindingFlags.NonPublic)!;
            hover.Invoke(null, [origin, true]); origin.UpdateLayout();
            Require(picture.Stretch == Microsoft.UI.Xaml.Media.Stretch.Uniform, "Hover-fit setup failed");
            var version = (long)Field(gallery, "_dragPreviewVersion")!;
            using var preview = await (Task<Windows.Graphics.Imaging.SoftwareBitmap?>)typeof(WinUiGalleryIsland)
                .GetMethod("CreateMoveDragPreviewAsync", flags)!.Invoke(gallery, [origin, only, version])!;
            Require(preview is not null && preview.PixelWidth <= 208 && preview.PixelHeight <= 208, "Drag preview is missing or unbounded");
            var pixels = new byte[preview!.PixelWidth * preview.PixelHeight * 4]; preview.CopyToBuffer(pixels.AsBuffer());
            var alphas = pixels.Where((_, index) => index % 4 == 3).ToArray();
            Require(alphas.Max() == 204 && alphas.All(alpha => alpha <= 204) && origin.Opacity == 1, "Preview opacity changed source tiles or did not preserve transparency");
            var sample = ((preview.PixelHeight / 4) * preview.PixelWidth + preview.PixelWidth / 2) * 4;
            Require(pixels[sample + 1] > 180 && pixels[sample] < 10 && pixels[sample + 2] < 10 &&
                picture.Stretch == Microsoft.UI.Xaml.Media.Stretch.UniformToFill, "Drag preview captured black hover-fit margins instead of cropped content");
            picture.Source = originalSource; hover.Invoke(null, [origin, false]);
            var anchor = (Windows.Foundation.Point)typeof(WinUiGalleryIsland).GetField("DragPreviewAnchor", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            Require(anchor.X == 0 && anchor.Y == 0, "Preview is not fastened at its top-left corner");
            var targetItem = Target();
            var over = Drag(token, DragDrop.PreviewDragOverEvent, targetItem);
            Require(over.Effects == DragDropEffects.Move && BrowseView.GetIsGalleryDropTarget(targetItem), "Drop target lacks move effect or highlight");
            Drag(token, DragDrop.PreviewDropEvent, targetItem);
            await WaitUntil(() => File.Exists(Path.Combine(target, files[2].Name)) && Field(browse, "_galleryCommandCancellation") is null);
            Require(files.Take(2).All(file => File.Exists(file.FullPath)), "Dragging an unselected file moved the previous selection");
            Require(!browse.IsGalleryMoveDragActive && !BrowseView.GetIsGalleryDropTarget(targetItem), "Drop retained gesture state");
            await browse.UndoLastOperationAsync();
            Require(files.All(file => File.Exists(file.FullPath)), "Drag move Undo failed");
        });
        await Check("Gallery drag rejects same-folder stale and cancelled drops and groups selected-file moves", async () =>
        {
            var files = state.Files.ToArray(); state.GallerySelection.ReplaceSelection(state.Files, files.Take(2), files[0]);
            var token = await Begin(state.GallerySelection.GetDraggedFiles(state.Files, files[1]));
            await Task.Delay(220); var targetItem = Target();
            var rootItem = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(state.Roots[0]);
            Require(Drag(token, DragDrop.PreviewDragOverEvent, rootItem).Effects == DragDropEffects.None, "Same-folder drop allowed");
            End();
            Require(files.All(file => File.Exists(file.FullPath)), "Cancel mutated files");
            var nextToken = await Begin(state.GallerySelection.GetDraggedFiles(state.Files, files[1]));
            Require(Drag(token, DragDrop.PreviewDragOverEvent, targetItem).Effects == DragDropEffects.None, "Stale token accepted");
            Drag(nextToken, DragDrop.PreviewDropEvent, targetItem);
            await WaitUntil(() => files.Take(2).All(file => !File.Exists(file.FullPath)) && Field(browse, "_galleryCommandCancellation") is null);
            Require(File.Exists(files[2].FullPath), "Group drag moved an unselected file");
            await browse.UndoLastOperationAsync();
            Require(files.All(file => File.Exists(file.FullPath)), "One Undo did not restore the entire drag group");
            var stale = await Begin(state.GallerySelection.GetDraggedFiles(state.Files, state.Files[0]));
            navigation.SelectDestination("Browse");
            Require(!browse.IsGalleryMoveDragActive && (int)Field(browse, "_reconciliationDeferrals")! == 0, "Mode change retained drag deferral");
            Require(files.All(file => File.Exists(file.FullPath)), "Mode change mutated drag files");
        });
        End();
    }
}
