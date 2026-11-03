using System.Text.Json;
using MediFiler3.Domain.Operations;
using MediFiler3.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediFiler3.Infrastructure.Tests;

public sealed class OperationJournalRecoveryServiceTests
{
    [Theory]
    [InlineData(RecoveryConclusion.Completed, FileOperationEventKind.Completed)]
    [InlineData(RecoveryConclusion.Failed, FileOperationEventKind.Failed)]
    public async Task PersistsConclusiveRecoveryOutcomes(
        RecoveryConclusion conclusion,
        FileOperationEventKind expectedEvent)
    {
        using var temp = new TemporaryDatabase();
        var operation = new FileOperationRecord(Guid.NewGuid(), null, FileOperationKind.Move,
            "source", "destination", DateTimeOffset.UtcNow, 1, "{}");
        await temp.Journal.AppendPreparedAsync(operation);
        var inspector = new FixedRecoveryInspector(new OperationRecoveryAssessment(conclusion, "Verified."));
        var service = new OperationJournalRecoveryService(temp.Journal, inspector,
            NullLogger<OperationJournalRecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(expectedEvent, (await temp.Journal.GetAsync(operation.Id))!.State);
        Assert.Empty(await temp.Journal.GetIncompleteAsync());
    }

    [Fact]
    public async Task PersistsAmbiguousRecoveryAndDoesNotRetryItOnNextStartup()
    {
        using var temp = new TemporaryDatabase();
        var operation = new FileOperationRecord(Guid.NewGuid(), null, FileOperationKind.SendToRecycleBin,
            "source", null, DateTimeOffset.UtcNow, 1, JsonSerializer.Serialize(new RecycleBinPayload()));
        await temp.Journal.AppendPreparedAsync(operation);
        var inspector = new FixedRecoveryInspector(new OperationRecoveryAssessment(
            RecoveryConclusion.Ambiguous, "Restore outcome cannot be verified."));
        var service = new OperationJournalRecoveryService(temp.Journal, inspector,
            NullLogger<OperationJournalRecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);

        var history = await temp.Journal.GetAsync(operation.Id);
        Assert.NotNull(history);
        Assert.Equal(FileOperationEventKind.RecoveryRequired, history.State);
        Assert.Equal(1, inspector.CallCount);
        Assert.Empty(await temp.Journal.GetIncompleteAsync());
    }

    private sealed class FixedRecoveryInspector(OperationRecoveryAssessment assessment)
        : MediFiler3.Application.Operations.IFileOperationRecoveryInspector
    {
        public int CallCount { get; private set; }

        public Task<OperationRecoveryAssessment> AssessAsync(
            JournaledFileOperation operation,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(assessment);
        }
    }
}
