using MediFiler3.Application.Operations;
using Microsoft.Extensions.Logging;

namespace MediFiler3;

public partial class MainWindow
{
    private CancellationTokenSource? _backupReviewCancellation;

    private void InitializeBackupReview(IOperationBackupMaintenance maintenance)
    {
        _settingsView.ReviewBackupsRequested = () => ReviewBackupsAsync(maintenance, false);
        _settingsView.ClearBackupsRequested = () => ReviewBackupsAsync(maintenance, true);
        void Changed()
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(() => { if (!_titleBarClosed) _ = UpdateHistoryAvailabilityAsync(); }));
        }
        maintenance.Changed += Changed;
        _settingsView.Unloaded += (_, _) => _backupReviewCancellation?.Cancel();
        Closed += (_, _) => { maintenance.Changed -= Changed; _backupReviewCancellation?.Cancel(); };
    }

    private async Task ReviewBackupsAsync(IOperationBackupMaintenance maintenance, bool clear)
    {
        if (_backupReviewCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _backupReviewCancellation = cancellation;
        try
        {
            _settingsView.SetBackupCleanupStatus("Checking stored overwrite backups…");
            var items = await maintenance.GetBackupsAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (PageContent.Content != _settingsView) return;
            var confirmed = await ToolbarIsland.ShowBackupPreviewAsync(items,
                id => maintenance.RevealInExplorerAsync(id, cancellation.Token), clear);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!confirmed)
            {
                _settingsView.SetBackupCleanupStatus($"{items.Count} stored backups; {items.Count(item => !item.CanRemove)} retained from cleanup.");
                return;
            }
            _settingsView.SetBackupCleanupStatus("Clearing reviewed overwrite backups…");
            // Only the reviewed, eligible IDs are authorized; new backups are outside this confirmation.
            var result = await maintenance.ClearAsync(items.Where(item => item.CanRemove).Select(item => item.OperationId).ToArray(), cancellation.Token);
            _settingsView.SetBackupCleanupStatus($"Cleared {result.RemovedCount} backups." +
                (result.Issues.Count == 0 ? "" : $" {result.Issues.Count} could not be cleared.\n" + string.Join("\n", result.Issues.Take(3))));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not review/clear overwrite backups.");
            _settingsView.SetBackupCleanupStatus($"Could not finish backup review: {exception.Message}");
        }
        finally { _backupReviewCancellation = null; }
    }
}
