using System.Globalization;
using MediFiler3.Application.Operations;
using MediFiler3.Domain.Operations;
using Microsoft.Data.Sqlite;

namespace MediFiler3.Infrastructure.Persistence;

public sealed class SqliteFileOperationJournal(SqliteDatabase database) : IFileOperationJournal
{
    public async Task AppendPreparedAsync(
        FileOperationRecord operation,
        CancellationToken cancellationToken = default)
    {
        if (operation.Id == Guid.Empty || operation.PayloadVersion < 0)
        {
            throw new ArgumentException("Operation ID and payload version are invalid.", nameof(operation));
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO FileOperations
                    (Id, BatchId, Kind, SourcePath, DestinationPath, CreatedUtc, PayloadVersion, PayloadJson,
                     CompensatesOperationId)
                VALUES ($id, $batch, $kind, $source, $destination, $created, $payloadVersion, $payload, $compensates);
                """;
            insert.Parameters.AddWithValue("$id", operation.Id.ToString("D"));
            insert.Parameters.AddWithValue("$batch", (object?)operation.BatchId?.ToString("D") ?? DBNull.Value);
            insert.Parameters.AddWithValue("$kind", operation.Kind.ToString());
            insert.Parameters.AddWithValue("$source", (object?)operation.SourcePath ?? DBNull.Value);
            insert.Parameters.AddWithValue("$destination", (object?)operation.DestinationPath ?? DBNull.Value);
            insert.Parameters.AddWithValue("$created", operation.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$payloadVersion", operation.PayloadVersion);
            insert.Parameters.AddWithValue("$payload", (object?)operation.PayloadJson ?? DBNull.Value);
            insert.Parameters.AddWithValue("$compensates", (object?)operation.CompensatesOperationId?.ToString("D") ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await InsertEventAsync(connection, (SqliteTransaction)transaction, operation.Id,
            FileOperationEventKind.Prepared, operation.CreatedUtc, null, cancellationToken).ConfigureAwait(false);
        if (operation.CompensatesOperationId is null)
        {
            await InvalidateRedoPathAsync(connection, (SqliteTransaction)transaction, cancellationToken)
                .ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendEventAsync(
        Guid operationId,
        FileOperationEventKind kind,
        string? detailsJson = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var current = await GetLatestEventAsync(connection, (SqliteTransaction)transaction, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            throw new KeyNotFoundException($"Operation {operationId} is not in the journal.");
        }

        if (!CanTransition(current.Kind, kind))
        {
            throw new InvalidOperationException($"Invalid journal transition: {current.Kind} -> {kind}.");
        }

        await InsertEventAsync(connection, (SqliteTransaction)transaction, operationId, kind,
            DateTimeOffset.UtcNow, detailsJson, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<JournaledFileOperation?> GetAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var operation = await ReadOperationAsync(connection, null, operationId, cancellationToken).ConfigureAwait(false);
        return operation is null ? null : operation with
        {
            Events = await ReadEventsAsync(connection, null, operationId, cancellationToken).ConfigureAwait(false)
        };
    }

    public async Task<IReadOnlyList<JournaledFileOperation>> GetBatchAsync(Guid batchId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM FileOperations WHERE BatchId = $batch ORDER BY CreatedUtc, rowid;";
        command.Parameters.AddWithValue("$batch", batchId.ToString("D"));
        var ids = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) ids.Add(Guid.Parse(reader.GetString(0)));
        }

        var operations = new List<JournaledFileOperation>(ids.Count);
        foreach (var id in ids)
        {
            var operation = await GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (operation is not null) operations.Add(operation);
        }
        return operations;
    }

    public async Task<IReadOnlyList<JournaledFileOperation>> GetIncompleteAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.Id
            FROM FileOperations o
            JOIN FileOperationEvents e ON e.OperationId = o.Id
            WHERE e.Sequence = (
                SELECT MAX(last.Sequence) FROM FileOperationEvents last WHERE last.OperationId = o.Id
            )
            AND e.Kind NOT IN ('Completed', 'Failed', 'RecoveryRequired', 'Undone', 'Redone', 'RedoInvalidated')
            ORDER BY o.CreatedUtc;
            """;

        var ids = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        var results = new List<JournaledFileOperation>(ids.Count);
        foreach (var id in ids)
        {
            var entry = await GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (entry is not null)
            {
                results.Add(entry);
            }
        }

        return results;
    }

    public Task<JournaledFileOperation?> GetNextUndoAsync(CancellationToken cancellationToken = default) =>
        GetHistoryCandidateAsync("Completed", "Redone", cancellationToken);

    public Task<JournaledFileOperation?> GetNextRedoAsync(CancellationToken cancellationToken = default) =>
        GetHistoryCandidateAsync("Undone", null, cancellationToken);

    private async Task<JournaledFileOperation?> GetHistoryCandidateAsync(
        string firstState,
        string? secondState,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.Id
            FROM FileOperations o
            JOIN FileOperationEvents e ON e.OperationId = o.Id
            WHERE o.CompensatesOperationId IS NULL
              AND e.Sequence = (SELECT MAX(last.Sequence) FROM FileOperationEvents last WHERE last.OperationId = o.Id)
              AND (e.Kind = $first OR ($second IS NOT NULL AND e.Kind = $second))
            ORDER BY e.Sequence DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$first", firstState);
        command.Parameters.AddWithValue("$second", (object?)secondState ?? DBNull.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string id ? await GetAsync(Guid.Parse(id), cancellationToken).ConfigureAwait(false) : null;
    }

    private static async Task InvalidateRedoPathAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT o.Id FROM FileOperations o
            JOIN FileOperationEvents e ON e.OperationId = o.Id
            WHERE o.CompensatesOperationId IS NULL
              AND e.Sequence = (SELECT MAX(last.Sequence) FROM FileOperationEvents last WHERE last.OperationId = o.Id)
              AND e.Kind = 'Undone';
            """;
        var ids = new List<Guid>();
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) ids.Add(Guid.Parse(reader.GetString(0)));
        }

        foreach (var id in ids)
        {
            await InsertEventAsync(connection, transaction, id, FileOperationEventKind.RedoInvalidated,
                DateTimeOffset.UtcNow, null, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool CanTransition(FileOperationEventKind current, FileOperationEventKind next) =>
        current switch
        {
            FileOperationEventKind.Prepared => next is FileOperationEventKind.CopyStarted or
                FileOperationEventKind.DestinationVerified or FileOperationEventKind.SourceRemoved or
                FileOperationEventKind.Completed or FileOperationEventKind.Failed or
                FileOperationEventKind.RecoveryRequired,
            FileOperationEventKind.CopyStarted => next is FileOperationEventKind.DestinationVerified or
                FileOperationEventKind.Failed or FileOperationEventKind.RecoveryRequired,
            FileOperationEventKind.DestinationVerified => next is FileOperationEventKind.SourceRemoved or
                FileOperationEventKind.Completed or FileOperationEventKind.Failed or
                FileOperationEventKind.RecoveryRequired,
            FileOperationEventKind.SourceRemoved => next is FileOperationEventKind.Completed or
                FileOperationEventKind.RecoveryRequired,
            FileOperationEventKind.Completed => next is FileOperationEventKind.Undone,
            FileOperationEventKind.Undone => next is FileOperationEventKind.Redone or FileOperationEventKind.RedoInvalidated,
            FileOperationEventKind.Redone => next is FileOperationEventKind.Undone,
            _ => false
        };

    private static async Task InsertEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        FileOperationEventKind kind,
        DateTimeOffset occurredUtc,
        string? detailsJson,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO FileOperationEvents (OperationId, Kind, OccurredUtc, DetailsJson)
            VALUES ($id, $kind, $occurred, $details);
            """;
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        command.Parameters.AddWithValue("$kind", kind.ToString());
        command.Parameters.AddWithValue("$occurred", occurredUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$details", (object?)detailsJson ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<FileOperationEvent?> GetLatestEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Sequence, OperationId, Kind, OccurredUtc, DetailsJson
            FROM FileOperationEvents WHERE OperationId = $id ORDER BY Sequence DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadEvent(reader) : null;
    }

    private static async Task<JournaledFileOperation?> ReadOperationAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, BatchId, Kind, SourcePath, DestinationPath, CreatedUtc, PayloadVersion, PayloadJson,
                   CompensatesOperationId
            FROM FileOperations WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var operation = new FileOperationRecord(
            Guid.Parse(reader.GetString(0)), reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            Enum.Parse<FileOperationKind>(reader.GetString(2)), reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : Guid.Parse(reader.GetString(8)));
        return new JournaledFileOperation(operation, []);
    }

    private static async Task<IReadOnlyList<FileOperationEvent>> ReadEventsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Sequence, OperationId, Kind, OccurredUtc, DetailsJson
            FROM FileOperationEvents WHERE OperationId = $id ORDER BY Sequence;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        var result = new List<FileOperationEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadEvent(reader));
        return result;
    }

    private static FileOperationEvent ReadEvent(SqliteDataReader reader) => new(
        reader.GetInt64(0), Guid.Parse(reader.GetString(1)), Enum.Parse<FileOperationEventKind>(reader.GetString(2)),
        DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
        reader.IsDBNull(4) ? null : reader.GetString(4));
}
