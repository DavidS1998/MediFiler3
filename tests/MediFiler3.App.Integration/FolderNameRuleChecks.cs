using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Persistence;
using MediFiler3.Infrastructure.Persistence;
using MediFiler3.Interop;
using MediFiler3.Views;

internal static partial class Program
{
    private static async Task RunFolderNameRuleChecksAsync(MediFiler3.MainWindow window, NavigationSessionState state,
        BrowseView browse, SettingsView settingsView, ApplicationSettingsService settings, SqliteDatabase database, string directory)
    {
        var navigation = (WinUiNavigationIsland)window.FindName("NavigationIsland");
        var island = (WinUiCarouselSettingsIsland)settingsView.FindName("CarouselSliderIsland");
        void Invoke(string field)
        {
            var button = (Microsoft.UI.Xaml.Controls.Button)Field(island, field)!;
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(button)
                .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        }
        async Task Settings()
        {
            navigation.SelectDestination("Settings");
            await WaitUntil(() => Field(island, "_folderRulesList") is Microsoft.UI.Xaml.Controls.ListView { IsLoaded: true });
        }
        void Edit(string pattern, FolderRuleColor color, int expansion)
        {
            ((Microsoft.UI.Xaml.Controls.TextBox)Field(island, "_folderRuleName")!).Text = pattern;
            ((Microsoft.UI.Xaml.Controls.ColorPicker)Field(island, "_folderRuleColor")!).Color = Microsoft.UI.ColorHelper.FromArgb(255, color.Red, color.Green, color.Blue);
            ((Microsoft.UI.Xaml.Controls.ComboBox)Field(island, "_folderRuleExpanded")!).SelectedIndex = expansion;
        }
        var pink = new FolderRuleColor(255, 113, 206); var blue = new FolderRuleColor(40, 160, 240);
        IEnumerable<string> NativeText(Microsoft.UI.Xaml.DependencyObject parent)
        {
            if (parent is Microsoft.UI.Xaml.Controls.TextBlock text) yield return text.Text;
            for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
                foreach (var value in NativeText(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, index))) yield return value;
        }
        var rootPath = Directory.CreateDirectory(Path.Combine(directory, "folder-rules")).FullName;
        Directory.CreateDirectory(Path.Combine(rootPath, "Album [SET]", "child"));
        Directory.CreateDirectory(Path.Combine(rootPath, "Plain"));
        navigation.SelectDestination("Browse"); browse.SetFolderPinned(true);
        await LoadRestartRootsAsync(window, [rootPath]);
        await WaitUntil(() => state.Roots.Count == 1 && state.Roots[0].Children.Any(folder => folder.Name == "Album [SET]" && folder.HasChildren));
        var root = state.Roots[0]; var match = root.Children.Single(folder => folder.Name == "Album [SET]");
        await RunFolderCountRuleChecksAsync(window, state, browse, settingsView, settings, database, rootPath);

