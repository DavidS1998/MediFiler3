using MediFiler3.Infrastructure.Persistence;
using MediFiler3.Infrastructure.Operations;
using Microsoft.Data.Sqlite;

namespace MediFiler3.Infrastructure.Tests;

internal sealed class TemporaryDatabase : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MediFiler3Tests", Guid.NewGuid().ToString("N"));

    public TemporaryDatabase()
    {
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "journal.db");
        Database = new SqliteDatabase(DatabasePath);
        Journal = new SqliteFileOperationJournal(Database);
        Backups = new FileSystemOperationBackupStore(Journal, new WindowsFileIdentityProvider(), Path.Combine(_directory, "app-managed", "UndoBackups"));
    }

    public string DatabasePath { get; }
    public SqliteDatabase Database { get; }
    public SqliteFileOperationJournal Journal { get; }
    public FileSystemOperationBackupStore Backups { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
