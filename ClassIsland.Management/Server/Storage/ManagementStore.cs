using System.Text.Json;
using ClassIsland.Management.Contracts;
using Microsoft.Data.Sqlite;

namespace ClassIsland.Management.Server.Storage;

public sealed class ManagementStore
{
    private readonly string _connectionString;
    private readonly Lock _gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ManagementStore(IConfiguration configuration)
    {
        var directory = Path.GetFullPath(configuration["Management:DataDirectory"] ?? "data");
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "management.db"), ForeignKeys = true
        }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS documents (
                collection TEXT NOT NULL, id TEXT NOT NULL, payload TEXT NOT NULL,
                PRIMARY KEY(collection, id));
            """;
        command.ExecuteNonQuery();
    }

    // 同一进程内串行化读改写，事务同时保证操作与审计记录一起提交。
    public T Transaction<T>(Func<StoreSession, T> action)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var result = action(new StoreSession(connection, transaction));
            transaction.Commit();
            return result;
        }
    }

    public T? Get<T>(string collection, string id) => Transaction(s => s.Get<T>(collection, id));
    public List<T> List<T>(string collection) => Transaction(s => s.List<T>(collection));

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public sealed class StoreSession(SqliteConnection connection, SqliteTransaction transaction)
    {
        public T? Get<T>(string collection, string id)
        {
            using var command = Command("SELECT payload FROM documents WHERE collection=$collection AND id=$id", collection, id);
            return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<T>(json, JsonOptions) : default;
        }

        public List<T> List<T>(string collection)
        {
            using var command = Command("SELECT payload FROM documents WHERE collection=$collection ORDER BY rowid", collection);
            using var reader = command.ExecuteReader();
            var items = new List<T>();
            while (reader.Read()) items.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions)!);
            return items;
        }

        public void Put<T>(string collection, string id, T value)
        {
            using var command = Command("INSERT INTO documents(collection,id,payload) VALUES($collection,$id,$payload) ON CONFLICT(collection,id) DO UPDATE SET payload=$payload", collection, id);
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(value, JsonOptions));
            command.ExecuteNonQuery();
        }

        public void Delete(string collection, string id)
        {
            using var command = Command("DELETE FROM documents WHERE collection=$collection AND id=$id", collection, id);
            command.ExecuteNonQuery();
        }

        public void Audit(string actor, string action, string target, string detail = "")
        {
            var entry = new AuditInfo { Actor = actor, Action = action, Target = target, Detail = detail };
            Put("audit", entry.Id, entry);
        }

        private SqliteCommand Command(string sql, string collection, string? id = null)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$collection", collection);
            if (id != null) command.Parameters.AddWithValue("$id", id);
            return command;
        }
    }
}
