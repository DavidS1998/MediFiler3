using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MediFiler3.Application.Navigation;
using MediFiler3.Interop;
using MediFiler3.Views;

internal static partial class Program
{
    private static async Task RunFolderClickChecksAsync(MediFiler3.MainWindow window, NavigationSessionState state, BrowseView browse, WinUiNavigationIsland navigation)
    {
        await Check("Explicit active-folder row clicks reload the listing from its first file in every view", async () =>
        {
            var directory = Directory.CreateDirectory(Path.Combine(Root, "active-folder-click")).FullName;
            foreach (var name in new[] { "a.png", "b.png", "c.png" }) File.Copy(Fixtures[".png"].FullPath, Path.Combine(directory, name));
            await LoadRestartRootsAsync(window, [directory]); await WaitUntil(() => state.Files.Count == 3);
            var folder = state.ActiveFolder!; browse.SetFolderPinned(true);
            var tree = (TreeView)browse.FindName("FolderTree");
            IEnumerable<DependencyObject> Visuals(DependencyObject parent)
            {
                yield return parent;
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                    foreach (var child in Visuals(VisualTreeHelper.GetChild(parent, i))) yield return child;
            }
            foreach (var view in new[] { "Browse", "Gallery", "Reader" })
            {
                navigation.SelectDestination(view);
                ((NavigationController)Field(browse, "_navigationController")!).SelectFile(state.Files[^1]);
                if (browse.IsReader) browse.JumpReaderPage(state.Reader.Pages.Count - 1);
                await WaitUntil(() => state.CurrentFile?.Name == "c.png");
                await (Task)typeof(BrowseView).GetMethod("ActivateFolderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browse, [folder, null, 0])!;
                Require(state.CurrentFile?.Name == "c.png", "Internal selection notification reloaded the active folder");
                var added = Path.Combine(directory, $"0-{view}.png"); File.Copy(Fixtures[".png"].FullPath, added);
                window.UpdateLayout();
                var item = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(folder)!;
                var header = (FrameworkElement)item.Template.FindName("PART_Header", item);
                var icon = Visuals(header).OfType<FolderTreeIcon>().Single();
                await WaitUntil(() => icon.FolderPath == directory &&
                    icon.Source is System.Windows.Media.Imaging.BitmapSource { IsFrozen: true });
                var label = Visuals(header).OfType<TextBlock>().First(text => text.Text == folder.DisplayName);
                var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent };
                label.RaiseEvent(click);
                try { await WaitUntil(() => state.Files.Any(file => file.FullPath == added) && state.CurrentFile == state.Files[0] &&
                    (!browse.IsReader || browse.ReaderPageIndex == 0)); }
                catch (TimeoutException exception) { throw new InvalidOperationException($"{view} row click: count={click.ClickCount}; handled={click.Handled}; current={state.CurrentFile?.Name}; first={state.Files.FirstOrDefault()?.Name}; added={state.Files.Any(file => file.FullPath == added)}; page={browse.ReaderPageIndex}", exception); }
                Require(ReferenceEquals(folder, state.ActiveFolder) && ReferenceEquals(folder, state.Roots[0]) && folder.DirectFileCount == state.Files.Count,
                    "Active-folder click rebuilt tree nodes or left stale counts");
            }
        });
    }
}
