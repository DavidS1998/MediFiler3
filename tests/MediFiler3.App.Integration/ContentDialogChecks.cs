using System.Reflection;
using MediFiler3.Application.Operations;
using MediFiler3.Interop;
using Native = Microsoft.UI.Xaml.Controls;

internal static partial class Program
{
    private static async Task RunContentDialogChecksAsync(WinUiToolbarIsland toolbar, System.Windows.Window owner)
    {
        await Check("Backup review virtualizes saved versions and routes location links without deleting", async () =>
        {
            var items = Enumerable.Range(0, 100).Select(index => new OverwriteBackupItem(Guid.NewGuid(),
                $@"C:\Original\file {index}.png", $@"C:\Backups\{index}.png", false, DateTimeOffset.UtcNow,
                index != 1, index == 1 ? "Recovery required" : null)).ToArray();
            Guid? revealed = null;
            var cleared = true;
            await DriveContentDialogAsync("Overwrite backups", async () => cleared = await toolbar.ShowBackupPreviewAsync(items,
                id => { revealed = id; return Task.CompletedTask; }, false), async (_, dialog) =>
            {
                Require(dialog.DefaultButton == Native.ContentDialogButton.Close && dialog.PrimaryButtonText == "", "Read-only backup review offers deletion");
                var body = (Native.StackPanel)dialog.Content;
                var list = body.Children.OfType<Native.ListView>().Single();
                await WaitUntil(() => list.ContainerFromIndex(0) is Native.ListViewItem { ContentTemplateRoot: not null });
                var row = (Native.ListViewItem)list.ContainerFromIndex(0);
                var link = (Native.HyperlinkButton)((Microsoft.UI.Xaml.FrameworkElement)row.ContentTemplateRoot).FindName("BackupLocation");
                Require((Guid)link.Tag == items[0].OperationId, "Backup link targets the wrong saved version");
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(link)
                    .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await WaitUntil(() => revealed == items[0].OperationId);
                Require(Enumerable.Range(0, 100).Count(index => list.ContainerFromIndex(index) is not null) < 100,
                    "Backup preview realizes the entire inventory");
                InvokeNativeButton(NativeDialogButton(dialog, "CloseButton")!);
            });
            Require(!cleared, "Reviewing backup locations authorized deletion");
        });
        foreach (var (title, initial, close) in new[] { ("Rename file", "Sample", false), ("Rename folder", "Folder.name", false), ("New folder", "", false), ("Rename file", "Sample", true) })
            await Check($"ContentDialog {title} {(close ? "Cancel" : "submit")} preserves text prompt behavior", async () =>
            {
                string? result = null;
                await DriveContentDialogAsync(title, async () => result = await toolbar.PromptTextAsync(title, "Enter the new name.", initial, "Save"),
                    (window, dialog) =>
                    {
                        Require(window.Owner == owner, "ContentDialog host lost WPF ownership");
                        var content = (Native.StackPanel)dialog.Content;
                        var input = (Native.TextBox)content.Children[1];
                        Require(input.Text == initial, "Initial text changed");
                        Require(dialog.DefaultButton == Native.ContentDialogButton.Primary, "Enter no longer submits text");
                        input.Text = "  Updated.name  ";
                        InvokeNativeButton(NativeDialogButton(dialog, close ? "CloseButton" : "PrimaryButton")!);
                    });
                Require(result == (close ? null : "Updated.name"), "Text trim/cancellation result changed");
            });
        await Check("ContentDialog outer window excludes rounded corners across layout changes", async () =>
        {
            await DriveContentDialogAsync("Rounded dialog", async () => await toolbar.PromptTextAsync("Rounded dialog", "Corner test"), async (window, dialog) =>
            {
                await WaitUntil(() => dialog.CornerRadius.TopLeft > 0 && HasRoundedDialogRegion(window));
                var width = window.ActualWidth;
                window.Width += 80;
                await WaitUntil(() => window.ActualWidth > width && HasRoundedDialogRegion(window));
                InvokeNativeButton(NativeDialogButton(dialog, "CloseButton")!);
            });
        });
        foreach (var escape in new[] { false, true })
            await Check(escape ? "ContentDialog Escape cancels confirmation" : "ContentDialog host close cancels confirmation", async () =>
            {
                var result = true;
                await DriveContentDialogAsync("Confirm action", async () => result = await toolbar.ConfirmAsync("Confirm action", "Continue?", "Continue"),
                    (window, dialog) =>
                    {
                        Require(dialog.DefaultButton == Native.ContentDialogButton.Close, "Destructive prompt lost default Cancel");
                        if (escape)
                            window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                                System.Windows.PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.Escape)
                                { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
                        else window.Close();
                    });
                Require(!result, "Closing confirmed an operation");
            });
        await Check("Application warning uses ownerless ContentDialog with OK", async () =>
        {
            var host = typeof(WinUiToolbarIsland).Assembly.GetType("MediFiler3.Interop.WinUiContentDialogHost")!;
            Native.ContentDialogResult result = Native.ContentDialogResult.Primary;
            await DriveContentDialogAsync("Application warning", async () => result = await (Task<Native.ContentDialogResult>)host.GetMethod("ShowAsync")!
                .Invoke(null, [null, "Application warning", (Func<Native.ContentDialog>)(() => new Native.ContentDialog
                { Content = "Warning details", CloseButtonText = "OK", DefaultButton = Native.ContentDialogButton.Close }), 480d])!,
                (window, dialog) =>
                {
                    // Keep this probe hidden, just like the owned dialog fixtures.
                    window.Opacity = 0;
                    Require(window.Owner is null && dialog.CloseButtonText == "OK", "Warning semantics changed");
                    InvokeNativeButton(NativeDialogButton(dialog, "CloseButton")!);
                });
            Require(result == Native.ContentDialogResult.None, "Warning return value changed");
        });
    }

