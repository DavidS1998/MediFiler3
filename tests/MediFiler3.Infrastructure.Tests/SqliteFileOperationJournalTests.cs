using MediFiler3.Domain.Operations;
using MediFiler3.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace MediFiler3.Infrastructure.Tests;

public sealed class SqliteFileOperationJournalTests
{
    [Fact]
    public async Task InitializesAndReopensDatabaseWithOperationHistory()
    {
        using var temp = new TemporaryDatabase();
        var operation = NewOperation();

        await temp.Journal.AppendPreparedAsync(operation);
        await temp.Journal.AppendEventAsync(operation.Id, FileOperationEventKind.Completed, "{} ");

        var reopened = new SqliteFileOperationJournal(new SqliteDatabase(temp.DatabasePath));
        var history = await reopened.GetAsync(operation.Id);

        Assert.NotNull(history);
        Assert.Equal(FileOperationEventKind.Completed, history.State);
        Assert.Collection(history.Events,
            item => Assert.Equal(FileOperationEventKind.Prepared, item.Kind),
            item => Assert.Equal(FileOperationEventKind.Completed, item.Kind));
        Assert.True(File.Exists(temp.DatabasePath));
    }

    [Fact]
    public async Task MigratesVersionOneRowsAndLeavesInterruptedIntentRecoverable()
    {
        using var temp = new TemporaryDatabase();
        var id = Guid.NewGuid();
        await using (var connection = new SqliteConnection($"Data Source={temp.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE FileOperations (
                    Id TEXT NOT NULL PRIMARY KEY, BatchId TEXT NULL, Kind TEXT NOT NULL, Status TEXT NOT NULL,
                    SourcePath TEXT NOT NULL, DestinationPath TEXT NULL, CreatedUtc TEXT NOT NULL,
                    FinishedUtc TEXT NULL, Error TEXT NULL, PayloadJson TEXT NULL);
                CREATE INDEX IX_FileOperations_Status_CreatedUtc ON FileOperations(Status, CreatedUtc);
                INSERT INTO FileOperations VALUES
                    ($id, NULL, 'Move', 'Interrupted', 'C:\source.txt', 'C:\target.txt',
                     '2026-01-02T03:04:05.0000000+00:00', NULL, 'crash', NULL);
                PRAGMA user_version = 1;
                """;
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        await temp.Database.InitializeAsync();
        var history = await temp.Journal.GetAsync(id);
        var incomplete = await temp.Journal.GetIncompleteAsync();

        Assert.NotNull(history);
        Assert.Equal("C:\\source.txt", history.Operation.SourcePath);
        Assert.Single(history.Events);
        Assert.Equal(FileOperationEventKind.Prepared, history.State);
        Assert.Single(incomplete);

        await using var verify = await temp.Database.OpenConnectionAsync();
        await using var versionCommand = verify.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        Assert.Equal(4L, (long)(await versionCommand.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task MigratesVersionTwoSchemaAndAddsCompensationLinkColumn()
    {
        using var temp = new TemporaryDatabase();
        var id = Guid.NewGuid();
        await using (var connection = new SqliteConnection($"Data Source={temp.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE FileOperations (
                    Id TEXT NOT NULL PRIMARY KEY, BatchId TEXT NULL, Kind TEXT NOT NULL,
                    SourcePath TEXT NULL, DestinationPath TEXT NULL, CreatedUtc TEXT NOT NULL,
                    PayloadVersion INTEGER NOT NULL, PayloadJson TEXT NULL);
                CREATE TABLE FileOperationEvents (
                    Sequence INTEGER PRIMARY KEY AUTOINCREMENT, OperationId TEXT NOT NULL,
                    Kind TEXT NOT NULL, OccurredUtc TEXT NOT NULL, DetailsJson TEXT NULL);
                INSERT INTO FileOperations VALUES
                    ($id, NULL, 'CreateFolder', NULL, 'C:\\new', '2026-01-02T03:04:05.0000000+00:00', 1, '{}');
                INSERT INTO FileOperationEvents (OperationId, Kind, OccurredUtc)
                    VALUES ($id, 'Prepared', '2026-01-02T03:04:05.0000000+00:00');
                PRAGMA user_version = 2;
                """;
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        await temp.Database.InitializeAsync();
        var migrated = await temp.Journal.GetAsync(id);

        Assert.NotNull(migrated);
        Assert.Null(migrated.Operation.CompensatesOperationId);
        Assert.Equal(FileOperationEventKind.Prepared, migrated.State);
        await using var verify = await temp.Database.OpenConnectionAsync();
        await using var columns = verify.CreateCommand();
        columns.CommandText = "PRAGMA table_info(FileOperations);";
        await using var reader = await columns.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync()) names.Add(reader.GetString(1));
        Assert.Contains("CompensatesOperationId", names);
    }

