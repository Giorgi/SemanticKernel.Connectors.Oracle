using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Runtime.CompilerServices;

namespace SemanticKernel.Connectors.Oracle;

internal class OracleDatabaseManager : IDisposable
{
    private readonly int vectorSize;
    private readonly OracleConnection oracleConnection;
    private readonly bool ownsConnection;

    public OracleDatabaseManager(OracleConnection oracleConnection, int vectorSize, bool ownsConnection = false)
    {
        this.vectorSize = vectorSize;
        this.ownsConnection = ownsConnection;
        this.oracleConnection = oracleConnection;

        if (this.oracleConnection.State == ConnectionState.Closed)
        {
            this.oracleConnection.Open();
        }
    }

    public async Task CreateTableAsync(string collectionName, CancellationToken cancellationToken)
    {
        await using var command = oracleConnection.CreateCommand();
        command.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {collectionName} (
                    key TEXT NOT NULL,
                    metadata JSON,
                    embedding VECTOR({vectorSize}, FLOAT32),
                    timestamp TIMESTAMP WITH TIME ZONE,
                    PRIMARY KEY (key))";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DoesTableExistAsync(string collectionName, CancellationToken cancellationToken)
    {
        await using var command = oracleConnection.CreateCommand();
        command.CommandText = $"SELECT * FROM user_tables WHERE table_name = :tableName";
        command.BindByName = true;
        command.Parameters.Add(new OracleParameter("tableName", collectionName.ToUpper()));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return reader.HasRows;
    }

    public async IAsyncEnumerable<string> GetTablesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var command = oracleConnection.CreateCommand();
        command.CommandText = "SELECT table_name FROM user_tables";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return reader.GetString(0);
        }
    }

    public async Task DeleteTableAsync(string collectionName, CancellationToken cancellationToken)
    {
        await using var command = oracleConnection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS {collectionName}";

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string collectionName, string key, CancellationToken cancellationToken)
    {
        await using var command = oracleConnection.CreateCommand();
        command.BindByName = true;
        command.CommandText = $"DELETE FROM {collectionName} WHERE key=:key";
        command.Parameters.Add(new OracleParameter("key", key));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string collectionName, IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public async Task UpsertAsync(string tableName, string key, string metadata, float[] embedding, DateTime? timestamp, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public async Task<OracleMemoryEntry?> ReadSingleAsync(string collectionName, string key, bool withEmbedding, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<OracleMemoryEntry> ReadBatchAsync(string collectionName, IEnumerable<string> keys, bool withEmbeddings, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<(OracleMemoryEntry, double)> GetNearestMatchesAsync(string tableName, ReadOnlyMemory<float> embedding, int limit, double minRelevanceScore, bool withEmbeddings, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        if (ownsConnection)
        {
            oracleConnection.Dispose();
        }
    }
}