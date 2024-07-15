using Microsoft.SemanticKernel.Memory;
using Oracle.ManagedDataAccess.Client;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SemanticKernel.Connectors.Oracle;

[Experimental("SKEXP0001")]
public class OracleMemoryStore : IMemoryStore, IDisposable
{
    private readonly OracleDatabaseManager databaseManager;

    public OracleMemoryStore(string connectionString, int vectorSize)
    {
        databaseManager = new OracleDatabaseManager(new OracleConnection(connectionString), vectorSize, true);
    }

    public OracleMemoryStore(OracleConnection connection, int vectorSize)
    {
        databaseManager = new OracleDatabaseManager(connection, vectorSize);
    }

    /// <inheritdoc/>
    public async Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = new())
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        await databaseManager.CreateTableAsync(collectionName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> GetCollectionsAsync([EnumeratorCancellation] CancellationToken cancellationToken = new())
    {
        await foreach (var collection in databaseManager.GetTablesAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return collection;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DoesCollectionExistAsync(string collectionName, CancellationToken cancellationToken = new())
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        return await databaseManager.DoesTableExistAsync(collectionName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = new())
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        await databaseManager.DeleteTableAsync(collectionName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string> UpsertAsync(string collectionName, MemoryRecord record,
        CancellationToken cancellationToken = new())
    {
        return await InternalUpsertAsync(collectionName, record, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> UpsertBatchAsync(string collectionName, IEnumerable<MemoryRecord> records,
        [EnumeratorCancellation] CancellationToken cancellationToken = new())
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        foreach (var record in records)
        {
            yield return await InternalUpsertAsync(collectionName, record, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<MemoryRecord?> GetAsync(string collectionName, string key, bool withEmbedding = false,
        CancellationToken cancellationToken = new())
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        return await InternalGetAsync(collectionName, key, withEmbedding, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<MemoryRecord> GetBatchAsync(string collectionName, IEnumerable<string> keys, bool withEmbeddings = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = new())
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        await foreach (var entry in InternalGetAsync(collectionName, keys, withEmbeddings, cancellationToken).ConfigureAwait(false))
        {
            yield return entry;
        }
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string collectionName, string key, CancellationToken cancellationToken = new())
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        await databaseManager.DeleteAsync(collectionName, key, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RemoveBatchAsync(string collectionName, IEnumerable<string> keys,
        CancellationToken cancellationToken = new())
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        await databaseManager.DeleteAsync(collectionName, keys, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<(MemoryRecord, double)> GetNearestMatchesAsync(string collectionName, ReadOnlyMemory<float> embedding, int limit,
        double minRelevanceScore = 0, bool withEmbeddings = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = new())
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        if (limit <= 0)
        {
            yield break;
        }

        var results = databaseManager.GetNearestMatchesAsync(
            tableName: collectionName,
            embedding: embedding,
            limit: limit,
            minRelevanceScore: minRelevanceScore,
            withEmbeddings: withEmbeddings,
            cancellationToken: cancellationToken);

        await foreach (var (entry, cosineSimilarity) in results.ConfigureAwait(false))
        {
            yield return (GetMemoryRecordFromEntry(entry), cosineSimilarity);
        }
    }

    /// <inheritdoc/>
    public async Task<(MemoryRecord, double)?> GetNearestMatchAsync(string collectionName, ReadOnlyMemory<float> embedding, double minRelevanceScore = 0,
        bool withEmbedding = false, CancellationToken cancellationToken = new())
    {
        return await GetNearestMatchesAsync(
            collectionName: collectionName,
            embedding: embedding,
            limit: 1,
            minRelevanceScore: minRelevanceScore,
            withEmbeddings: withEmbedding,
            cancellationToken: cancellationToken).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        databaseManager.Dispose();
    }

    private async Task<string> InternalUpsertAsync(string collectionName, MemoryRecord record, CancellationToken cancellationToken)
    {
        record.Key = record.Metadata.Id;

        await databaseManager.UpsertAsync(
            tableName: collectionName,
            key: record.Key,
            metadata: record.GetSerializedMetadata(),
            embedding: record.Embedding.ToArray(),
            timestamp: record.Timestamp?.UtcDateTime,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return record.Key;
    }

    private async Task<MemoryRecord?> InternalGetAsync(string collectionName, string key, bool withEmbedding, CancellationToken cancellationToken)
    {
        var entry = await databaseManager.ReadSingleAsync(collectionName, key, withEmbedding, cancellationToken);

        return entry == null ? null : GetMemoryRecordFromEntry(entry.Value);
    }

    private async IAsyncEnumerable<MemoryRecord> InternalGetAsync(string collectionName, IEnumerable<string> keys, bool withEmbeddings, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var entry in databaseManager.ReadBatchAsync(collectionName, keys, withEmbeddings, cancellationToken).ConfigureAwait(false))
        {
            yield return GetMemoryRecordFromEntry(entry);
        }
    }

    private static MemoryRecord GetMemoryRecordFromEntry(OracleMemoryEntry entry)
    {
        return MemoryRecord.FromJsonMetadata(
            json: entry.MetadataString,
            embedding: entry.Embedding ?? ReadOnlyMemory<float>.Empty,
            key: entry.Key,
            timestamp: entry.Timestamp?.ToLocalTime());
    }
}