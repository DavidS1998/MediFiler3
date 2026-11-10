using Microsoft.UI.Xaml.Controls;

namespace MediFiler3.Interop;

/// <summary>Supplies a full-size XamlRoot above WPF/native media and modal WPF ownership.</summary>
internal static class WinUiContentDialogHost
{
    public static Task<ContentDialogResult> ShowAsync(System.Windows.Window? owner, string title,
        Func<ContentDialog> createDialog, double width = 480)
    {
        if (owner is not null) return ShowCore(owner, title, createDialog, width);
        var application = System.Windows.Application.Current;
        var shutdownMode = application.ShutdownMode;
        application.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        try { return ShowCore(null, title, createDialog, width); }
        finally { application.ShutdownMode = shutdownMode; }
    }

    private static Task<ContentDialogResult> ShowCore(System.Windows.Window? owner, string title,
        Func<ContentDialog> createDialog, double width)
    {
        var presentationOwner = owner ?? System.Windows.Application.Current.MainWindow;
        var window = new System.Windows.Window
        {
            Title = title,
            Width = width,
            Height = Math.Min(640, System.Windows.SystemParameters.WorkArea.Height),
            MaxHeight = System.Windows.SystemParameters.WorkArea.Height,
            WindowStyle = System.Windows.WindowStyle.None,
            ResizeMode = System.Windows.ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? System.Windows.WindowStartupLocation.CenterScreen : System.Windows.WindowStartupLocation.CenterOwner,
            Owner = owner,
            Opacity = presentationOwner?.Opacity ?? 1,
            ShowActivated = presentationOwner?.ShowActivated ?? true,
            ShowInTaskbar = false,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 32))
        };
        var host = new WinUiDialogIsland();
        window.Content = host;
        ContentDialog? dialog = null;
        var completion = new TaskCompletionSource<ContentDialogResult>();
        var finished = false;
        window.Closing += (_, args) =>
        {
            if (finished || dialog is null) return;
            args.Cancel = true;
            dialog.Hide();
        };
        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Escape) return;
            args.Handled = true;
            dialog?.Hide();
        };
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                dialog = createDialog();
                dialog.Title = title;
                dialog.XamlRoot = host.XamlRoot;
                dialog.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
                dialog.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top;
                dialog.Resources["ContentDialogMaxWidth"] = width;
                dialog.Resources["ContentDialogMinWidth"] = width;
                dialog.SizeChanged += (_, args) =>
                {
                    if (args.NewSize.Height > 0) window.Height = Math.Ceiling(args.NewSize.Height);
                };
                host.RootElement!.Children.Add(dialog);
                completion.TrySetResult(await dialog.ShowAsync(ContentDialogPlacement.InPlace));
            }
            catch (Exception exception) { completion.TrySetException(exception); }
            finally
            {
                finished = true;
                _ = window.Dispatcher.BeginInvoke(new Action(window.Close));
            }
        };
        window.ShowDialog();
        return completion.Task;
    }
}
