using System.IO;
using System.Text.Json;
using MediFiler3.Application.Persistence;

namespace MediFiler3.Infrastructure.Persistence;

/// <summary>
/// Typed state storage, separate from the append-only operation journal. Payload versions
/// allow individual record formats to evolve independently of the database schema.
/// No filesystem availability checks, source mutations, or cache deletion happen here.
/// </summary>
public sealed class SqliteApplicationStateStore(SqliteDatabase database) :
    IApplicationSettingsStore, IWindowLayoutStore, IRootFolderHistoryStore, IThumbnailCacheIndexStore
{
    async Task<ApplicationSettings> IApplicationSettingsStore.LoadAsync(CancellationToken cancellationToken)
    {
        var values = await ReadAsync<ApplicationSettings>("ApplicationSettings", cancellationToken).ConfigureAwait(false);
        var settings = (values.SingleOrDefault() ?? new ApplicationSettings()).NormalizeGalleryDimensions();
        settings.Validate();
        return settings;
    }

    public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        return WriteAsync("ApplicationSettings", "current", settings, cancellationToken);
    }

    async Task<WindowLayout?> IWindowLayoutStore.LoadAsync(CancellationToken cancellationToken)
    {
        var values = await ReadAsync<WindowLayout>("WindowLayout", cancellationToken).ConfigureAwait(false);
        var layout = values.SingleOrDefault();
        layout?.Validate();
        return layout;
    }

    public Task SaveAsync(WindowLayout layout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        layout.Validate();
        return WriteAsync("WindowLayout", "current", layout, cancellationToken);
    }

    async Task<IReadOnlyList<RootFolderHistory>> IRootFolderHistoryStore.LoadAsync(CancellationToken cancellationToken)
    {
        var roots = await ReadAsync<RootFolderHistory>("RootFolderHistory", cancellationToken).ConfigureAwait(false);
        foreach (var root in roots) ValidateRoot(root);
        return roots;
    }

    public Task SaveAsync(RootFolderHistory root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ValidateRoot(root);
        var path = NormalizePath(root.Path);
        return WriteAsync("RootFolderHistory", path.ToUpperInvariant(), root with { Path = path }, cancellationToken);
    }

    Task IRootFolderHistoryStore.RemoveAsync(string path, CancellationToken cancellationToken) =>
        RemoveAsync("RootFolderHistory", NormalizePath(path).ToUpperInvariant(), cancellationToken);

    public async Task RelocateAsync(string oldPath, RootFolderHistory replacement, CancellationToken cancellationToken = default)
    {
        ValidateRoot(replacement);
        var path = NormalizePath(replacement.Path);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // A single atomic UPDATE preserves the old entry if the new primary key conflicts.
        command.CommandText = """
            UPDATE RootFolderHistory SET Id = $new, PayloadVersion = 1, PayloadJson = $json WHERE Id = $old;
            """;
        command.Parameters.AddWithValue("$old", NormalizePath(oldPath).ToUpperInvariant());
        command.Parameters.AddWithValue("$new", path.ToUpperInvariant());
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(replacement with { Path = path }));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new KeyNotFoundException("The original root entry no longer exists.");
    }

    async Task<IReadOnlyList<ThumbnailCacheEntry>> IThumbnailCacheIndexStore.LoadAsync(CancellationToken cancellationToken)
    {
        var entries = await ReadAsync<ThumbnailCacheEntry>("ThumbnailCacheIndex", cancellationToken).ConfigureAwait(false);
        foreach (var entry in entries) ValidateCacheEntry(entry);
        return entries;
    }

    public Task SaveAsync(ThumbnailCacheEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateCacheEntry(entry);
        return WriteAsync("ThumbnailCacheIndex", entry.Key, entry, cancellationToken);
    }

    Task IThumbnailCacheIndexStore.RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return RemoveAsync("ThumbnailCacheIndex", key, cancellationToken);
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("A root path must be absolute.", nameof(path));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static void ValidateRoot(RootFolderHistory root)
    {
        _ = NormalizePath(root.Path);
        if (root.OpenCount < 0) throw new ArgumentOutOfRangeException(nameof(root.OpenCount));
    }

    private static void ValidateCacheEntry(ThumbnailCacheEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.CacheFileName);
        if (entry.CacheFileName is "." or ".." ||
            entry.CacheFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            Path.GetFileName(entry.CacheFileName) != entry.CacheFileName)
            throw new ArgumentException("Cache entries require a leaf filename.", nameof(entry));
        if (entry.SizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(entry.SizeBytes));
    }

    // Table names are private constants supplied above; all record values are parameters.
    private async Task<IReadOnlyList<T>> ReadAsync<T>(string table, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT PayloadVersion, PayloadJson FROM {table} ORDER BY Id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var values = new List<T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.GetInt32(0) != 1)
                throw new InvalidDataException($"Unsupported {table} payload version.");
            values.Add(JsonSerializer.Deserialize<T>(reader.GetString(1))
                ?? throw new InvalidDataException($"Invalid {table} payload."));
        }
        return values;
    }

    private async Task WriteAsync<T>(string table, string id, T value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {table} (Id, PayloadVersion, PayloadJson) VALUES ($id, 1, $json)
            ON CONFLICT(Id) DO UPDATE SET PayloadVersion = excluded.PayloadVersion, PayloadJson = excluded.PayloadJson;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$json", json);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RemoveAsync(string table, string id, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
