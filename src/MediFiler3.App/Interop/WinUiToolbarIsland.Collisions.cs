using System.Globalization;
using System.IO;
using MediFiler3.Application.Operations;
using MediFiler3.Application.Viewing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace MediFiler3.Interop;

public sealed partial class WinUiToolbarIsland
{
    public IFileCollisionPreviewService? CollisionPreviews { get; set; }

    private Grid CreateCollisionComparison(FileCollision collision, CancellationToken token)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var incoming = CreateCollisionCard("Incoming", collision.SourcePath, collision.IsDirectory, token);
        var existing = CreateCollisionCard("Existing", collision.DestinationPath, collision.ExistingIsDirectory, token);
        Grid.SetColumn(existing, 1);
        grid.Children.Add(incoming);
        grid.Children.Add(existing);
        return grid;
    }

    private Border CreateCollisionCard(string label, string? path, bool directory, CancellationToken token)
    {
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var preview = new Grid { Height = 140, Background = Brush(11, 12, 14) };
        var placeholder = new TextBlock { Text = directory ? "Folder" : "Loading preview…",
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center };
        var image = new Microsoft.UI.Xaml.Controls.Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(image, label + " file preview");
        preview.Children.Add(placeholder);
        preview.Children.Add(image);
        content.Children.Add(preview);
        var missingLabel = directory ? "New folder" : "File to restore";
        content.Children.Add(new TextBlock { Text = path is null ? missingLabel : Path.GetFileName(path),
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        var size = new TextBlock { Text = directory ? "Folder" : "Loading size…" };
        content.Children.Add(size);
        var pathText = new TextBlock { Text = path ?? "Not yet created", TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1, Foreground = Brush(166, 170, 178) };
        ToolTipService.SetToolTip(pathText, path ?? missingLabel);
        content.Children.Add(pathText);
        var card = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(8),
            Background = Brush(28, 30, 34), Child = content };
        if (path is not null && CollisionPreviews is not null)
            _ = LoadCollisionCardAsync(path, image, placeholder, size, CollisionPreviews, token);
        else if (!directory) { placeholder.Text = "Preview unavailable"; size.Text = "Size unavailable"; }
        return card;
    }

    private static async Task LoadCollisionCardAsync(string path, Microsoft.UI.Xaml.Controls.Image image,
        TextBlock placeholder, TextBlock size, IFileCollisionPreviewService previews, CancellationToken token)
    {
        try
        {
            var result = await previews.LoadAsync(path, token);
            if (token.IsCancellationRequested) return;
            size.Text = result.IsDirectory ? "Folder" : result.Size is { } bytes
                ? ViewerMetadataFormatter.Format(new ViewerMetadata(bytes)) : "Size unavailable";
            if (result.Size is { } exact) ToolTipService.SetToolTip(size, exact.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
            placeholder.Text = result.IsDirectory ? "Folder" : "Preview unavailable";
            if (result.ThumbnailPng is not { Length: > 0 } png) return;
            using var stream = new InMemoryRandomAccessStream();
            using var writer = new DataWriter(stream);
            writer.WriteBytes(png);
            await writer.StoreAsync();
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            writer.DetachStream();
            if (token.IsCancellationRequested) return;
            image.Source = bitmap;
            placeholder.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            if (token.IsCancellationRequested) return;
            placeholder.Text = "Preview unavailable";
            if (size.Text == "Loading size…") size.Text = "Size unavailable";
        }
    }
}
