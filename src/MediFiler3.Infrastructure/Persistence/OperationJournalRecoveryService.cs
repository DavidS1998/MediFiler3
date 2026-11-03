using System.Text.Json;
using MediFiler3.Application.Operations;
using MediFiler3.Domain.Operations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediFiler3.Infrastructure.Persistence;

public sealed class OperationJournalRecoveryService(
    IFileOperationJournal journal,
    IFileOperationRecoveryInspector inspector,
    ILogger<OperationJournalRecoveryService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in await journal.GetIncompleteAsync(cancellationToken).ConfigureAwait(false))
        {
            var assessment = await inspector.AssessAsync(entry, cancellationToken).ConfigureAwait(false);
            var (eventKind, logLevel) = assessment.Conclusion switch
            {
                RecoveryConclusion.Completed => (FileOperationEventKind.Completed, LogLevel.Information),
                RecoveryConclusion.Failed => (FileOperationEventKind.Failed, LogLevel.Information),
                _ => (FileOperationEventKind.RecoveryRequired, LogLevel.Warning)
            };
            var details = JsonSerializer.Serialize(new OperationEventDetails(
                assessment.Explanation, PayloadJson: assessment.EvidenceJson));
            await journal.AppendEventAsync(entry.Operation.Id, eventKind, details, cancellationToken)
                .ConfigureAwait(false);
            logger.Log(logLevel, "Recovered journal operation {OperationId} as {Outcome}: {Explanation}",
                entry.Operation.Id, eventKind, assessment.Explanation);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
