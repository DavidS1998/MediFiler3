using MediFiler3.Application.Operations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediFiler3.Infrastructure.Operations;

/// <summary>Recovery completes before this registered worker starts; mutations share the maintenance gate.</summary>
public sealed class OperationBackupRetentionService(IOperationBackupMaintenance backups,
    ILogger<OperationBackupRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                var result = await backups.ClearExpiredAsync(stoppingToken).ConfigureAwait(false);
                if (result.RemovedCount > 0) logger.LogInformation("Removed {Count} expired overwrite backups and retired their dependent history groups.", result.RemovedCount);
                if (result.Issues.Count > 0) logger.LogWarning("Expired overwrite backups retained: {Issues}", string.Join("; ", result.Issues));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning(exception, "Overwrite backup expiry failed; cleanup will retry."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
