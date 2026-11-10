using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MediFiler3.Application.Persistence;
using MediFiler3.Infrastructure.Persistence;
using MediFiler3.Interop;
using MediFiler3.Services;

internal static partial class Program
{
    private static async Task RunWindowLayoutChecksAsync()
    {
        var platform = new WindowsWindowPlacement();
        var monitor = platform.GetMonitors().First(m => m.IsPrimary);
        foreach (var maximized in new[] { false, true })
        foreach (var minimized in new[] { false, true })
            await Check($"window layout round trip retains normal bounds and {(maximized ? "maximized" : "normal")} state{(minimized ? " after minimizing" : "")}", async () =>
            {
                var databasePath = Path.Combine(Root, $"layout-{maximized}-{minimized}.db");
                var store = new SqliteApplicationStateStore(new SqliteDatabase(databasePath));
                var saved = WindowPlacementPolicy.Resolve(new(0, 0, 1000, 650, maximized, 300, monitor.DeviceName, 30, 40), [monitor]).ToLayout();
                await store.SaveAsync(saved);
                var service = new WindowLayoutService(store);
                await service.InitializeAsync();
                var window = LayoutWindow();
                using (var controller = new WindowLayoutController(window, service, platform))
                {
                    Exception? error = null;
                    controller.PlacementFailed += exception => error = exception;
                    window.Show();
                    try
                    {
                        await Task.Delay(50);
                        if (error is not null) throw error;
                        Require(window.WindowState == (maximized ? WindowState.Maximized : WindowState.Normal), "Initial window state differs");
                        var actual = platform.Capture(window, maximized, 300);
                        RequireBoundsClose(saved, actual);
                        if (!maximized)
                        {
                            window.Left += 12;
                            window.Top += 8;
                            window.Width += 10;
                            window.Height += 6;
                            await Task.Delay(30);
                        }
                        saved = platform.Capture(window, maximized, 300);
                        if (minimized) window.WindowState = WindowState.Minimized;
                    }
                    finally { window.Close(); }
                    if (error is not null) throw error;
                }
                await service.FlushAsync();
                var restarted = new WindowLayoutService(new SqliteApplicationStateStore(new SqliteDatabase(databasePath)));
                await restarted.InitializeAsync();
                RequireBoundsClose(saved, restarted.Current!);
                Require(restarted.Current!.Maximized == maximized, "Minimized state replaced last normal/maximized state");
                var reopened = LayoutWindow();
                using var restored = new WindowLayoutController(reopened, restarted, platform);
                reopened.Show();
                try
                {
                    await Task.Delay(50);
                    Require(reopened.WindowState == (maximized ? WindowState.Maximized : WindowState.Normal), "Restart restored minimized/wrong state");
                    RequireBoundsClose(saved, platform.Capture(reopened, maximized, 300));
                }
                finally { reopened.Close(); await restarted.FlushAsync(); }
            });

        await Check("first-show maximized layout preserves the normal restore rectangle", async () =>
        {
            var store = new SqliteApplicationStateStore(new SqliteDatabase(Path.Combine(Root, "layout-first-max.db")));
            var saved = WindowPlacementPolicy.Resolve(new(0, 0, 1000, 650, true, 300, monitor.DeviceName, 25, 35), [monitor]).ToLayout();
            await store.SaveAsync(saved);
            var service = new WindowLayoutService(store);
            await service.InitializeAsync();
            var window = LayoutWindow();
            window.ShowActivated = true; // Exercise the ordinary application startup branch, still fully transparent.
            var previousForeground = GetLayoutForegroundWindow();
            using var controller = new WindowLayoutController(window, service, platform);
            try
            {
                window.Show();
                await Task.Delay(50);
                Require(window.WindowState == WindowState.Maximized, "First show did not maximize");
                RequireBoundsClose(saved, platform.Capture(window, true, 300));
                window.WindowState = WindowState.Normal;
                await Task.Delay(30);
                RequireBoundsClose(saved, platform.Capture(window, false, 300));
            }
            finally
            {
                window.Close();
                if (previousForeground != 0) SetLayoutForegroundWindow(previousForeground);
                await service.FlushAsync();
            }
        });

        await Check("display change reconciles window into smaller work area and restores minimum size later", async () =>
        {
            var store = new SqliteApplicationStateStore(new SqliteDatabase(Path.Combine(Root, "layout-display.db")));
            var service = new WindowLayoutService(store);
            await service.InitializeAsync();
            var displays = new ChangedMonitors(platform);
            var window = LayoutWindow();
            using var controller = new WindowLayoutController(window, service, displays);
            Exception? error = null;
            controller.PlacementFailed += exception => error = exception;
            window.Show();
            try
            {
                await Task.Delay(50);
                var current = platform.Capture(window, false, 300);
                var own = platform.GetMonitors().Single(m => m.DeviceName == current.MonitorDeviceName);
                displays.Monitors = [own with { Width = 800 * own.DpiScale, Height = 500 * own.DpiScale }];
                SendLayoutMessage(new WindowInteropHelper(window).Handle, 0x0231, 0, 0);
                SendLayoutMessage(new WindowInteropHelper(window).Handle, 0x007E, 0, 0);
                await Task.Delay(50);
                Require(window.MinWidth == 900 && window.MinHeight == 600, "Display adjustment interrupted a resize gesture");
                SendLayoutMessage(new WindowInteropHelper(window).Handle, 0x0232, 0, 0);
                await WaitUntil(() => error is not null || window.ActualWidth <= 801 && window.ActualHeight <= 501);
                if (error is not null) throw error;
                Require(window.MinWidth == 800 && window.MinHeight == 500, "Minimum dimensions prevent fitting a small display");
                var fitted = platform.Capture(window, false, 300);
                Require(fitted.MonitorOffsetX is >= -1 && fitted.MonitorOffsetY is >= -1, "Fitted window is above/left of work area");
                displays.Monitors = platform.GetMonitors();
                controller.ReconcileDisplays();
                await WaitUntil(() => window.MinWidth == 900 && window.MinHeight == 600);
                Require(window.ActualWidth >= 899 && window.ActualHeight >= 599, "Minimum size was not restored");
            }
            finally { window.Close(); await service.FlushAsync(); }
        });
    }

    private static Window LayoutWindow() => new() { Width = 1280, Height = 800, MinWidth = 900, MinHeight = 600,
        Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
    private static void RequireBoundsClose(WindowLayout expected, WindowLayout actual)
    {
        Require(Math.Abs(expected.Left - actual.Left) <= 2 && Math.Abs(expected.Top - actual.Top) <= 2 &&
            Math.Abs(expected.Width - actual.Width) <= 2 && Math.Abs(expected.Height - actual.Height) <= 2,
            $"Normal bounds differ: expected {expected.Left},{expected.Top},{expected.Width},{expected.Height}; actual {actual.Left},{actual.Top},{actual.Width},{actual.Height}");
    }
    private sealed class ChangedMonitors(WindowsWindowPlacement actual) : IWindowPlacementPlatform
    {
        public IReadOnlyList<MonitorWorkArea> Monitors = actual.GetMonitors();
        public IReadOnlyList<MonitorWorkArea> GetMonitors() => Monitors;
        public WindowLayout Capture(Window window, bool maximized, double folderTreeWidth) => actual.Capture(window, maximized, folderTreeWidth);
        public void Apply(Window window, WindowPlacement placement, bool beforeFirstShow) => actual.Apply(window, placement, beforeFirstShow);
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendLayoutMessage(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint GetLayoutForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")] private static extern bool SetLayoutForegroundWindow(nint window);
}