        await Check("Native folder rules editor saves multiple actions priority disabling and removal with depth fallback", async () =>
        {
            await Settings();
            var list = (Microsoft.UI.Xaml.Controls.ListView)Field(island, "_folderRulesList")!;
            while (list.Items.Count > 0)
            {
                var before = list.Items.Count; list.SelectedIndex = 0; Invoke("_folderRuleRemove");
                await WaitUntil(() => list.Items.Count < before);
            }
            Require(list.CanDragItems && list.CanReorderItems && list.AllowDrop, "Native rule reorder handles are not enabled");
            Edit("[SET]", pink, 2); Invoke("_folderRuleSave");
            await WaitUntil(() => settings.Current.FolderNameRules?.Count == 1);
            await WaitUntil(() => list.ContainerFromIndex(0) is Microsoft.UI.Xaml.Controls.ListViewItem row && NativeText(row).Contains("[SET]"));
            Require(NativeText((Microsoft.UI.Xaml.Controls.ListViewItem)list.ContainerFromIndex(0)).Any(text => text.Contains("255, 113, 206") && text.Contains("Collapsed by default")),
                "Rule card does not render its combined actions");
            await browse.FolderRulesCompletion;
            Require(match.RuleTextColor == pink && !match.IsExpanded, "Color/collapse rule did not apply together");
            navigation.SelectDestination("Browse");
            var tree = (TreeView)browse.FindName("FolderTree");
            IEnumerable<DependencyObject> Visuals(DependencyObject parent)
            {
                yield return parent;
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                    foreach (var child in Visuals(VisualTreeHelper.GetChild(parent, index))) yield return child;
            }
            await WaitUntil(() => Visuals(tree).OfType<TextBlock>().Any(label => ReferenceEquals(label.DataContext, match) &&
                label.Foreground is SolidColorBrush brush && brush.Color == Color.FromRgb(255, 113, 206)));
            match.IsExpanded = true;
            await Settings(); Invoke("_folderRuleAdd");
            await WaitUntil(() => ((Microsoft.UI.Xaml.Controls.TextBox)Field(island, "_folderRuleName")!).Text == "");
            Edit("SET", blue, 0); Invoke("_folderRuleSave");
            await WaitUntil(() => settings.Current.FolderNameRules?.Count == 2);
            await browse.FolderRulesCompletion; Require(match.RuleTextColor == pink, "Later rule overrode the first match");
            Invoke("_folderRuleUp");
            await WaitUntil(() => settings.Current.FolderNameRules?[0].Contains == "SET");
            await browse.FolderRulesCompletion;
            Require(match.RuleTextColor == blue && !match.IsExpanded, "Reordering did not apply color and the lower explicit collapse action");
            list = (Microsoft.UI.Xaml.Controls.ListView)Field(island, "_folderRulesList")!;
            await WaitUntil(() => list.ContainerFromIndex(0) is Microsoft.UI.Xaml.Controls.ListViewItem);
            Microsoft.UI.Xaml.Controls.ToggleSwitch? RowToggle(Microsoft.UI.Xaml.DependencyObject parent)
            {
                if (parent is Microsoft.UI.Xaml.Controls.ToggleSwitch toggle) return toggle;
                for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                    if (RowToggle(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i)) is { } found) return found;
                return null;
            }
            RowToggle((Microsoft.UI.Xaml.Controls.ListViewItem)list.ContainerFromIndex(0))!.IsOn = false;
            await WaitUntil(() => settings.Current.FolderNameRules?[0].Enabled == false);
            await browse.FolderRulesCompletion; Require(match.RuleTextColor == pink && !match.IsExpanded, "Disabled rule still matched or collapse did not apply");
            // Exercise the same managed-source order and completion path used by native
            // CanReorderItems, without moving the user's pointer or running an OLE loop.
            var items = (System.Collections.ObjectModel.ObservableCollection<WinUiCarouselSettingsIsland.FolderRuleEditorItem>)Field(island, "_folderRuleItems")!;
            items.Move(0, 1);
            typeof(WinUiCarouselSettingsIsland).GetMethod("CommitFolderRuleDrag", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(island, null);
            await WaitUntil(() => settings.Current.FolderNameRules?[0].Contains == "[SET]");
            await settings.FlushAsync();
            var restored = new ApplicationSettingsService(new SqliteApplicationStateStore(new SqliteDatabase(database.DatabasePath)));
            await restored.InitializeAsync();
            Require(restored.Current.FolderNameRules!.SequenceEqual(settings.Current.FolderNameRules!), "Ordered rule actions did not survive SQLite reload");
            Directory.CreateDirectory(Path.Combine(rootPath, "New [set]", "child"));
            navigation.SelectDestination("Browse"); await browse.RefreshAsync();
            await WaitUntil(() => root.Children.Any(folder => folder.Name == "New [set]" && folder.HasChildren && folder.RuleTextColor == pink));
            Require(!root.Children.Single(folder => folder.Name == "New [set]").IsExpanded, "Newly discovered matching folder did not default closed");
            await Settings();
            list = (Microsoft.UI.Xaml.Controls.ListView)Field(island, "_folderRulesList")!;
            var savedOrder = settings.Current.FolderNameRules!.Select(rule => rule.Id).ToArray();
            items.Move(0, 1); // A cancelled drag has no completion/commit.
            navigation.SelectDestination("Browse"); await Settings();
            list = (Microsoft.UI.Xaml.Controls.ListView)Field(island, "_folderRulesList")!;
            Require(items.Select(item => item.Rule.Id).SequenceEqual(savedOrder) && settings.Current.FolderNameRules!.Select(rule => rule.Id).SequenceEqual(savedOrder),
                "Leaving Settings retained an uncommitted drag order");
            list.SelectedIndex = 0; Invoke("_folderRuleRemove"); await WaitUntil(() => settings.Current.FolderNameRules?.Count == 1);
            Invoke("_folderRuleRemove"); await WaitUntil(() => settings.Current.FolderNameRules?.Count == 0);
            await browse.FolderRulesCompletion;
            Require(match.RuleTextColor is null && !match.IsExpanded && root.Children.All(folder => folder.RuleTextColor is null),
                "Removing rules did not restore default colors or preserved expansion");
            navigation.SelectDestination("Browse");
        });