    private static bool HasRoundedDialogRegion(System.Windows.Window window)
    {
        var region = CreateDialogProbeRegion(0, 0, 0, 0);
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
            return ReadDialogRegion(handle, region) == 3 && !DialogRegionContains(region, 0, 0) &&
                DialogRegionContains(region, (int)(window.ActualWidth * dpi.DpiScaleX / 2), (int)(window.ActualHeight * dpi.DpiScaleY / 2)) &&
                DialogRegionContains(region, (int)(window.ActualWidth * dpi.DpiScaleX) - 2, (int)(window.ActualHeight * dpi.DpiScaleY / 2));
        }
        finally { DeleteDialogProbeRegion(region); }
    }
    [System.Runtime.InteropServices.DllImport("gdi32.dll", EntryPoint = "CreateRectRgn")] private static extern nint CreateDialogProbeRegion(int left, int top, int right, int bottom);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowRgn")] private static extern int ReadDialogRegion(nint window, nint region);
    [System.Runtime.InteropServices.DllImport("gdi32.dll", EntryPoint = "PtInRegion")] private static extern bool DialogRegionContains(nint region, int x, int y);
    [System.Runtime.InteropServices.DllImport("gdi32.dll", EntryPoint = "DeleteObject")] private static extern bool DeleteDialogProbeRegion(nint region);

    private static Task DriveContentDialogAsync(string title, Func<Task> request,
        Action<System.Windows.Window, Native.ContentDialog> choose) =>
        DriveContentDialogAsync(title, request, (window, dialog) => { choose(window, dialog); return Task.CompletedTask; });

    private static async Task DriveContentDialogAsync(string title, Func<Task> request,
        Func<System.Windows.Window, Native.ContentDialog, Task> choose)
    {
        Exception? failure = null;
        var visited = false;
        var started = DateTime.UtcNow;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += async (_, _) =>
        {
            var window = System.Windows.Application.Current.Windows.Cast<System.Windows.Window>().FirstOrDefault(w => w.Title == title);
            if (window is null) return;
            try
            {
                if (DateTime.UtcNow - started > TimeSpan.FromSeconds(5)) throw new TimeoutException("ContentDialog did not initialize");
                var root = window.Content?.GetType().GetProperty("RootElement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(window.Content) as Native.Grid;
                if (root?.Children.FirstOrDefault() is not Native.ContentDialog dialog || NativeDialogButton(dialog, "CloseButton") is null) return;
                timer.Stop(); visited = true;
                await choose(window, dialog);
            }
            catch (Exception exception) { failure = exception; timer.Stop(); window.Close(); }
        };
        timer.Start();
        try
        {
            await request();
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            Require(visited, "Expected ContentDialog was never shown");
        }
        finally { timer.Stop(); }
    }

    private static void InvokeNativeButton(Native.Button button) =>
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)
            .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
}
