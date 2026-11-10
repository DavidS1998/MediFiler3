using MediFiler3.Application.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MediFiler3.Interop;

public sealed partial class WinUiToolbarIsland
{
    public async Task<string?> PromptTextAsync(string title, string message, string initialValue = "", string primaryAction = "OK")
    {
        var owner = System.Windows.Window.GetWindow(this);
        if (owner is null) return null;
        TextBox? input = null;
        var entered = false;
        var result = await WinUiContentDialogHost.ShowAsync(owner, title, () =>
        {
            var content = new StackPanel { Spacing = 14 };
            content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            input = new TextBox { Text = initialValue };
            content.Children.Add(input);
            var dialog = new ContentDialog { Content = content, PrimaryButtonText = primaryAction,
                CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
            input.KeyDown += (_, args) =>
            {
                if (args.Key != Windows.System.VirtualKey.Enter) return;
                args.Handled = true;
                entered = true;
                dialog.Hide();
            };
            return dialog;
        });
        return result == ContentDialogResult.Primary || entered ? input?.Text.Trim() : null;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmAction)
    {
        var owner = System.Windows.Window.GetWindow(this);
        if (owner is null) return false;
        var result = await WinUiContentDialogHost.ShowAsync(owner, title, () => new ContentDialog
        {
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirmAction, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
        }, 540);
        return result == ContentDialogResult.Primary;
    }

    public async Task<FileCollisionResolution> ResolveCollisionAsync(FileCollision collision)
    {
        var owner = System.Windows.Window.GetWindow(this);
        if (owner is null) return new(FileCollisionChoice.Cancel);
        using var previewsCancellation = new CancellationTokenSource();
        try
        {
            var result = await WinUiContentDialogHost.ShowAsync(owner, "Name collision", () =>
            {
                var dialog = new ContentDialog
                {
                    Content = CreateCollisionComparison(collision, previewsCancellation.Token),
                    PrimaryButtonText = "Rename", SecondaryButtonText = "Overwrite", CloseButtonText = "Cancel",
                    IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(collision.SuggestedName),
                    IsSecondaryButtonEnabled = collision.CanOverwrite,
                    DefaultButton = ContentDialogButton.Close
                };
                dialog.Closed += (_, _) => previewsCancellation.Cancel();
                dialog.Opened += (_, _) =>
                {
                    SetDialogButtonTooltip(dialog, "PrimaryButton", $"Rename to {collision.SuggestedName}");
                    if (collision.IsDirectory && collision.CanOverwrite)
                        SetDialogButtonTooltip(dialog, "SecondaryButton", "Replace the whole destination folder, including its contents; preserve it for Undo.");
                };
                return dialog;
            }, 720);
            return result switch
            {
                ContentDialogResult.Primary => new(FileCollisionChoice.Rename, collision.SuggestedName),
                ContentDialogResult.Secondary => new(FileCollisionChoice.Overwrite),
                _ => new(FileCollisionChoice.Cancel)
            };
        }
        finally { previewsCancellation.Cancel(); }
    }

    private static void SetDialogButtonTooltip(DependencyObject parent, string name, string tooltip)
    {
        if (parent is Button { Name: var buttonName } button && buttonName == name)
            ToolTipService.SetToolTip(button, tooltip);
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
            SetDialogButtonTooltip(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, index), name, tooltip);
    }
}
