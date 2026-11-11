using System.Windows;
using System.Windows.Threading;

namespace MediFiler3;

public partial class MainWindow
{
    // Native sidebar HWNDs do not deliver WPF MouseMove/Leave consistently.
    private readonly DispatcherTimer _folderHoverTimer = new(DispatcherPriority.Input)
        { Interval = TimeSpan.FromMilliseconds(40) };

    private void InitializeFolderHover()
    {
        _browseView.SidebarHoverProbe = IsPointerOverSidebar;
        _folderHoverTimer.Tick += OnFolderHoverTimer;
        _folderHoverTimer.Start();
        Closed += (_, _) => { _folderHoverTimer.Stop(); _browseView.SidebarHoverProbe = null; };
    }

    private bool IsPointerOverSidebar()
    {
        if (!IsVisible || !IsSidebarVisible || Sidebar.Visibility != Visibility.Visible || !GetCursorPos(out var cursor)) return false;
        var point = Sidebar.PointFromScreen(new Point(cursor.X, cursor.Y));
        return point.X >= 0 && point.X < Sidebar.ActualWidth && point.Y >= 0 && point.Y < Sidebar.ActualHeight;
    }

    private void OnFolderHoverTimer(object? sender, EventArgs args)
    {
        if (IsFullscreen || !IsVisible || WindowState == WindowState.Minimized || PageContent.Content != _browseView || !GetCursorPos(out var cursor)) return;
        _browseView.UpdateShellFolderHover(IsPointerOverSidebar(), _browseView.PointFromScreen(new Point(cursor.X, cursor.Y)));
    }
}
