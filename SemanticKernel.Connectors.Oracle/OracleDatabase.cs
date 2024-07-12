using System.Runtime.CompilerServices;
using Oracle.ManagedDataAccess.Client;

namespace SemanticKernel.Connectors.Oracle;

class OracleDatabase(OracleConnection oracleConnection, int vectorSize, bool ownsConnection = false) : IDisposable
{
    private readonly int vectorSize = vectorSize;

    public async Task CreateTableAsync(string collectionName, CancellationToken cancellationToken)
    {
        await using var command = oracleConnection.CreateCommand();
        command.CommandText = $"CREATE TABLE {collectionName} (key VARCHAR2(256) NOT NULL, embedding BLOB NOT NULL, PRIMARY KEY(key))";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DoesTableExistAsync(string collectionName, CancellationToken cancellationToken)
    {
        await using var command = oracleConnection.CreateCommand();
        command.CommandText = $"SELECT * FROM user_tables WHERE table_name = '{collectionName}'";
        return (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != null; 
    }

    public async IAsyncEnumerable<string> GetTablesAsync([EnumeratorCancellation]CancellationToken cancellationToken)
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
            
    }

    public async Task DeleteAsync(string collectionName, string key, CancellationToken cancellationToken)
    {
            
    }

    public async Task RemoveAsync(string collectionName, IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<OracleMemoryEntry> ReadBatchAsync(string collectionName, IEnumerable<string> keys, bool withEmbeddings, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public async Task<OracleMemoryEntry?> ReadSingleAsync(string collectionName, string key, bool withEmbedding, CancellationToken cancellationToken)
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