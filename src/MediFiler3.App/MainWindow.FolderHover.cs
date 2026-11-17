using System.Windows;
using System.Windows.Threading;

namespace MediFiler3;

public partial class MainWindow
{
    // Native sidebar/viewer HWNDs do not deliver WPF MouseMove/Leave consistently.
    private readonly DispatcherTimer _folderHoverTimer = new(DispatcherPriority.Input)
        { Interval = TimeSpan.FromMilliseconds(40) };

    private void InitializeFolderHover()
    {
        _browseView.FolderHoverPositionProbe = GetFolderHoverPosition;
        _folderHoverTimer.Tick += OnFolderHoverTimer;
        _folderHoverTimer.Start();
        Closed += (_, _) => { _folderHoverTimer.Stop(); _browseView.FolderHoverPositionProbe = null; };
    }

    private Point? GetFolderHoverPosition()
    {
        if (!IsVisible || WindowState == WindowState.Minimized || PageContent.Content != _browseView ||
            PresentationSource.FromVisual(_browseView) is null || !GetCursorPos(out var cursor)) return null;
        return _browseView.PointFromScreen(new Point(cursor.X, cursor.Y));
    }

    private void OnFolderHoverTimer(object? sender, EventArgs args)
    {
        if (IsFullscreen) return;
        var point = GetFolderHoverPosition();
        _browseView.UpdateShellFolderHover(point);
        _browseView.UpdateShellCarouselHover(point);
    }
}
