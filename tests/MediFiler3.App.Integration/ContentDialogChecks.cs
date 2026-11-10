using System.Reflection;
using MediFiler3.Interop;
using Native = Microsoft.UI.Xaml.Controls;

internal static partial class Program
{
    private static async Task RunContentDialogChecksAsync(WinUiToolbarIsland toolbar, System.Windows.Window owner)
    {
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

    private static async Task DriveContentDialogAsync(string title, Func<Task> request,
        Action<System.Windows.Window, Native.ContentDialog> choose)
    {
        Exception? failure = null;
        var visited = false;
        var started = DateTime.UtcNow;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) =>
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
                choose(window, dialog);
            }
            catch (Exception exception) { failure = exception; timer.Stop(); window.Close(); }
        };
        timer.Start();
        try
        {
            await request();
            if (failure is not null) throw failure;
            Require(visited, "Expected ContentDialog was never shown");
        }
        finally { timer.Stop(); }
    }

    private static void InvokeNativeButton(Native.Button button) =>
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)
            .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
}
