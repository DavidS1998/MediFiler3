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
using System.Windows;

namespace MediFiler3;

public partial class App : System.Windows.Application
{
    private IHost? _host;

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
                    services.AddSingleton<MediFiler3.Application.Folders.IFolderTreeService, FileSystemFolderTreeService>();
                    services.AddSingleton<MediFiler3.Application.Files.IFileListingService, FileSystemFileListingService>();
                    services.AddSingleton<MediFiler3.Application.Files.IFileContentService, FileSystemFileContentService>();
                    services.AddSingleton<IThumbnailService, FileSystemThumbnailService>();
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
            Task.Run(() => settings.InitializeAsync()).GetAwaiter().GetResult();
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            _host?.Services.GetService<ILogger<App>>()?.LogCritical(exception, "Application startup failed.");
            MessageBox.Show(
                "MediFiler3 could not start. See the application logs for details.",
                "MediFiler3 startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try { _host.Services.GetRequiredService<ApplicationSettingsService>().FlushAsync().GetAwaiter().GetResult(); }
            catch (Exception exception)
            {
                _host.Services.GetRequiredService<ILogger<App>>().LogError(exception, "Could not save settings at shutdown.");
                MessageBox.Show("Settings could not be saved. Your latest preferences may not survive restart.",
                    "MediFiler3 settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            _host.Services.GetRequiredService<RootFolderHistoryService>().FlushAsync().GetAwaiter().GetResult();
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        WinUiXamlEnvironment.Shutdown();

        base.OnExit(e);
    }
}
