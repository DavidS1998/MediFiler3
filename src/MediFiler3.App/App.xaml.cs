using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MediFiler3.Interop;
using MediFiler3.Infrastructure.Folders;
using MediFiler3.Infrastructure.Files;
using MediFiler3.Application.Files;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Operations;
using MediFiler3.Services;
using MediFiler3.Infrastructure.Persistence;
using MediFiler3.Application.Persistence;
using MediFiler3.Infrastructure.Operations;
using System.IO;
using System.Windows;

namespace MediFiler3;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private bool _settingsFlushedBeforeClose;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            WinUiXamlEnvironment.Initialize();
            _host = Host.CreateDefaultBuilder(e.Args)
                .ConfigureServices(services =>
                {
                    services.AddSingleton<SqliteDatabase>();
                    services.AddSingleton<SqliteApplicationStateStore>();
                    services.AddSingleton<ApplicationSettingsService>();
                    services.AddSingleton<WindowLayoutService>();
                    services.AddSingleton<IWindowPlacementPlatform, WindowsWindowPlacement>();
                    services.AddSingleton<IRootFolderAvailabilityService, FileSystemRootFolderAvailabilityService>();
                    services.AddSingleton<RootFolderHistoryService>();
                    services.AddSingleton<IApplicationSettingsStore>(p => p.GetRequiredService<SqliteApplicationStateStore>());
                    services.AddSingleton<IWindowLayoutStore>(p => p.GetRequiredService<SqliteApplicationStateStore>());
                    services.AddSingleton<IRootFolderHistoryStore>(p => p.GetRequiredService<SqliteApplicationStateStore>());
                    services.AddSingleton<IThumbnailCacheIndexStore>(p => p.GetRequiredService<SqliteApplicationStateStore>());
                    services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<SqliteDatabase>());
                    services.AddSingleton<IFileOperationJournal, SqliteFileOperationJournal>();
                    services.AddSingleton<IRecycleBinRestoreService, WindowsRecycleBinRestoreService>();
                    services.AddSingleton<IFileOrganizationService, FileSystemFileOrganizationService>();
                    services.AddSingleton<IFileIdentityProvider, WindowsFileIdentityProvider>();
                    services.AddSingleton<FileSystemOperationBackupStore>();
                    services.AddSingleton<IFileOperationBackupStore>(p => p.GetRequiredService<FileSystemOperationBackupStore>());
                    services.AddSingleton<IOperationBackupMaintenance>(p => p.GetRequiredService<FileSystemOperationBackupStore>());
                    services.AddSingleton<IFileOperationRecoveryInspector, FileSystemOperationRecoveryInspector>();
                    services.AddSingleton<OperationJournalRecoveryService>();
                    services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<OperationJournalRecoveryService>());
                    services.AddHostedService<OperationBackupRetentionService>();
                    services.AddSingleton<MediFiler3.Application.Folders.IFolderTreeService, FileSystemFolderTreeService>();
                    services.AddSingleton<MediFiler3.Application.Files.IFileListingService, FileSystemFileListingService>();
                    services.AddSingleton<MediFiler3.Application.Files.IFileContentService, FileSystemFileContentService>();
                    services.AddSingleton<IFolderChangeMonitor, FileSystemFolderChangeMonitor>();
                    services.AddSingleton<FileSystemThumbnailService>();
                    services.AddSingleton<IReaderImageService, FileSystemReaderImageService>();
                    services.AddSingleton<IThumbnailService>(p => p.GetRequiredService<FileSystemThumbnailService>());
                    services.AddSingleton<IFileCollisionPreviewService, FileSystemCollisionPreviewService>();
                    services.AddSingleton<ITextContentService, FileSystemTextContentService>();
                    services.AddSingleton<IWindowsFileLaunchAdapter, WindowsFileLaunchAdapter>();
                    services.AddSingleton<IExternalFileService, WindowsExternalFileService>();
                    services.AddSingleton<IDefaultApplicationLookup, WindowsDefaultApplicationLookup>();
                    services.AddSingleton<IDefaultApplicationService, WindowsDefaultApplicationService>();
                    services.AddSingleton<WicImageDecoder>();
                    services.AddSingleton<NavigationSessionState>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            _host.Start();
            var settings = _host.Services.GetRequiredService<ApplicationSettingsService>();
            settings.PersistenceFailed += exception => _host.Services.GetRequiredService<ILogger<App>>()
                .LogWarning(exception, "Could not load/save settings; defaults or current session values remain available.");
            var layout = _host.Services.GetRequiredService<WindowLayoutService>();
            layout.PersistenceFailed += exception => _host.Services.GetRequiredService<ILogger<App>>()
                .LogWarning(exception, "Could not load/save window layout; safe placement or current session values remain available.");
            Task.Run(() => Task.WhenAll(settings.InitializeAsync(), layout.InitializeAsync())).GetAwaiter().GetResult();
            _host.Services.GetRequiredService<FileSystemThumbnailService>().MaintenanceFailed += exception =>
                _host.Services.GetRequiredService<ILogger<App>>().LogWarning(exception, "Thumbnail maintenance failed; cleanup will retry.");
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Closing += (_, _) =>
            {
                FlushSettingsWithWarning();
                _settingsFlushedBeforeClose = true;
            };
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            var logPath = WriteStartupFailureLog(exception);
            _host?.Services.GetService<ILogger<App>>()?.LogCritical(exception, "Application startup failed.");
            ShowApplicationMessage(
                logPath is null ? $"MediFiler3 could not start.\n\n{exception.Message}" :
                    $"MediFiler3 could not start. Details were saved to:\n{logPath}",
                "MediFiler3 startup error",
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static string? WriteStartupFailureLog(Exception exception)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediFiler3");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "startup-error.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            return path;
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            if (!_settingsFlushedBeforeClose) FlushSettingsWithWarning();
            _host.Services.GetRequiredService<RootFolderHistoryService>().FlushAsync().GetAwaiter().GetResult();
            try { _host.Services.GetRequiredService<FileSystemThumbnailService>().FlushAsync().GetAwaiter().GetResult(); }
            catch (Exception exception) { _host.Services.GetRequiredService<ILogger<App>>().LogWarning(exception, "Could not flush thumbnail index."); }
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        WinUiXamlEnvironment.Shutdown();

        base.OnExit(e);
    }

    private void FlushSettingsWithWarning()
    {
        if (_host is null) return;
        try { _host.Services.GetRequiredService<ApplicationSettingsService>().FlushAsync().GetAwaiter().GetResult(); }
        catch (Exception exception)
        {
            _host.Services.GetRequiredService<ILogger<App>>().LogError(exception, "Could not save settings at shutdown.");
            ShowApplicationMessage("Settings could not be saved. Your latest preferences may not survive restart.",
                "MediFiler3 settings", MessageBoxImage.Warning);
        }
        try { _host.Services.GetRequiredService<WindowLayoutService>().FlushAsync().GetAwaiter().GetResult(); }
        catch (Exception exception)
        {
            _host.Services.GetRequiredService<ILogger<App>>().LogError(exception, "Could not save window layout at shutdown.");
            ShowApplicationMessage("Window layout could not be saved. Its latest size and position may not survive restart.",
                "MediFiler3 window layout", MessageBoxImage.Warning);
        }
    }

    private void ShowApplicationMessage(string message, string title, MessageBoxImage emergencyIcon)
    {
        if (WinUiXamlEnvironment.IsInitialized)
        {
            try
            {
                WinUiContentDialogHost.ShowAsync(MainWindow?.IsVisible == true ? MainWindow : null, title,
                    () => new Microsoft.UI.Xaml.Controls.ContentDialog
                    {
                        Content = new Microsoft.UI.Xaml.Controls.TextBlock { Text = message, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
                        CloseButtonText = "OK", DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close
                    }).GetAwaiter().GetResult();
                return;
            }
            catch (Exception exception)
            {
                _host?.Services.GetService<ILogger<App>>()?.LogWarning(exception, "WinUI could not display an application warning.");
            }
        }
        // Startup failure can itself mean WinUI is unavailable; retain an emergency way to report it.
        MessageBox.Show(message, title, MessageBoxButton.OK, emergencyIcon);
    }
}
