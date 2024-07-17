using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Runtime.CompilerServices;

namespace SemanticKernel.Connectors.Oracle;

internal class OracleDatabaseManager : IDisposable
{
    private readonly int vectorSize;
    private readonly OracleConnection oracleConnection;
    private readonly bool ownsConnection;

    private const string QueryColumnsWithoutEmbeddings = "key, metadata, timestamp";
    private const string QueryColumnsWithEmbeddings = QueryColumnsWithoutEmbeddings + " , embedding";

    private const int KeyIndex = 0;
    private const int MetadataIndex = KeyIndex + 1;
    private const int TimestampIndex = MetadataIndex + 1;
    private const int EmbeddingIndex = TimestampIndex + 1;

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

    public async Task CreateTableAsync(string tableName, CancellationToken cancellationToken)
    {
        await using var command = oracleConnection.CreateCommand();
        command.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {tableName} (
                    key Varchar2(100) NOT NULL,
                    metadata JSON,
                    embedding VECTOR({vectorSize}, FLOAT32),
                    timestamp TIMESTAMP,
                    PRIMARY KEY (key))";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DoesTableExistAsync(string tableName, CancellationToken cancellationToken)
    {
        await using var command = oracleConnection.CreateCommand();
        command.BindByName = true;
        command.CommandText = $"SELECT * FROM user_tables WHERE table_name = :tableName";
        command.Parameters.Add("tableName", tableName.ToUpper());

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

    public async Task DeleteTableAsync(string tableName, CancellationToken cancellationToken)
    {
        await using var command = oracleConnection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS {tableName}";

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string tableName, string key, CancellationToken cancellationToken)
    {
        await using var command = oracleConnection.CreateCommand();
        command.BindByName = true;
        command.CommandText = $"DELETE FROM {tableName} WHERE key=:key";
        command.Parameters.Add("key", key);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string tableName, IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        var keysArray = keys.ToArray();

        await using var command = oracleConnection.CreateCommand();

        command.BindByName = true;
        command.ArrayBindCount = keysArray.Length;

        command.CommandText = $"DELETE FROM {tableName} WHERE key=:key";
        command.Parameters.Add("key", keysArray);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertAsync(string tableName, string key, string? metadata, float[] embedding, DateTime? timestamp, CancellationToken cancellationToken)
    {
        await using var command = oracleConnection.CreateCommand();
        command.BindByName = true;

        command.CommandText = $@"MERGE INTO {tableName} 
                USING dual ON (key=:key)
                WHEN MATCHED THEN UPDATE SET metadata=:metadata, embedding=:embedding, timestamp=:timestamp
                WHEN NOT MATCHED THEN INSERT (key, metadata, embedding, timestamp) VALUES (:key, :metadata, :embedding, :timestamp)";

        command.Parameters.Add("key", key);
        command.Parameters.Add("metadata", OracleDbType.Json, metadata?.Length ?? 0, metadata ?? (object)DBNull.Value, ParameterDirection.Input);
        command.Parameters.Add("embedding", OracleDbType.Vector_Float32, embedding ?? (object)DBNull.Value, ParameterDirection.Input);
        command.Parameters.Add("timestamp", timestamp ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<OracleMemoryEntry?> ReadSingleAsync(string tableName, string key, bool withEmbeddings, CancellationToken cancellationToken)
    {
        await using var command = oracleConnection.CreateCommand();
        command.BindByName = true;
        command.CommandText = $"SELECT {(withEmbeddings ? QueryColumnsWithEmbeddings : QueryColumnsWithoutEmbeddings)} FROM {tableName} WHERE key=:key";
        command.Parameters.Add("key", key);

        await using var dataReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return await ReadEntryAsync(dataReader, withEmbeddings, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public async IAsyncEnumerable<OracleMemoryEntry> ReadBatchAsync(string tableName, IEnumerable<string> keys, bool withEmbeddings, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        //var keysArray = keys.ToArray();
        //if (keysArray.Length == 0)
        //{
        //    yield break;
        //}

        foreach (var key in keys)
        {
            var oracleMemoryEntry = await ReadSingleAsync(tableName, key, withEmbeddings, cancellationToken).ConfigureAwait(false);

            if (oracleMemoryEntry == null)
            {
                continue;
            }

            yield return oracleMemoryEntry.Value;
        }

        //await using var command = oracleConnection.CreateCommand();

        //command.BindByName = true;
        //command.ArrayBindCount = keysArray.Length;

        //command.CommandText = $"SELECT {(withEmbeddings ? QueryColumnsWithEmbeddings : QueryColumnsWithoutEmbeddings)} FROM {tableName} WHERE key = :key";

        //command.Parameters.Add("key", keysArray);

        //await using var dataReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        //while (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        //{
        //    yield return await this.ReadEntryAsync(dataReader, withEmbeddings, cancellationToken).ConfigureAwait(false);
        //}
    }

    public async IAsyncEnumerable<(OracleMemoryEntry, double)> GetNearestMatchesAsync(string tableName, ReadOnlyMemory<float> embedding, int limit, double minRelevanceScore, bool withEmbeddings, CancellationToken cancellationToken)
    {
        await using var command = oracleConnection.CreateCommand();

        command.BindByName = true;
        command.CommandText = @$"
            SELECT * FROM (SELECT {(withEmbeddings ? QueryColumnsWithEmbeddings : QueryColumnsWithoutEmbeddings)}, 1 - (embedding <=> :embedding) AS cosine_similarity FROM {tableName}
            ) sk_memory_cosine_similarity_table
            WHERE cosine_similarity >= :min_relevance_score
            ORDER BY cosine_similarity DESC
            FETCH NEXT :limit ROWS ONLY";

        command.Parameters.Add("embedding", OracleDbType.Vector_Float32, embedding.ToArray(), ParameterDirection.Input);
        command.Parameters.Add("min_relevance_score", minRelevanceScore);
        command.Parameters.Add("limit", limit);

        await using var dataReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var cosineSimilarity = dataReader.GetDouble(dataReader.FieldCount - 1);
            yield return (await this.ReadEntryAsync(dataReader, withEmbeddings, cancellationToken).ConfigureAwait(false), cosineSimilarity);
        }
    }

    public void Dispose()
    {
        if (ownsConnection)
        {
            oracleConnection.Dispose();
        }
    }

    private async Task<OracleMemoryEntry> ReadEntryAsync(OracleDataReader dataReader, bool withEmbeddings = false, CancellationToken cancellationToken = default)
    {
        var key = dataReader.GetString(KeyIndex);
        var metadata = dataReader.GetString(MetadataIndex);
        var timestamp = dataReader.IsDBNull(TimestampIndex) ? (DateTime?)null : await dataReader.GetFieldValueAsync<DateTime>(TimestampIndex, cancellationToken).ConfigureAwait(false);
        var embedding = withEmbeddings ? await dataReader.GetFieldValueAsync<float[]>(EmbeddingIndex, cancellationToken).ConfigureAwait(false) : null;

        return new OracleMemoryEntry() { Key = key, MetadataString = metadata, Embedding = embedding, Timestamp = timestamp };
    }
}