        await Check("Saving rules applies lower explicit structure actions and reapplies unchanged saves to existing rows", async () =>
        {
            await Settings();
            var list = (Microsoft.UI.Xaml.Controls.ListView)Field(island, "_folderRulesList")!;
            Invoke("_folderRuleAdd"); await WaitUntil(() => list.SelectedItem is WinUiCarouselSettingsIsland.FolderRuleEditorItem { IsDraft: true });
            Edit("[SET]", pink, 0); Invoke("_folderRuleSave"); await WaitUntil(() => settings.Current.FolderNameRules?.Count == 1);
            Invoke("_folderRuleAdd"); await WaitUntil(() => list.SelectedItem is WinUiCarouselSettingsIsland.FolderRuleEditorItem { IsDraft: true });
            ((Microsoft.UI.Xaml.Controls.RadioButtons)Field(island, "_folderRuleKind")!).SelectedIndex = 2;
            ((Microsoft.UI.Xaml.Controls.CheckBox)Field(island, "_folderRuleUseColor")!).IsChecked = false;
            ((Microsoft.UI.Xaml.Controls.ComboBox)Field(island, "_folderRuleExpanded")!).SelectedIndex = 2;
            match.IsExpanded = true;
            Invoke("_folderRuleSave"); await WaitUntil(() => settings.Current.FolderNameRules?.Count == 2);
            await browse.FolderRulesCompletion;
            Require(match.RuleTextColor == pink && !match.IsExpanded, "Color-only rule blocked lower saved collapse or manual state prevented application");
            // Saving an identical rule is still an explicit request to apply its action.
            match.IsExpanded = true;
            Invoke("_folderRuleSave"); await WaitUntil(() => !match.IsExpanded);
            ((Microsoft.UI.Xaml.Controls.ComboBox)Field(island, "_folderRuleExpanded")!).SelectedIndex = 1;
            Require(!match.IsExpanded, "Unsaved expansion applied prematurely");
            Invoke("_folderRuleSave"); await WaitUntil(() => match.IsExpanded);
            ((Microsoft.UI.Xaml.Controls.ComboBox)Field(island, "_folderRuleExpanded")!).SelectedIndex = 3;
            Invoke("_folderRuleSave"); await WaitUntil(() => match.IsHiddenByRule);
            ((Microsoft.UI.Xaml.Controls.ComboBox)Field(island, "_folderRuleExpanded")!).SelectedIndex = 0;
            // All folders still needs an action when expansion is left unchanged.
            ((Microsoft.UI.Xaml.Controls.CheckBox)Field(island, "_folderRuleUseColor")!).IsChecked = true;
            Invoke("_folderRuleSave"); await WaitUntil(() => !match.IsHiddenByRule);
            Require(match.IsExpanded, "Leaving expansion unchanged changed existing expansion");
            navigation.SelectDestination("Browse");
        });

        await Check("Folder rule batches keep dispatcher responsive reject superseded results and detach removed roots", async () =>
        {
            var originalRoots = state.Roots.ToArray();
            var originalFolder = state.ActiveFolder;
            var large = new FolderTreeEntry("rules-scale", rootPath, Enumerable.Range(0, 10000)
                .Select(index => new FolderTreeEntry($"[SET] {index}", Path.Combine(rootPath, "virtual" + index))).ToArray(), isRoot: true);
            try
            {
                var pinkRule = new FolderNameRule("[SET]", pink, false);
                var blueRule = new FolderNameRule("[SET]", blue, false);
                state.ReplaceRoots([large], preserveActiveContext: true);
                browse.SetFolderNameRules([pinkRule]); browse.SetFolderNameRules([blueRule]);
                var pulse = false;
                _ = window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => pulse = true));
                await browse.FolderRulesCompletion.WaitAsync(TimeSpan.FromSeconds(20));
                Require(pulse && large.Children.All(folder => folder.RuleTextColor == blue), "Rule scan blocked input or published superseded colors");
                browse.SetFolderNameRules([pinkRule]); state.ReplaceRoots([], preserveActiveContext: true);
                await browse.FolderRulesCompletion.WaitAsync(TimeSpan.FromSeconds(20));
                Require(large.Children.All(folder => folder.RuleTextColor == blue), "Removed tree accepted late rule results");
            }
            finally
            {
                state.ReplaceRoots(originalRoots, preserveActiveContext: true); state.SetActiveFolder(originalFolder);
                browse.SetFolderNameRules(settings.Current.GetFolderRules());
                await browse.FolderRulesCompletion;
            }
        });
    }
}
