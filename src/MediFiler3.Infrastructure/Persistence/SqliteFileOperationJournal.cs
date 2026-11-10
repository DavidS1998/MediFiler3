using System.Globalization;
using MediFiler3.Application.Operations;
using MediFiler3.Domain.Operations;
using Microsoft.Data.Sqlite;

namespace MediFiler3.Infrastructure.Persistence;

public sealed class SqliteFileOperationJournal(SqliteDatabase database, string? rootScope = null) : IFileOperationJournal
{
    public async Task<IReadOnlyList<JournaledFileOperation>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM FileOperations WHERE $root IS NULL OR RootScope IS $root ORDER BY rowid;";
        command.Parameters.AddWithValue("$root", (object?)rootScope ?? DBNull.Value);
        var ids = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) ids.Add(Guid.Parse(reader.GetString(0)));
        var result = new List<JournaledFileOperation>();
        foreach (var id in ids)
            if (await GetAsync(id, cancellationToken).ConfigureAwait(false) is { } entry) result.Add(entry);
        return result;
    }

    public async Task DiscardHistoryAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var entry = await ReadOperationAsync(connection, transaction, operationId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Backup operation was not found.");
        if (rootScope is not null && entry.Operation.RootScope != rootScope) throw new InvalidOperationException("Operation belongs to another root.");
        while (entry.Operation.CompensatesOperationId is { } parent)
            entry = await ReadOperationAsync(connection, transaction, parent, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Backup ownership is incomplete.");
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            WITH RECURSIVE Owned(Id) AS (
                SELECT Id FROM FileOperations
                WHERE CompensatesOperationId IS NULL AND RootScope IS $root
                  AND (Id = $id OR ($batch IS NOT NULL AND BatchId = $batch))
                UNION
                SELECT child.Id FROM FileOperations child JOIN Owned parent ON child.CompensatesOperationId = parent.Id
                WHERE child.RootScope IS $root
            )
            SELECT o.Id, o.CompensatesOperationId IS NULL FROM FileOperations o JOIN Owned ON o.Id = Owned.Id;
            """;
        query.Parameters.AddWithValue("$root", (object?)entry.Operation.RootScope ?? DBNull.Value);
        query.Parameters.AddWithValue("$id", entry.Operation.Id.ToString("D"));
        query.Parameters.AddWithValue("$batch", (object?)entry.Operation.BatchId?.ToString("D") ?? DBNull.Value);
        var ids = new List<(Guid Id, bool Original)>();
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) ids.Add((Guid.Parse(reader.GetString(0)), reader.GetBoolean(1)));
        foreach (var (id, original) in ids)
        {
            var current = await GetLatestEventAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false);
            if (current?.Kind is not (FileOperationEventKind.Completed or FileOperationEventKind.Redone or
                FileOperationEventKind.Undone or FileOperationEventKind.RedoInvalidated or FileOperationEventKind.HistoryDiscarded or FileOperationEventKind.Failed))
                throw new InvalidOperationException("The history group has an unresolved operation; backups were retained for recovery.");
            if (original)
                await InsertEventAsync(connection, transaction, id, FileOperationEventKind.HistoryDiscarded, DateTimeOffset.UtcNow,
                    "{\"Message\":\"Overwrite backups cleared from Settings; this history group is retired.\"}", cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public IFileOperationJournal ForRoot(string rootIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootIdentity);
        return new SqliteFileOperationJournal(database, rootIdentity);
    }

    public async Task AppendPreparedAsync(
        FileOperationRecord operation,
        CancellationToken cancellationToken = default)
    {
        if (rootScope is not null)
        {
            if (operation.RootScope is not null && operation.RootScope != rootScope)
                throw new InvalidOperationException("Operation belongs to another root.");
            operation = operation with { RootScope = rootScope };
        }
        if (operation.Id == Guid.Empty || operation.PayloadVersion < 0)
        {
            throw new ArgumentException("Operation ID and payload version are invalid.", nameof(operation));
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (operation.CompensatesOperationId is { } originalId)
        {
            var original = await ReadOperationAsync(connection, (SqliteTransaction)transaction, originalId, cancellationToken).ConfigureAwait(false);
            if (original is null || original.Operation.RootScope != operation.RootScope)
                throw new InvalidOperationException("Compensation must belong to the original root.");
        }
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO FileOperations
                    (Id, BatchId, Kind, SourcePath, DestinationPath, CreatedUtc, PayloadVersion, PayloadJson,
                     CompensatesOperationId, RootScope)
                VALUES ($id, $batch, $kind, $source, $destination, $created, $payloadVersion, $payload, $compensates, $root);
                """;
            insert.Parameters.AddWithValue("$root", (object?)operation.RootScope ?? DBNull.Value);
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
            await InvalidateRedoPathAsync(connection, (SqliteTransaction)transaction, operation.RootScope, cancellationToken)
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

        if (rootScope is not null)
        {
            var record = await ReadOperationAsync(connection, (SqliteTransaction)transaction, operationId, cancellationToken).ConfigureAwait(false);
            if (record is null || record.Operation.RootScope != rootScope)
                throw new InvalidOperationException("Operation belongs to another root.");
        }
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
        return operation is null || (rootScope is not null && operation.Operation.RootScope != rootScope) ? null : operation with
        {
            Events = await ReadEventsAsync(connection, null, operationId, cancellationToken).ConfigureAwait(false)
        };
    }

    public async Task<IReadOnlyList<JournaledFileOperation>> GetBatchAsync(Guid batchId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM FileOperations WHERE BatchId = $batch AND RootScope IS $root ORDER BY CreatedUtc, rowid;";
        command.Parameters.AddWithValue("$root", (object?)rootScope ?? DBNull.Value);
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
            AND e.Kind NOT IN ('Completed', 'Failed', 'RecoveryRequired', 'Undone', 'Redone', 'RedoInvalidated', 'HistoryDiscarded')
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
            WHERE o.CompensatesOperationId IS NULL AND o.RootScope IS $root
              AND e.Sequence = (SELECT MAX(last.Sequence) FROM FileOperationEvents last WHERE last.OperationId = o.Id)
              AND (e.Kind = $first OR ($second IS NOT NULL AND e.Kind = $second))
            ORDER BY e.Sequence DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$root", (object?)rootScope ?? DBNull.Value);
        command.Parameters.AddWithValue("$first", firstState);
        command.Parameters.AddWithValue("$second", (object?)secondState ?? DBNull.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string id ? await GetAsync(Guid.Parse(id), cancellationToken).ConfigureAwait(false) : null;
    }

    private static async Task InvalidateRedoPathAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string? scope,
        CancellationToken cancellationToken)
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT o.Id FROM FileOperations o
            JOIN FileOperationEvents e ON e.OperationId = o.Id
            WHERE o.CompensatesOperationId IS NULL AND o.RootScope IS $root
              AND e.Sequence = (SELECT MAX(last.Sequence) FROM FileOperationEvents last WHERE last.OperationId = o.Id)
              AND e.Kind = 'Undone';
            """;
        query.Parameters.AddWithValue("$root", (object?)scope ?? DBNull.Value);
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
            FileOperationEventKind.Completed => next is FileOperationEventKind.Undone or FileOperationEventKind.RecoveryRequired,
            FileOperationEventKind.Undone => next is FileOperationEventKind.Redone or FileOperationEventKind.RedoInvalidated or FileOperationEventKind.RecoveryRequired,
            FileOperationEventKind.Redone => next is FileOperationEventKind.Undone or FileOperationEventKind.RecoveryRequired,
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
                   CompensatesOperationId, RootScope
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
            reader.IsDBNull(8) ? null : Guid.Parse(reader.GetString(8)), reader.IsDBNull(9) ? null : reader.GetString(9));
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
