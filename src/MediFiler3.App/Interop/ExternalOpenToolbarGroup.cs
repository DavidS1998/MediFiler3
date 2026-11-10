using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using MediFiler3.Application.Files;
using Windows.Storage.Streams;

namespace MediFiler3.Interop;

internal sealed class ExternalOpenToolbarGroup
{
    public AppBarButton OpenButton { get; }
    public AppBarButton ChooseButton { get; }
    private byte[]? _iconBytes;
    private long _iconVersion;

    public ExternalOpenToolbarGroup(Action open, Action choose)
    {
        OpenButton = new AppBarButton { Icon = new SymbolIcon(Symbol.OpenFile), Width = 32,
            LabelPosition = CommandBarLabelPosition.Collapsed };
        ChooseButton = new AppBarButton { Icon = new FontIcon { Glyph = "\uE8A7",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons") }, Label = "Open with...", Width = 32,
            LabelPosition = CommandBarLabelPosition.Collapsed };
        OpenButton.Click += (_, _) => open();
        ChooseButton.Click += (_, _) => choose();
        ToolTipService.SetToolTip(ChooseButton, "Choose an application (Ctrl+Shift+Enter)");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ChooseButton, "Open with another application");
        SetState(new(false, false, null));
    }

    public void SetState(ExternalOpenState state)
    {
        OpenButton.Label = state.DefaultLabel;
        OpenButton.IsEnabled = state.CanOpenDefault;
        ChooseButton.IsEnabled = state.CanChooseApplication;
        ToolTipService.SetToolTip(OpenButton, state.DefaultLabel + " (Ctrl+Enter)");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(OpenButton, state.DefaultLabel);
        var bytes = state.Application?.IconPng;
        if (ReferenceEquals(bytes, _iconBytes) && OpenButton.Icon is not null) return;
        _iconBytes = bytes;
        var version = ++_iconVersion;
        OpenButton.Icon = new SymbolIcon(Symbol.OpenFile);
        if (bytes is not null) _ = LoadIconAsync(bytes, version);
    }

    private async Task LoadIconAsync(byte[] bytes, long version)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using var writer = new DataWriter(stream);
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            if (version == _iconVersion) OpenButton.Icon = new ImageIcon { Source = bitmap };
            writer.DetachStream();
        }
        catch (Exception) { /* Retain the generic open icon when a registered icon cannot be decoded. */ }
    }
}
