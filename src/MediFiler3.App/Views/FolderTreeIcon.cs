using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using MediFiler3.Services;

namespace MediFiler3.Views;

public sealed class FolderTreeIcon : Image
{
    public static readonly DependencyProperty FolderPathProperty = DependencyProperty.Register(
        nameof(FolderPath), typeof(string), typeof(FolderTreeIcon),
        new PropertyMetadata(null, (sender, _) => ((FolderTreeIcon)sender).Refresh()));

    private static readonly ImageSource Fallback = CreateFallback();
    private CancellationTokenSource? _request;

    public string? FolderPath
    {
        get => (string?)GetValue(FolderPathProperty);
        set => SetValue(FolderPathProperty, value);
    }

    public FolderTreeIcon()
    {
        Source = Fallback;
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => CancelRequest();
        IsVisibleChanged += (_, _) => Refresh();
    }

    protected override AutomationPeer? OnCreateAutomationPeer() => null;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if ((oldDpi.DpiScaleX <= 1) != (newDpi.DpiScaleX <= 1)) Refresh();
    }

    private async void Refresh()
    {
        CancelRequest();
        Source = Fallback;
        if (!IsLoaded || !IsVisible || FolderPath is not { Length: > 0 } path) return;

        var request = new CancellationTokenSource();
        _request = request;
        try
        {
            var icon = await WindowsFolderIcons.GetAsync(path, VisualTreeHelper.GetDpi(this).DpiScaleX <= 1, request.Token);
            if (ReferenceEquals(_request, request) && icon is not null) Source = icon;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_request, request)) _request = null;
            request.Dispose();
        }
    }

    private void CancelRequest()
    {
        var request = _request;
        _request = null;
        request?.Cancel();
    }

    private static DrawingImage CreateFallback()
    {
        var drawing = new GeometryDrawing(Brushes.Goldenrod, null,
            Geometry.Parse("M0,2 H6 L8,4 H16 V14 H0 Z"));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
}
