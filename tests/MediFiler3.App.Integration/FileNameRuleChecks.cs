using System.IO;
using System.Windows.Media;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Persistence;
using MediFiler3.Infrastructure.Persistence;
using MediFiler3.Interop;
using MediFiler3.Views;
using Native = Microsoft.UI.Xaml.Controls;

internal static partial class Program
{
    private static async Task RunFileNameRuleChecksAsync(MediFiler3.MainWindow window, NavigationSessionState state,
        BrowseView browse, SettingsView settingsView, ApplicationSettingsService settings, SqliteDatabase database, string directory)
    {
        var navigation = (WinUiNavigationIsland)window.FindName("NavigationIsland");
        var island = (WinUiCarouselSettingsIsland)settingsView.FindName("CarouselSliderIsland");
        void Invoke(string name)
        {
            var button = (Native.Button)Field(island, name)!;
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(button)
                .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        }
        async Task Settings()
        {
            navigation.SelectDestination("Settings");
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            await WaitUntil(() => Field(island, "_fileRulesList") is Native.ListView { IsLoaded: true, ItemsSource: not null });
            ((Native.ListView)Field(island, "_fileRulesList")!).StartBringIntoView();
        }
        bool Caption(byte r, byte g, byte b) => window.TitleBarBackground is SolidColorBrush brush && brush.Color == Color.FromRgb(r, g, b);
        Native.GridView GalleryGrid() => (Native.GridView)Field((WinUiGalleryIsland)Field(browse, "_gallery")!, "_grid")!;
        async Task<Native.GridView> ReadyGallery()
        {
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            await WaitUntil(() => Field(browse, "_gallery") is WinUiGalleryIsland gallery && Field(gallery, "_grid") is Native.GridView { IsLoaded: true } grid &&
                grid.Items.Count == state.Files.Count && state.Files.All(file => grid.ContainerFromItem(file) is Native.GridViewItem { ContentTemplateRoot: Native.Grid }));
            return GalleryGrid();
        }
        Microsoft.UI.Xaml.Media.LinearGradientBrush Gradient(Native.GridView grid, MediFiler3.Domain.Files.FileEntry file)
        {
            var container = (Native.GridViewItem)grid.ContainerFromItem(file);
            return (Microsoft.UI.Xaml.Media.LinearGradientBrush)((Native.Border)((Native.Grid)container.ContentTemplateRoot).Children[2]).Background;
        }
        var rootPath = Directory.CreateDirectory(Path.Combine(directory, "file-color-rules")).FullName;
        File.Copy(Fixtures[".png"].FullPath, Path.Combine(rootPath, "[SET] page.png"));
        File.Copy(Fixtures[".png"].FullPath, Path.Combine(rootPath, "plain.png"));
        await Check("Filename color rules have live saved previews priority toggles draft isolation and SQLite persistence", async () =>
        {
            await Settings();
            var list = (Native.ListView)Field(island, "_fileRulesList")!;
            Require(list.Items.Count == 0 && list.CanReorderItems, "File rules did not start empty with native reorder");
            Invoke("_fileRuleAdd"); await WaitUntil(() => list.SelectedItem is WinUiCarouselSettingsIsland.FolderRuleEditorItem { IsDraft: true });
            ((Native.TextBox)Field(island, "_fileRuleName")!).Text = "[SET]";
            ((Native.ColorPicker)Field(island, "_fileRuleColor")!).Color = Microsoft.UI.ColorHelper.FromArgb(255, 255, 113, 206);
            var draft = (WinUiCarouselSettingsIsland.FolderRuleEditorItem)list.SelectedItem;
            Require(draft.Name == "[SET]" && draft.Summary.Contains("Unsaved") && settings.Current.FileNameRules is null, $"Draft preview mismatch: name={draft.Name}, summary={draft.Summary}, saved={settings.Current.FileNameRules?.Count.ToString() ?? "null"}");
            Invoke("_fileRuleSave"); await WaitUntil(() => settings.Current.FileNameRules?.Count == 1);
            ((Native.ColorPicker)Field(island, "_fileRuleColor")!).Color = Microsoft.UI.ColorHelper.FromArgb(255, 10, 20, 30);
            Require(((WinUiCarouselSettingsIsland.FolderRuleEditorItem)list.SelectedItem).IsDirty && settings.Current.FileNameRules![0].Color.Red == 255,
                "Saved file rule failed to preview separately");
            navigation.SelectDestination("Browse"); await Settings();
            list = (Native.ListView)Field(island, "_fileRulesList")!;
            Require(!((WinUiCarouselSettingsIsland.FolderRuleEditorItem)list.SelectedItem).IsDirty, "Leaving Settings kept unsaved edits");
            Invoke("_fileRuleAdd"); await WaitUntil(() => list.SelectedItem is WinUiCarouselSettingsIsland.FolderRuleEditorItem { IsDraft: true });
            ((Native.TextBox)Field(island, "_fileRuleName")!).Text = ".png";
            ((Native.ColorPicker)Field(island, "_fileRuleColor")!).Color = Microsoft.UI.ColorHelper.FromArgb(255, 20, 160, 240);
            Invoke("_fileRuleSave"); await WaitUntil(() => settings.Current.FileNameRules?.Count == 2);
            Invoke("_fileRuleUp"); await WaitUntil(() => settings.Current.FileNameRules?[0].Contains == ".png");
            Invoke("_fileRuleDown"); await WaitUntil(() => settings.Current.FileNameRules?[0].Contains == "[SET]");
            await WaitUntil(() => list.ContainerFromIndex(1) is Native.ListViewItem);
            CountRuleVisuals((Native.ListViewItem)list.ContainerFromIndex(1)).OfType<Native.ToggleSwitch>().Single().IsOn = false;
            await WaitUntil(() => settings.Current.FileNameRules?[1].Enabled == false);
            await settings.FlushAsync();
            var restored = new ApplicationSettingsService(new SqliteApplicationStateStore(new SqliteDatabase(database.DatabasePath)));
            await restored.InitializeAsync();
            Require(restored.Current.FileNameRules!.SequenceEqual(settings.Current.FileNameRules!), "File rule order/colors/enabled state did not persist");
        });
        await Check("File colors reach native Browse caption and Gallery gradient while toolbar and Reader remain unchanged", async () =>
        {
            navigation.SelectDestination("Browse"); await LoadRestartRootsAsync(window, [rootPath]);
            await WaitUntil(() => state.Files.Count == 2);
            var tagged = state.Files.Single(file => file.Name.Contains("[SET]")); var plain = state.Files.Single(file => file.Name == "plain.png");
            state.SetCurrentFile(tagged); await WaitUntil(() => Caption(255, 113, 206));
            Require(Field(window, "_lastCaptionColorResult") is int result && result >= 0 &&
                Field(window, "_lastNativeCaptionColor") is uint nativeColor && nativeColor == 0x00CE71FF, "Native DWM caption write did not accept the chosen RGB");
            Require(((System.Windows.Controls.Border)window.FindName("ToolbarFrame")).Background is SolidColorBrush toolbar && toolbar.Color == Colors.Black,
                "File color changed the toolbar");
            state.SetCurrentFile(plain); await WaitUntil(() => Caption(0, 0, 0));
            state.SetCurrentFile(tagged); await WaitUntil(() => Caption(255, 113, 206));
            navigation.SelectDestination("Gallery"); var grid = await ReadyGallery();
            var gallery = (WinUiGalleryIsland)Field(browse, "_gallery")!;
            await gallery.FileColorsCompletion;
            await WaitUntil(() => Gradient(grid, tagged).GradientStops[1].Color == Microsoft.UI.ColorHelper.FromArgb(204, 255, 113, 206));
            Require(Gradient(grid, tagged).GradientStops[0].Color.A == 0 && Gradient(grid, plain).GradientStops[1].Color == Microsoft.UI.ColorHelper.FromArgb(204, 0, 0, 0),
                "Unmatched gradient or fade opacity changed");
            Require(Caption(0, 0, 0), "Gallery retained a file-colored native caption");
            navigation.SelectDestination("Reader"); await WaitUntil(() => browse.IsReader);
            Require(Caption(0, 0, 0), "Reader used file colors");
            navigation.SelectDestination("Browse"); await WaitUntil(() => Caption(255, 113, 206));
            await Settings();
            var list = (Native.ListView)Field(island, "_fileRulesList")!;
            list.SelectedIndex = 0;
            ((Native.ColorPicker)Field(island, "_fileRuleColor")!).Color = Microsoft.UI.ColorHelper.FromArgb(255, 40, 170, 60);
            Invoke("_fileRuleSave"); await WaitUntil(() => settings.Current.FileNameRules![0].Color.Green == 170);
            navigation.SelectDestination("Gallery"); grid = await ReadyGallery(); await gallery.FileColorsCompletion;
            await WaitUntil(() => Gradient(grid, tagged).GradientStops[1].Color == Microsoft.UI.ColorHelper.FromArgb(204, 40, 170, 60));
            navigation.SelectDestination("Browse"); await WaitUntil(() => Caption(40, 170, 60));
            await Settings(); list = (Native.ListView)Field(island, "_fileRulesList")!; list.SelectedIndex = 0; Invoke("_fileRuleRemove"); await WaitUntil(() => settings.Current.FileNameRules?.Count == 1);
            Invoke("_fileRuleRemove"); await WaitUntil(() => settings.Current.FileNameRules?.Count == 0);
            navigation.SelectDestination("Gallery"); grid = await ReadyGallery(); await gallery.FileColorsCompletion;
            await WaitUntil(() => Gradient(grid, tagged).GradientStops[1].Color == Microsoft.UI.ColorHelper.FromArgb(204, 0, 0, 0));
            navigation.SelectDestination("Browse"); Require(Caption(0, 0, 0), "Removing file rules left caption color behind");
        });
    }
}
