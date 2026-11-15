using System.IO;
using System.Windows.Threading;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Persistence;
using MediFiler3.Infrastructure.Persistence;
using MediFiler3.Interop;
using MediFiler3.Views;

internal static partial class Program
{
    private static IEnumerable<Microsoft.UI.Xaml.DependencyObject> CountRuleVisuals(Microsoft.UI.Xaml.DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in CountRuleVisuals(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i))) yield return child;
    }

    private static async Task RunFolderCountRuleChecksAsync(MediFiler3.MainWindow window, NavigationSessionState state,
        BrowseView browse, SettingsView settingsView, ApplicationSettingsService settings, SqliteDatabase database, string rootPath)
    {
        var navigation = (WinUiNavigationIsland)window.FindName("NavigationIsland");
        var island = (WinUiCarouselSettingsIsland)settingsView.FindName("CarouselSliderIsland");
        void Invoke(string name)
        {
            var button = (Microsoft.UI.Xaml.Controls.Button)Field(island, name)!;
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(button)
                .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        }
        await Check("Settings cards and count-rule rows show defaults inline toggles actual colors and decorative carets", async () =>
        {
            navigation.SelectDestination("Settings");
            await WaitUntil(() => Field(island, "_folderRulesList") is Microsoft.UI.Xaml.Controls.ListView { IsLoaded: true });
            var source = (Microsoft.UI.Xaml.Hosting.DesktopWindowXamlSource)Field(island, "_xamlSource")!;
            var cards = CountRuleVisuals(source.Content).OfType<Microsoft.UI.Xaml.Controls.Border>()
                .Where(card => card.Tag is string title && new[] { "Folder tree", "Files", "Carousel", "Continuous reader", "System" }.Contains(title)).ToArray();
            Require(cards.Select(card => (string)card.Tag).SequenceEqual(new[] { "Folder tree", "Files", "Carousel", "Continuous reader", "System" }), "Settings groups are not five cards");
            var systemText = CountRuleVisuals(cards[4]).OfType<Microsoft.UI.Xaml.Controls.TextBlock>().Select(text => text.Text).ToArray();
            Require(systemText.Contains("Overwrite backups") && systemText.Contains("Thumbnail cache"), "System card lost maintenance controls");
            var carousel = (Microsoft.UI.Xaml.Controls.Slider)Field(island, "_slider")!;
            Require(carousel.Minimum % carousel.TickFrequency == 0 && carousel.StepFrequency == carousel.TickFrequency &&
                carousel.Maximum % carousel.TickFrequency == 0, "Carousel native tick lattice misses valid stops");
            var expansion = (Microsoft.UI.Xaml.Controls.ComboBox)Field(island, "_folderRuleExpanded")!;
            Require(expansion.Header is null && expansion.Items.Cast<string>().SequenceEqual(new[] {
                "Leave expansion unchanged", "Expanded by default", "Collapsed by default", "Hide" }), "Expansion options do not explain their actions");
            var list = (Microsoft.UI.Xaml.Controls.ListView)Field(island, "_folderRulesList")!;
            Require(list.Items.Count == 2 && settings.Current.GetFolderRules()[0].Id == FolderNameRule.EmptyFolderRuleId &&
                settings.Current.GetFolderRules()[1] is { Kind: FolderRuleKind.AllFolders, Enabled: false, ExpandedByDefault: false, ApplyToRootFolders: false }, "Missing pre-existing default rules");
            list.SelectedIndex = 1;
            var defaultItem = (WinUiCarouselSettingsIsland.FolderRuleEditorItem)list.SelectedItem;
            Require(defaultItem.Summary.Contains("Collapsed by default") && !defaultItem.IsDirty, "Default summary is not glanceable");
            Require(((Microsoft.UI.Xaml.Controls.CheckBox)Field(island, "_folderRuleIncludeRoots")!).IsChecked == false, "Root actions must default off");
            Invoke("_folderRuleRemove"); await WaitUntil(() => list.Items.Count == 1);
            await WaitUntil(() => list.ContainerFromIndex(0) is Microsoft.UI.Xaml.Controls.ListViewItem);
            var row = (Microsoft.UI.Xaml.Controls.ListViewItem)list.ContainerFromIndex(0);
            Require(CountRuleVisuals(row).OfType<Microsoft.UI.Xaml.Controls.TextBlock>().Any(text => text.Text == "0 files" &&
                text.Foreground is Microsoft.UI.Xaml.Media.SolidColorBrush brush && brush.Color == Microsoft.UI.ColorHelper.FromArgb(255, 96, 96, 96)), "Rule row has no actual dark-grey text preview");
            var toggle = CountRuleVisuals(row).OfType<Microsoft.UI.Xaml.Controls.ToggleSwitch>().Single();
            toggle.IsOn = false; await WaitUntil(() => settings.Current.FolderRulesConfigured && settings.Current.FolderNameRules?[0].Enabled == false);
            toggle.IsOn = true; await WaitUntil(() => settings.Current.FolderNameRules?[0].Enabled == true);
            Require(((Microsoft.UI.Xaml.Controls.TextBox)Field(island, "_folderRuleName")!).PlaceholderText != "[SET]", "Name placeholder does not describe matching");
            Invoke("_folderRuleAdd");
            await WaitUntil(() => ((Microsoft.UI.Xaml.Controls.TextBox)Field(island, "_folderRuleName")!).Text == "" &&
                ((Microsoft.UI.Xaml.Controls.RadioButtons)Field(island, "_folderRuleKind")!).SelectedIndex == 0);
            Require(list.Items.Count == 2 && list.SelectedIndex == 1 &&
                list.SelectedItem is WinUiCarouselSettingsIsland.FolderRuleEditorItem { IsDraft: true, Selected: true }, "Add did not select a placeholder row");
            var draft = (WinUiCarouselSettingsIsland.FolderRuleEditorItem)list.SelectedItem;
            Require(draft.Summary.Contains("Unsaved") && settings.Current.FolderNameRules?.Count == 1, "Draft was not marked unsaved or leaked into settings");
            await WaitUntil(() => list.ContainerFromIndex(1) is Microsoft.UI.Xaml.Controls.ListViewItem);
            var draftRow = (Microsoft.UI.Xaml.Controls.ListViewItem)list.ContainerFromIndex(1);
            var accent = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            Require(CountRuleVisuals(draftRow).OfType<Microsoft.UI.Xaml.Controls.Border>().Any(border => border.BorderThickness.Left == 2 &&
                border.BorderBrush is Microsoft.UI.Xaml.Media.SolidColorBrush brush && brush.Color == accent), "Selected rule has no accent border");
            ((Microsoft.UI.Xaml.Controls.RadioButtons)Field(island, "_folderRuleKind")!).SelectedIndex = 1;
            ((Microsoft.UI.Xaml.Controls.TextBox)Field(island, "_folderRuleMinimum")!).Text = "101";
            ((Microsoft.UI.Xaml.Controls.TextBox)Field(island, "_folderRuleMaximum")!).Text = "";
            ((Microsoft.UI.Xaml.Controls.ColorPicker)Field(island, "_folderRuleColor")!).Color = Microsoft.UI.ColorHelper.FromArgb(255, 255, 0, 0);
            ((Microsoft.UI.Xaml.Controls.ComboBox)Field(island, "_folderRuleExpanded")!).SelectedIndex = 2;
            Require(draft.Name == "101+ files" && draft.Rule.TextColor == new FolderRuleColor(255, 0, 0) && draft.Rule.ExpandedByDefault == false,
                "Draft row did not preview settings live");
            Invoke("_folderRuleUp"); await WaitUntil(() => list.SelectedIndex == 0);
            Require(settings.Current.FolderNameRules?.Count == 1 && settings.Current.FolderNameRules[0].Id == FolderNameRule.EmptyFolderRuleId,
                "Moving an unsaved draft published it");
            Invoke("_folderRuleDown"); await WaitUntil(() => list.SelectedIndex == 1);
            Invoke("_folderRuleSave"); await WaitUntil(() => settings.Current.FolderNameRules?.Count == 2);
            Require(list.SelectedItem is WinUiCarouselSettingsIsland.FolderRuleEditorItem { IsDraft: false }, "Saved draft still shows as unsaved");
            await WaitUntil(() => list.ContainerFromIndex(1) is Microsoft.UI.Xaml.Controls.ListViewItem);
            row = (Microsoft.UI.Xaml.Controls.ListViewItem)list.ContainerFromIndex(1);
            bool Caret(string glyph, bool visible) => CountRuleVisuals(row).OfType<Microsoft.UI.Xaml.Controls.FontIcon>().Any(icon => icon.Glyph == glyph &&
                icon.Visibility == (visible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed));
            Require(Caret("\uE76C", true), "Closed-default rule lacks a right caret");
            Require(CountRuleVisuals(row).OfType<Microsoft.UI.Xaml.Controls.TextBlock>().Any(text => text.Text == "101+ files" &&
                text.Foreground is Microsoft.UI.Xaml.Media.SolidColorBrush brush && brush.Color == Microsoft.UI.ColorHelper.FromArgb(255, 255, 0, 0)), "Count rule lacks red text preview");
            Require(!CountRuleVisuals(row).OfType<Microsoft.UI.Xaml.Controls.TreeView>().Any(), "Rule caret simulates a folder tree");
            ((Microsoft.UI.Xaml.Controls.ComboBox)Field(island, "_folderRuleExpanded")!).SelectedIndex = 1;
            Invoke("_folderRuleSave"); await WaitUntil(() => settings.Current.FolderNameRules?[1].ExpandedByDefault == true);
            await WaitUntil(() => list.ContainerFromIndex(1) is Microsoft.UI.Xaml.Controls.ListViewItem);
            row = (Microsoft.UI.Xaml.Controls.ListViewItem)list.ContainerFromIndex(1); Require(Caret("\uE70D", true), "Open-default rule lacks a down caret");
            ((Microsoft.UI.Xaml.Controls.ComboBox)Field(island, "_folderRuleExpanded")!).SelectedIndex = 0;
            Invoke("_folderRuleSave"); await WaitUntil(() => settings.Current.FolderNameRules?[1].ExpandedByDefault is null);
            await WaitUntil(() => list.ContainerFromIndex(1) is Microsoft.UI.Xaml.Controls.ListViewItem);
            row = (Microsoft.UI.Xaml.Controls.ListViewItem)list.ContainerFromIndex(1); Require(Caret("\uE76C", false), "Rule with no expansion action has a visible caret");
            // Saved rows also preview drafts, while toggles and reorder publish saved actions only.
            var savedItem = (WinUiCarouselSettingsIsland.FolderRuleEditorItem)list.SelectedItem;
            ((Microsoft.UI.Xaml.Controls.TextBox)Field(island, "_folderRuleMinimum")!).Text = "250";
            ((Microsoft.UI.Xaml.Controls.ColorPicker)Field(island, "_folderRuleColor")!).Color = Microsoft.UI.ColorHelper.FromArgb(255, 0, 0, 255);
            ((Microsoft.UI.Xaml.Controls.ComboBox)Field(island, "_folderRuleExpanded")!).SelectedIndex = 3;
            ((Microsoft.UI.Xaml.Controls.CheckBox)Field(island, "_folderRuleIncludeRoots")!).IsChecked = true;
            Require(savedItem.IsDirty && savedItem.Summary.Contains("Unsaved") && savedItem.Summary.Contains("Hidden") && savedItem.Name == "250+ files",
                "Saved rule does not preview unsaved actions live");
            Require(settings.Current.FolderNameRules![1] is { MinimumFiles: 101, HideFolder: false, ApplyToRootFolders: false }, "Live edits applied before Save");
            toggle = CountRuleVisuals(row).OfType<Microsoft.UI.Xaml.Controls.ToggleSwitch>().Single();
            toggle.IsOn = false; await WaitUntil(() => settings.Current.FolderNameRules?[1].Enabled == false);
            Require(settings.Current.FolderNameRules![1] is { MinimumFiles: 101, HideFolder: false }, "Enabled toggle published unsaved actions");
            Invoke("_folderRuleUp"); await WaitUntil(() => list.SelectedIndex == 0);
            Require(settings.Current.FolderNameRules![0] is { MinimumFiles: 101, HideFolder: false }, "Reorder published unsaved actions");
            Invoke("_folderRuleSave"); await WaitUntil(() => settings.Current.FolderNameRules![0].HideFolder);
            Require(settings.Current.FolderNameRules![0] is { MinimumFiles: 250, ApplyToRootFolders: true, Enabled: false } &&
                list.SelectedItem is WinUiCarouselSettingsIsland.FolderRuleEditorItem { IsDirty: false }, "Saving actions did not commit and clear Unsaved");
            await settings.FlushAsync();
            var restored = new ApplicationSettingsService(new SqliteApplicationStateStore(new SqliteDatabase(database.DatabasePath))); await restored.InitializeAsync();
            Require(restored.Current.GetFolderRules().SequenceEqual(settings.Current.GetFolderRules()), "Count ranges/actions were not persisted");
            navigation.SelectDestination("Browse");
        });

        await Check("Count rules refresh asynchronously on direct-count changes and never equate unknown with zero", async () =>
        {
            var roots = state.Roots.ToArray(); var active = state.ActiveFolder;
            var synthetic = new FolderTreeEntry("counts", rootPath, Enumerable.Range(0, 70).Select(index =>
                new FolderTreeEntry("count" + index, Path.Combine(rootPath, "virtual" + index), directFileCount: index == 0 ? null : 0)).ToArray(), isRoot: true);
            var red = new FolderNameRule("", new(255, 0, 0)) { Kind = FolderRuleKind.FileCount, MinimumFiles = 101 };
            try
            {
                state.ReplaceRoots([synthetic], preserveActiveContext: true);
                browse.SetFolderNameRules([FolderNameRule.EmptyFolderDefault(), red]); await browse.FolderRulesCompletion;
                Require(synthetic.Children[0].RuleTextColor is null && synthetic.Children[1].RuleTextColor == new FolderRuleColor(96, 96, 96), "Unknown counts were colored as empty");
                synthetic.Children[35].UpdateDirectFileCount(101); await browse.FolderRulesCompletion;
                Require(synthetic.Children[35].RuleTextColor == new FolderRuleColor(255, 0, 0) && synthetic.Children[34].RuleTextColor == new FolderRuleColor(96, 96, 96), "Count observer updated the wrong folder");
                synthetic.Children[35].UpdateDirectFileCount(0); synthetic.Children[36].UpdateDirectFileCount(1000);
                synthetic.Children[37].UpdateDirectFileCount(101); synthetic.Children[37].UpdateDirectFileCount(0);
                await browse.FolderRulesCompletion;
                Require(synthetic.Children[35].RuleTextColor == new FolderRuleColor(96, 96, 96) && synthetic.Children[36].RuleTextColor == new FolderRuleColor(255, 0, 0) &&
                    synthetic.Children[37].RuleTextColor == new FolderRuleColor(96, 96, 96), "Count ranges did not reconcile latest values");
                Require(synthetic.Children.Count == 70 && synthetic.Children.All(child => child.Children.Count == 0), "Count rules created simulated subfolders");
            }
            finally
            {
                state.ReplaceRoots(roots, preserveActiveContext: true); state.SetActiveFolder(active);
                browse.SetFolderNameRules(settings.Current.GetFolderRules()); await browse.FolderRulesCompletion;
            }
        });

        await Check("Hide rules remove real tree rows reversibly and apply to roots only when requested", async () =>
        {
            navigation.SelectDestination("Browse");
            var root = state.Roots.Single(); root.IsExpanded = true;
            var plain = root.Children.Single(folder => folder.Name == "Plain");
            var active = state.ActiveFolder;
            var tree = (System.Windows.Controls.TreeView)browse.FindName("FolderTree");
            tree.UpdateLayout();
            await WaitUntil(() => tree.ItemContainerGenerator.ContainerFromItem(root) is System.Windows.Controls.TreeViewItem);
            var rootRow = (System.Windows.Controls.TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(root);
            rootRow.UpdateLayout();
            await WaitUntil(() => rootRow.ItemContainerGenerator.ContainerFromItem(plain) is System.Windows.Controls.TreeViewItem);
            var plainRow = (System.Windows.Controls.TreeViewItem)rootRow.ItemContainerGenerator.ContainerFromItem(plain);
            var hide = new FolderNameRule("", null) { Kind = FolderRuleKind.AllFolders, HideFolder = true };
            try
            {
                browse.SetFolderNameRules([hide]); await browse.FolderRulesCompletion;
                await WaitUntil(() => plainRow.Visibility == System.Windows.Visibility.Collapsed);
                Require(rootRow.Visibility == System.Windows.Visibility.Visible && !root.IsHiddenByRule && plain.IsHiddenByRule,
                    "Hide did not exclude roots by default");
                browse.SetFolderNameRules([hide with { ApplyToRootFolders = true }]); await browse.FolderRulesCompletion;
                await WaitUntil(() => rootRow.Visibility == System.Windows.Visibility.Collapsed);
                Require(ReferenceEquals(active, state.ActiveFolder) && root.Children.Contains(plain), "Hide changed navigation or removed tree data");
                browse.SetFolderNameRules([]); await browse.FolderRulesCompletion;
                await WaitUntil(() => rootRow.Visibility == System.Windows.Visibility.Visible && plainRow.Visibility == System.Windows.Visibility.Visible);
            }
            finally { browse.SetFolderNameRules(settings.Current.GetFolderRules()); await browse.FolderRulesCompletion; }
        });
    }
}
