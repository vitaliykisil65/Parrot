using Microsoft.Data.Sqlite;

namespace Parrot.Core.Data;

/// <summary>Owns the SQLite connection string and the schema version.</summary>
public sealed class Database : IDisposable
{
    private const int SchemaVersion = 1;

    /// <summary>
    /// An in-memory database only exists while some connection to it is open, so tests
    /// need one held open for the lifetime of this object.
    /// </summary>
    private readonly SqliteConnection? _keepAlive;

    public Database(string? databaseFile = null)
    {
        var file = databaseFile ?? AppPaths.DatabaseFile;

        if (!IsInMemory(file))
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);

        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Shared cache keeps an in-memory database alive across pooled connections,
            // which is what makes ":memory:" usable from the tests.
            Cache = IsInMemory(file) ? SqliteCacheMode.Shared : SqliteCacheMode.Default,
        }.ToString();

        if (IsInMemory(file))
        {
            _keepAlive = new SqliteConnection(ConnectionString);
            _keepAlive.Open();
        }

        Migrate();
    }

    public void Dispose() => _keepAlive?.Dispose();

    public string ConnectionString { get; }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    private void Migrate()
    {
        using var connection = OpenConnection();

        using var read = connection.CreateCommand();
        read.CommandText = "PRAGMA user_version;";
        var current = Convert.ToInt32(read.ExecuteScalar());

        if (current >= SchemaVersion)
            return;

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        if (current < 1)
            command.CommandText = Schema.V1;

        command.ExecuteNonQuery();

        command.CommandText = $"PRAGMA user_version = {SchemaVersion};";
        command.ExecuteNonQuery();

        transaction.Commit();

        // WAL survives across connections and cannot be set inside a transaction.
        using var wal = connection.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode = WAL;";
        wal.ExecuteNonQuery();
    }

    private static bool IsInMemory(string file) =>
        file.Contains(":memory:", StringComparison.OrdinalIgnoreCase) ||
        file.StartsWith("file::memory:", StringComparison.OrdinalIgnoreCase);
}