    [Fact]
    public async Task RejectsInvalidTransitionsAndKeepsEventsAppendOnly()
    {
        using var temp = new TemporaryDatabase();
        var operation = NewOperation();
        await temp.Journal.AppendPreparedAsync(operation);
        await temp.Journal.AppendEventAsync(operation.Id, FileOperationEventKind.Completed);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            temp.Journal.AppendEventAsync(operation.Id, FileOperationEventKind.Failed));

        var history = await temp.Journal.GetAsync(operation.Id);
        Assert.NotNull(history);
        Assert.Equal(2, history.Events.Count);
        Assert.Equal(FileOperationEventKind.Completed, history.State);
    }

    [Fact]
    public async Task NewUserOperationInvalidatesRedoButCompensationDoesNot()
    {
        using var temp = new TemporaryDatabase();
        var original = NewOperation();
        await temp.Journal.AppendPreparedAsync(original);
        await temp.Journal.AppendEventAsync(original.Id, FileOperationEventKind.Completed);
        await temp.Journal.AppendEventAsync(original.Id, FileOperationEventKind.Undone);
        Assert.Equal(original.Id, (await temp.Journal.GetNextRedoAsync())?.Operation.Id);

        var compensation = NewOperation(compensates: original.Id);
        await temp.Journal.AppendPreparedAsync(compensation);
        Assert.Equal(original.Id, (await temp.Journal.GetNextRedoAsync())?.Operation.Id);

        var newUserOperation = NewOperation();
        await temp.Journal.AppendPreparedAsync(newUserOperation);

        Assert.Null(await temp.Journal.GetNextRedoAsync());
        Assert.Equal(FileOperationEventKind.RedoInvalidated,
            (await temp.Journal.GetAsync(original.Id))!.State);
    }

    [Fact]
    public async Task ExposesMostRecentAppliedOperationForUndoAndExcludesIncompleteAndCompensationRows()
    {
        using var temp = new TemporaryDatabase();
        var first = NewOperation();
        await temp.Journal.AppendPreparedAsync(first);
        await temp.Journal.AppendEventAsync(first.Id, FileOperationEventKind.Completed);
        var second = NewOperation();
        await temp.Journal.AppendPreparedAsync(second);
        await temp.Journal.AppendEventAsync(second.Id, FileOperationEventKind.Completed);
        var compensation = NewOperation(compensates: second.Id);
        await temp.Journal.AppendPreparedAsync(compensation);
        await temp.Journal.AppendEventAsync(compensation.Id, FileOperationEventKind.Completed);
        var pending = NewOperation();
        await temp.Journal.AppendPreparedAsync(pending);

        Assert.Equal(second.Id, (await temp.Journal.GetNextUndoAsync())?.Operation.Id);
        Assert.Contains(await temp.Journal.GetIncompleteAsync(), x => x.Operation.Id == pending.Id);
    }

    private static FileOperationRecord NewOperation(Guid? compensates = null) => new(
        Guid.NewGuid(), null, FileOperationKind.Move, "source", "destination", DateTimeOffset.UtcNow,
        1, "{}", compensates);
}
