using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using System.IO;

namespace MediFiler3.Infrastructure.Persistence;

/// <summary>
/// Owns the local SQLite database location and initializes its schema when the host starts.
/// Safe to call InitializeAsync repeatedly, including after reopening the application.
/// </summary>
public sealed class SqliteDatabase : IHostedService
{
    private const int CurrentSchemaVersion = 3;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public SqliteDatabase() : this(GetDefaultDatabasePath())
    {
    }

    public SqliteDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public string DatabasePath { get; }

    private static string GetDefaultDatabasePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MediFiler3",
        "medi-filer.db");

    public async Task StartAsync(CancellationToken cancellationToken) =>
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var durability = connection.CreateCommand())
        {
            durability.CommandText = "PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;";
            await durability.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return connection;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (var journalMode = connection.CreateCommand())
            {
                journalMode.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;";
                await journalMode.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }

            var version = await GetSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (version > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Database schema version {version} is newer than supported version {CurrentSchemaVersion}.");
            }

            if (version == 0)
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);
                await CreateJournalSchemaAsync(connection, (SqliteTransaction)transaction, cancellationToken)
                    .ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (version == 1)
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var migrate = connection.CreateCommand();
                migrate.Transaction = (SqliteTransaction)transaction;
                migrate.CommandText = """
                    ALTER TABLE FileOperations RENAME TO FileOperationsV1;
                    """;
                await migrate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await CreateJournalSchemaAsync(connection, (SqliteTransaction)transaction, cancellationToken)
                    .ConfigureAwait(false);
                await using var copyLegacy = connection.CreateCommand();
                copyLegacy.Transaction = (SqliteTransaction)transaction;
                copyLegacy.CommandText = """
                    INSERT INTO FileOperations (Id, BatchId, Kind, SourcePath, DestinationPath, CreatedUtc,
                                                PayloadVersion, PayloadJson)
                    SELECT Id, BatchId, Kind, SourcePath, DestinationPath, CreatedUtc, 0, PayloadJson
                    FROM FileOperationsV1;

                    INSERT INTO FileOperationEvents (OperationId, Kind, OccurredUtc, DetailsJson)
                    SELECT Id, 'Prepared', CreatedUtc, NULL FROM FileOperationsV1;

                    INSERT INTO FileOperationEvents (OperationId, Kind, OccurredUtc, DetailsJson)
                    SELECT Id,
                           Status,
                           COALESCE(FinishedUtc, CreatedUtc),
                           CASE WHEN Error IS NULL THEN NULL ELSE json_object('Message', Error) END
                    FROM FileOperationsV1
                    WHERE Status IN ('Completed', 'Failed');
                    PRAGMA user_version = 3;
                    """;
                await copyLegacy.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (version == 2)
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var migrate = connection.CreateCommand();
                migrate.Transaction = (SqliteTransaction)transaction;
                migrate.CommandText = """
                    ALTER TABLE FileOperations ADD COLUMN CompensatesOperationId TEXT NULL;
                    CREATE INDEX IF NOT EXISTS IX_FileOperations_CompensatesOperationId
                        ON FileOperations (CompensatesOperationId);
                    PRAGMA user_version = 3;
                    """;
                await migrate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static async Task CreateJournalSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var createTable = connection.CreateCommand();
        createTable.Transaction = transaction;
        createTable.CommandText = """
                    CREATE TABLE IF NOT EXISTS FileOperations (
                        Id TEXT NOT NULL PRIMARY KEY,
                        BatchId TEXT NULL,
                        Kind TEXT NOT NULL,
                        SourcePath TEXT NULL,
                        DestinationPath TEXT NULL,
                        CreatedUtc TEXT NOT NULL,
                        PayloadVersion INTEGER NOT NULL,
                        PayloadJson TEXT NULL,
                        CompensatesOperationId TEXT NULL,
                        FOREIGN KEY (CompensatesOperationId) REFERENCES FileOperations(Id)
                    );
                    CREATE TABLE IF NOT EXISTS FileOperationEvents (
                        Sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                        OperationId TEXT NOT NULL,
                        Kind TEXT NOT NULL,
                        OccurredUtc TEXT NOT NULL,
                        DetailsJson TEXT NULL,
                        FOREIGN KEY (OperationId) REFERENCES FileOperations(Id)
                    );
                    CREATE INDEX IF NOT EXISTS IX_FileOperationEvents_Operation_Sequence
                        ON FileOperationEvents (OperationId, Sequence);
                    CREATE INDEX IF NOT EXISTS IX_FileOperations_CompensatesOperationId
                        ON FileOperations (CompensatesOperationId);
                    PRAGMA user_version = 3;
                    """;
        await createTable.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> GetSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
