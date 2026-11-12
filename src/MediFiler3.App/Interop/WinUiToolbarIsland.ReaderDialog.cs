using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MediFiler3.Interop;

public sealed partial class WinUiToolbarIsland
{
    public async Task<int?> PromptReaderPageAsync(IReadOnlyList<FileEntry> pages, int currentIndex)
    {
        var owner = System.Windows.Window.GetWindow(this);
        if (owner is null || pages.Count == 0) return null;
        var selected = -1;
        var result = await WinUiContentDialogHost.ShowAsync(owner, "Go to Reader page", () =>
        {
            var closed = false;
            var input = new TextBox { Text = (Math.Clamp(currentIndex, 0, pages.Count - 1) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                PlaceholderText = $"1-{pages.Count}", MaxLength = pages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length };
            var scope = new Microsoft.UI.Xaml.Input.InputScope();
            scope.Names.Add(new Microsoft.UI.Xaml.Input.InputScopeName { NameValue = Microsoft.UI.Xaml.Input.InputScopeNameValue.Number });
            input.InputScope = scope;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, $"Reader page number, 1 to {pages.Count}");
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = $"Page number (1-{pages.Count})" });
            panel.Children.Add(input); panel.Children.Add(error);
            var dialog = new ContentDialog { Content = panel, PrimaryButtonText = "Go", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            input.BeforeTextChanging += (sender, args) =>
            {
                if (closed) { args.Cancel = true; return; }
                args.Cancel = args.NewText.Length > 0 && (args.NewText.Any(character => character is < '0' or > '9') ||
                    ReaderPageLookup.Resolve(pages, args.NewText, out _) < 0);
            };
            void Validate()
            {
                if (closed) return;
                selected = ReaderPageLookup.Resolve(pages, input.Text, out var message);
                dialog.IsPrimaryButtonEnabled = selected >= 0; error.Text = message;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(input, message);
            }
            input.TextChanged += (_, _) => Validate(); Validate();
            dialog.PrimaryButtonClick += (_, args) => { Validate(); args.Cancel = selected < 0; };
            dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
            dialog.Closed += (_, _) => closed = true;
            return dialog;
        }, 480);
        return result == ContentDialogResult.Primary && selected >= 0 ? selected : null;
    }
}
