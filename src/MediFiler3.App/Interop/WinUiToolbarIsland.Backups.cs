using MediFiler3.Application.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace MediFiler3.Interop;

public sealed partial class WinUiToolbarIsland
{
    public sealed record BackupPreviewRow(OverwriteBackupItem Item)
    {
        public Guid OperationId => Item.OperationId;
        public string OriginalPath => Item.OriginalPath;
        public string BackupPath => Item.BackupPath;
        public string Summary => $"{(Item.IsDirectory ? "Folder and all contents" : "File")} — saved {Item.SavedUtc.ToLocalTime():g}; expires {Item.ExpiresUtc.ToLocalTime():g}";
        public string Disposition => Item.CanRemove ? "Will be permanently deleted by cleanup" : $"Retained: {Item.RetainedReason}";
    }

    public async Task<bool> ShowBackupPreviewAsync(IReadOnlyList<OverwriteBackupItem> backups,
        Func<Guid, Task> reveal, bool allowClear)
    {
        var owner = System.Windows.Window.GetWindow(this);
        if (owner is null) return false;
        var closed = false;
        var result = await WinUiContentDialogHost.ShowAsync(owner, allowClear ? "Clear overwrite backups" : "Overwrite backups", () =>
        {
            var body = new StackPanel { Spacing = 12 };
            body.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Text =
                "Backups expire after 30 days. Cleanup permanently deletes the saved versions and removes Undo and Redo for their operation groups. Current files remain in place. Folder backups include all their contents. Locations open in File Explorer. Cleanup rechecks each item before removal." });
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
            if (backups.Count == 0) body.Children.Add(new TextBlock { Text = "No stored overwrite backups." });
            else
            {
                var list = new ListView { ItemsSource = backups.Select(item => new BackupPreviewRow(item)).ToArray(),
                    SelectionMode = ListViewSelectionMode.None, MaxHeight = 320,
                    Height = Math.Min(320, backups.Count * 132), HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                    ItemTemplate = (DataTemplate)XamlReader.Load("""
                        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                          <StackPanel Spacing="4" Margin="0,4,0,8">
                            <TextBlock Text="{Binding OriginalPath}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" ToolTipService.ToolTip="{Binding OriginalPath}" />
                            <TextBlock Text="{Binding Summary}" TextWrapping="Wrap" />
                            <HyperlinkButton x:Name="BackupLocation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Tag="{Binding OperationId}" HorizontalAlignment="Left" Padding="0" ToolTipService.ToolTip="{Binding BackupPath}">
                              <TextBlock Text="{Binding BackupPath}" TextTrimming="CharacterEllipsis" />
                            </HyperlinkButton>
                            <TextBlock Text="{Binding Disposition}" TextWrapping="Wrap" />
                          </StackPanel>
                        </DataTemplate>
                        """) };
                async void Reveal(object sender, RoutedEventArgs args)
                {
                    if (sender is not HyperlinkButton { Tag: Guid id } link || !link.IsEnabled) return;
                    link.IsEnabled = false;
                    try { await reveal(id); if (!closed) status.Text = ""; }
                    catch (Exception exception) { if (!closed) status.Text = $"Could not reveal the backup: {exception.Message}"; }
                    finally { if (!closed) link.IsEnabled = true; }
                }
                list.ContainerContentChanging += (_, args) =>
                {
                    if (args.InRecycleQueue) return;
                    args.RegisterUpdateCallback(1, (_, phase) =>
                    {
                        if (phase.ItemContainer.ContentTemplateRoot is FrameworkElement row && row.FindName("BackupLocation") is HyperlinkButton link)
                        { link.Click -= Reveal; link.Click += Reveal; }
                    });
                };
                body.Children.Add(list);
            }
            body.Children.Add(status);
            var dialog = new ContentDialog { Content = body, PrimaryButtonText = allowClear ? "Clear backups" : "",
                IsPrimaryButtonEnabled = backups.Any(item => item.CanRemove), CloseButtonText = allowClear ? "Cancel" : "Close",
                DefaultButton = ContentDialogButton.Close };
            dialog.Closed += (_, _) => closed = true;
            return dialog;
        }, 760);
        return allowClear && result == ContentDialogResult.Primary;
    }
}
