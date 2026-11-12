using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Windows.Threading;

namespace MediFiler3.Interop;

public sealed partial class WinUiToolbarIsland
{
    private sealed record ReaderPageChoice(int Number, string Filename)
    {
        public string Label => $"{Number}   {Filename}";
        public override string ToString() => Label;
    }
    public async Task<int?> PromptReaderPageAsync(IReadOnlyList<FileEntry> pages, int currentIndex)
    {
        var owner = System.Windows.Window.GetWindow(this);
        if (owner is null || pages.Count == 0) return null;
        var choices = pages.Select((file, index) => new ReaderPageChoice(index + 1, file.Name)).ToArray();
        var selected = -1;
        var result = await WinUiContentDialogHost.ShowAsync(owner, "Go to Reader page or file", () =>
        {
            var closed = false;
            long revealVersion = 0;
            var input = new TextBox { Text = (Math.Clamp(currentIndex, 0, pages.Count - 1) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                PlaceholderText = "Page number or exact image filename" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, "Reader page number or image filename");
            var list = new ListView { ItemsSource = choices, DisplayMemberPath = nameof(ReaderPageChoice.Label), Height = 256,
                SelectionMode = ListViewSelectionMode.Single, HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, "Reader pages: page number and filename");
            list.ContainerContentChanging += (_, args) =>
            {
                if (!closed && !args.InRecycleQueue && args.Item is ReaderPageChoice choice)
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(args.ItemContainer, $"Page {choice.Number}: {choice.Filename}");
            };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = $"Type a page number (1-{pages.Count}) or exact image filename, or select a page below.", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(input); panel.Children.Add(list); panel.Children.Add(error);
            var dialog = new ContentDialog { Content = panel, PrimaryButtonText = "Go", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            void Reveal()
            {
                var request = ++revealVersion;
                var target = selected;
                if (target < 0) return;
                owner.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (!closed && request == revealVersion && list.IsLoaded) list.ScrollIntoView(choices[target]);
                }));
            }
            void Validate()
            {
                if (closed) return;
                selected = ReaderPageLookup.Resolve(pages, input.Text, out var message);
                dialog.IsPrimaryButtonEnabled = selected >= 0; error.Text = message;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(input, message);
                list.SelectedItem = selected >= 0 ? choices[selected] : null;
                Reveal();
            }
            list.SelectionChanged += (_, _) =>
            {
                if (closed || list.SelectedItem is not ReaderPageChoice choice) return;
                if (ReaderPageLookup.Resolve(pages, input.Text, out _) != choice.Number - 1)
                    input.Text = choice.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            };
            list.Loaded += (_, _) => Reveal();
            input.TextChanged += (_, _) => Validate(); Validate();
            dialog.PrimaryButtonClick += (_, args) => { Validate(); args.Cancel = selected < 0; };
            dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); Reveal(); };
            dialog.Closed += (_, _) => { closed = true; revealVersion++; };
            return dialog;
        }, 540);
        return result == ContentDialogResult.Primary && selected >= 0 ? selected : null;
    }
}
