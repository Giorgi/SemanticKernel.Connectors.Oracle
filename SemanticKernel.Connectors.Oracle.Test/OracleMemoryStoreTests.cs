// This file is based on https://github.com/microsoft/semantic-kernel/blob/main/dotnet/src/Connectors/Connectors.UnitTests/Memory/DuckDB/DuckDBMemoryStoreTests.cs
// Copyright (c) Microsoft. All rights reserved.
// Adapted for OracleMemoryStore by Giorgi Dalakishvili

using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel.Memory;
using Oracle.ManagedDataAccess.Client;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SemanticKernel.Connectors.Oracle.Test;

[Experimental("SKEXP0001")]
public class OracleMemoryStoreTests : IDisposable
{
    private int collectionNumber = 0;
    private static readonly string ConnectionString;

    static OracleMemoryStoreTests()
    {
        var builder = new ConfigurationBuilder()
               .AddEnvironmentVariables("SK_")
#if DEBUG
               .AddJsonFile($"testsettings.json", optional: true)
#endif
            ;

        var configurationRoot = builder.Build();

        var connectionString = configurationRoot["ConnectionString"];

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Connection String not initialized");
        }
        else
        {
            ConnectionString = connectionString;
        }
    }

    [Fact]
    public async Task ItCanCreateAndGetCollectionAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var collection = this.GetTestCollectionName();

        // Act
        await db.CreateCollectionAsync(collection);
        var collections = db.GetCollectionsAsync().ToBlockingEnumerable().ToList();

        // Assert
        Assert.NotEmpty(collections);
        Assert.Contains(collection, collections, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ItCanCheckIfCollectionExistsAsync()
    {
        // Arrange
        await using var oracleConnection = new OracleConnection(ConnectionString);
        using var db = new OracleMemoryStore(oracleConnection, 3);
        var collection = "my_collection";

        // Act
        await db.CreateCollectionAsync(collection);

        // Assert
        Assert.True(await db.DoesCollectionExistAsync("my_collection"));
        Assert.False(await db.DoesCollectionExistAsync("my_collection---"));
    }

    [Fact]
    public async Task CreatingDuplicateCollectionDoesNothingAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var collection = this.GetTestCollectionName();

        // Act
        await db.CreateCollectionAsync(collection);
        var collections = db.GetCollectionsAsync();

        await db.CreateCollectionAsync(collection);

        // Assert
        var collections2 = db.GetCollectionsAsync();
        Assert.Equal(collections.ToBlockingEnumerable().Count(), collections2.ToBlockingEnumerable().Count());
    }

    [Fact]
    public async Task CollectionsCanBeDeletedAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var collection = this.GetTestCollectionName();

        await db.CreateCollectionAsync(collection);

        var collections = db.GetCollectionsAsync().ToBlockingEnumerable().ToList();
        Assert.True(collections.Count > 0);

        // Act
        foreach (var c in collections)
        {
            await db.DeleteCollectionAsync(c);
        }

        // Assert
        var collections2 = db.GetCollectionsAsync();
        Assert.Empty(collections2.ToBlockingEnumerable());
    }

    [Fact]
    public async Task ItCanNotInsertLargerVectorAsync()
    {
        // Arrange
        float[] embedding = [1, 2, 3];
        using var db = new OracleMemoryStore(ConnectionString, embedding.Length - 1);

        var collection = this.GetTestCollectionName();
        await db.CreateCollectionAsync(collection);

        var testRecord = MemoryRecord.LocalRecord(
            id: "test",
            text: "text",
            description: "description",
            embedding: embedding,
            key: null,
            timestamp: null);

        var oracleException = await Assert.ThrowsAsync<OracleException>(async () => await db.UpsertAsync(collection, testRecord));
        Assert.Contains("Vector dimension count must match the dimension count specified in the column definition", oracleException.Message);
    }

    [Fact]
    public async Task ItCanNotInsertSmallerVectorAsync()
    {
        // Arrange
        float[] embedding = [1, 2, 3];
        using var db = new OracleMemoryStore(ConnectionString, embedding.Length + 1);

        var collection = this.GetTestCollectionName();
        await db.CreateCollectionAsync(collection);

        var testRecord = MemoryRecord.LocalRecord(
            id: "test",
            text: "text",
            description: "description",
            embedding: embedding,
            key: null,
            timestamp: null);

        var oracleException = await Assert.ThrowsAsync<OracleException>(async () => await db.UpsertAsync(collection, testRecord));
        Assert.Contains("Vector dimension count must match the dimension count specified in the column definition", oracleException.Message);
    }

    [Fact]
    public async Task GetAsyncReturnsEmptyEmbeddingUnlessSpecifiedAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var testRecord = MemoryRecord.LocalRecord(
            id: "test",
            text: "text",
            description: "description",
            embedding: new float[] { 1, 2, 3 },
            key: null,
            timestamp: null);
        var collection = this.GetTestCollectionName();

        // Act
        await db.CreateCollectionAsync(collection);
        var key = await db.UpsertAsync(collection, testRecord);

        var actualDefault = await db.GetAsync(collection, key);
        var actualWithEmbedding = await db.GetAsync(collection, key, true);

        // Assert
        Assert.NotNull(actualDefault);
        Assert.NotNull(actualWithEmbedding);

        Assert.True(actualDefault.Embedding.IsEmpty);
        Assert.Equal(actualWithEmbedding.Embedding.ToArray(), testRecord.Embedding.ToArray());
    }

    [Fact]
    public async Task ItCanUpsertAndRetrieveARecordWithNoTimestampAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var testRecord = MemoryRecord.LocalRecord(
            id: "test",
            text: "text",
            description: "description",
            embedding: new float[] { 1, 2, 3 },
            key: null,
            timestamp: null);
        var collection = this.GetTestCollectionName();

        // Act
        await db.CreateCollectionAsync(collection);
        var key = await db.UpsertAsync(collection, testRecord);
        var actual = await db.GetAsync(collection, key, true);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(testRecord.Metadata.Id, key);
        Assert.Equal(testRecord.Metadata.Id, actual.Key);
        Assert.Equal(testRecord.Embedding.ToArray(), actual.Embedding.ToArray());

        Assert.Equal(testRecord.Metadata.Text, actual.Metadata.Text);
        Assert.Equal(testRecord.Metadata.Description, actual.Metadata.Description);
        Assert.Equal(testRecord.Metadata.ExternalSourceName, actual.Metadata.ExternalSourceName);
        Assert.Equal(testRecord.Metadata.Id, actual.Metadata.Id);
    }

    [Fact]
    public async Task ItCanUpsertAndRetrieveARecordWithTimestampAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var testRecord = MemoryRecord.LocalRecord(
            id: "test",
            text: "text",
            description: "description",
            embedding: new float[] { 1, 2, 3 },
            key: null,
            timestamp: DateTimeOffset.UtcNow);
        var collection = this.GetTestCollectionName();

        // Act
        await db.CreateCollectionAsync(collection);
        var key = await db.UpsertAsync(collection, testRecord);
        var actual = await db.GetAsync(collection, key, true);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(testRecord.Metadata.Id, key);
        Assert.Equal(testRecord.Metadata.Id, actual.Key);
        Assert.Equal(testRecord.Embedding.ToArray(), actual.Embedding.ToArray());

        Assert.Equal(testRecord.Metadata.Text, actual.Metadata.Text);
        Assert.Equal(testRecord.Metadata.Description, actual.Metadata.Description);
        Assert.Equal(testRecord.Metadata.ExternalSourceName, actual.Metadata.ExternalSourceName);
        Assert.Equal(testRecord.Metadata.Id, actual.Metadata.Id);
    }

    [Fact]
    public async Task UpsertReplacesExistingRecordWithSameIdAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var commonId = "test";
        var testRecord = MemoryRecord.LocalRecord(
            id: commonId,
            text: "text",
            description: "description",
            embedding: new float[] { 1, 2, 3 });
        var testRecord2 = MemoryRecord.LocalRecord(
            id: commonId,
            text: "text2",
            description: "description2",
            embedding: new float[] { 1, 2, 4 });
        var collection = this.GetTestCollectionName();

        // Act
        await db.CreateCollectionAsync(collection);
        var key = await db.UpsertAsync(collection, testRecord);
        var key2 = await db.UpsertAsync(collection, testRecord2);
        var actual = await db.GetAsync(collection, key, true);

        // Assert
        Assert.NotNull(actual);

        Assert.Equal(testRecord.Metadata.Id, key);
        Assert.Equal(testRecord2.Metadata.Id, actual.Key);

        Assert.NotEqual(testRecord.Embedding.ToArray(), actual.Embedding.ToArray());
        Assert.Equal(testRecord2.Embedding.ToArray(), actual.Embedding.ToArray());

        Assert.NotEqual(testRecord.Metadata.Text, actual.Metadata.Text);
        Assert.Equal(testRecord2.Metadata.Description, actual.Metadata.Description);
    }

    [Fact]
    public async Task ExistingRecordCanBeRemovedAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var testRecord = MemoryRecord.LocalRecord(
            id: "test",
            text: "text",
            description: "description",
            embedding: new float[] { 1, 2, 3 });
        var collection = this.GetTestCollectionName();

        // Act
        await db.CreateCollectionAsync(collection);
        var key = await db.UpsertAsync(collection, testRecord);

        await db.RemoveAsync(collection, key);
        var actual = await db.GetAsync(collection, key);

        // Assert
        Assert.Null(actual);
    }

    [Fact]
    public async Task RemovingNonExistingRecordDoesNothingAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var collection = "test_collection_for_record_deletion";

        // Act
        await db.CreateCollectionAsync(collection);
        await db.RemoveAsync(collection, "key");
        var actual = await db.GetAsync(collection, "key");

        // Assert
        Assert.Null(actual);
    }

    [Fact]
    public async Task ItCanListAllDatabaseCollectionsAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        string[] testCollections = ["random_collection1", "random_collection2", "random_collection3"];

        await db.CreateCollectionAsync(testCollections[0]);
        await db.CreateCollectionAsync(testCollections[1]);
        await db.CreateCollectionAsync(testCollections[2]);

        // Act
        var collections = db.GetCollectionsAsync().ToBlockingEnumerable().ToList();

        // Assert
        foreach (var collection in testCollections)
        {
            Assert.True(await db.DoesCollectionExistAsync(collection));
        }

        Assert.NotNull(collections);
        Assert.NotEmpty(collections);

        Assert.Equal(testCollections.Length, collections.Count);

        Assert.Contains(testCollections[0], collections, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(testCollections[1], collections, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(testCollections[2], collections, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetNearestMatchesReturnsAllResultsWithNoMinScoreAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var compareEmbedding = new float[] { 1, 1, 1 };
        var topN = 4;
        var collection = this.GetTestCollectionName();

        await db.CreateCollectionAsync(collection);

        var i = 0;
        var testRecord = MemoryRecord.LocalRecord(
            id: "test" + i,
            text: "text" + i,
            description: "description" + i,
            embedding: new float[] { 1, 1, 1 });

        await db.UpsertAsync(collection, testRecord);

        i++;
        testRecord = MemoryRecord.LocalRecord(
            id: "test" + i,
            text: "text" + i,
            description: "description" + i,
            embedding: new float[] { -1, -1, -1 });
        await db.UpsertAsync(collection, testRecord);

        i++;
        testRecord = MemoryRecord.LocalRecord(
            id: "test" + i,
            text: "text" + i,
            description: "description" + i,
            embedding: new float[] { 1, 2, 3 });
        await db.UpsertAsync(collection, testRecord);

        i++;
        testRecord = MemoryRecord.LocalRecord(
            id: "test" + i,
            text: "text" + i,
            description: "description" + i,
            embedding: new float[] { -1, -2, -3 });
        await db.UpsertAsync(collection, testRecord);

        i++;
        testRecord = MemoryRecord.LocalRecord(
            id: "test" + i,
            text: "text" + i,
            description: "description" + i,
            embedding: new float[] { 1, -1, -2 });
        await db.UpsertAsync(collection, testRecord);

        // Act
        var threshold = -1;
        var topNResults = db.GetNearestMatchesAsync(collection, compareEmbedding, limit: topN, minRelevanceScore: threshold).ToBlockingEnumerable().ToArray();

        // Assert
        Assert.Equal(topN, topNResults.Length);
        for (var j = 0; j < topN - 1; j++)
        {
            var compare = topNResults[j].Item2.CompareTo(topNResults[j + 1].Item2);
            Assert.True(compare >= 0);
        }
    }

    [Fact]
    public async Task GetNearestMatchAsyncReturnsEmptyEmbeddingUnlessSpecifiedAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var compareEmbedding = new float[] { 1, 1, 1 };
        var collection = this.GetTestCollectionName();

        await db.CreateCollectionAsync(collection);
        var i = 0;
        var testRecord = MemoryRecord.LocalRecord(
            id: "test" + i,
            text: "text" + i,
            description: "description" + i,
            embedding: new float[] { 1, 1, 1 });
        await db.UpsertAsync(collection, testRecord);

        i++;
        testRecord = MemoryRecord.LocalRecord(
            id: "test" + i,
            text: "text" + i,
            description: "description" + i,
            embedding: new float[] { -1, -1, -1 });
        await db.UpsertAsync(collection, testRecord);

        i++;
        testRecord = MemoryRecord.LocalRecord(
            id: "test" + i,
            text: "text" + i,
            description: "description" + i,
            embedding: new float[] { 1, 2, 3 });
        await db.UpsertAsync(collection, testRecord);

        i++;
        testRecord = MemoryRecord.LocalRecord(
            id: "test" + i,
            text: "text" + i,
            description: "description" + i,
            embedding: new float[] { -1, -2, -3 });
        await db.UpsertAsync(collection, testRecord);

        i++;
        testRecord = MemoryRecord.LocalRecord(
            id: "test" + i,
            text: "text" + i,
            description: "description" + i,
            embedding: new float[] { 1, -1, -2 });
        await db.UpsertAsync(collection, testRecord);

        // Act
        var threshold = 0.75;
        var topNResultDefault = await db.GetNearestMatchAsync(collection, compareEmbedding, minRelevanceScore: threshold);
        var topNResultWithEmbedding = await db.GetNearestMatchAsync(collection, compareEmbedding, minRelevanceScore: threshold, withEmbedding: true);

        // Assert
        Assert.NotNull(topNResultDefault);
        Assert.NotNull(topNResultWithEmbedding);
        Assert.True(topNResultDefault.Value.Item1.Embedding.IsEmpty);
        Assert.False(topNResultWithEmbedding.Value.Item1.Embedding.IsEmpty);
    }

    [Fact]
    public async Task GetNearestMatchAsyncReturnsExpectedAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var compareEmbedding = new float[] { 1, 1, 1 };
        var collection = this.GetTestCollectionName();

        await db.CreateCollectionAsync(collection);
        var i = 0;
        var testRecord = MemoryRecord.LocalRecord(
            id: "test" + i,
            text: "text" + i,
            description: "description" + i,
            embedding: new float[] { 1, 1, 1 });
        await db.UpsertAsync(collection, testRecord);

        i++;
        testRecord = MemoryRecord.LocalRecord(
            id: "test" + i,
            text: "text" + i,
            description: "description" + i,
            embedding: new float[] { -1, -1, -1 });
        await db.UpsertAsync(collection, testRecord);

        i++;
        testRecord = MemoryRecord.LocalRecord(
            id: "test" + i,
            text: "text" + i,
            description: "description" + i,
            embedding: new float[] { 1, 2, 3 });
        await db.UpsertAsync(collection, testRecord);

        i++;
        testRecord = MemoryRecord.LocalRecord(
            id: "test" + i,
            text: "text" + i,
            description: "description" + i,
            embedding: new float[] { -1, -2, -3 });
        await db.UpsertAsync(collection, testRecord);

        i++;
        testRecord = MemoryRecord.LocalRecord(
            id: "test" + i,
            text: "text" + i,
            description: "description" + i,
            embedding: new float[] { 1, -1, -2 });
        await db.UpsertAsync(collection, testRecord);

        // Act
        var threshold = 0.75;
        var topNResult = await db.GetNearestMatchAsync(collection, compareEmbedding, minRelevanceScore: threshold);

        // Assert
        Assert.NotNull(topNResult);
        Assert.Equal("test0", topNResult.Value.Item1.Metadata.Id);
        Assert.True(topNResult.Value.Item2 >= threshold);
    }

    [Fact]
    public async Task GetNearestMatchesDifferentiatesIdenticalVectorsByKeyAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var compareEmbedding = new float[] { 1, 1, 1 };
        var topN = 4;
        var collection = this.GetTestCollectionName();

        await db.CreateCollectionAsync(collection);

        for (var i = 0; i < 10; i++)
        {
            var testRecord = MemoryRecord.LocalRecord(
                id: "test" + i,
                text: "text" + i,
                description: "description" + i,
                embedding: new float[] { 1, 1, 1 });
            await db.UpsertAsync(collection, testRecord);
        }

        // Act
        var topNResults = db.GetNearestMatchesAsync(collection, compareEmbedding, limit: topN, minRelevanceScore: 0.75).ToBlockingEnumerable().ToArray();

        // Assert
        Assert.Equal(topN, topNResults.Length);

        for (var i = 0; i < topNResults.Length; i++)
        {
            var compare = topNResults[i].Item2.CompareTo(0.75);
            Assert.True(compare >= 0);
        }
    }

    [Fact]
    public async Task ItCanBatchUpsertRecordsAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var numRecords = 10;
        var collection = this.GetTestCollectionName();

        var records = this.CreateBatchRecords(numRecords);

        // Act
        await db.CreateCollectionAsync(collection);
        var keys = db.UpsertBatchAsync(collection, records).ToBlockingEnumerable();
        var resultRecords = db.GetBatchAsync(collection, keys);

        // Assert
        Assert.NotNull(keys);
        Assert.Equal(numRecords, keys.Count());
        Assert.Equal(numRecords, resultRecords.ToBlockingEnumerable().Count());
    }

    [Fact]
    public async Task ItCanBatchGetRecordsAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var numRecords = 10;
        var collection = this.GetTestCollectionName();

        var records = this.CreateBatchRecords(numRecords);
        var keys = db.UpsertBatchAsync(collection, records);

        // Act
        await db.CreateCollectionAsync(collection);
        var results = db.GetBatchAsync(collection, keys.ToBlockingEnumerable());

        // Assert
        Assert.NotNull(keys);
        Assert.NotNull(results);
        Assert.Equal(numRecords, results.ToBlockingEnumerable().Count());
    }

    [Fact]
    public async Task ItCanBatchRemoveRecordsAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var numRecords = 10;
        var collection = this.GetTestCollectionName();

        var records = this.CreateBatchRecords(numRecords);
        await db.CreateCollectionAsync(collection);

        List<string> keys = [];

        // Act
        await foreach (var key in db.UpsertBatchAsync(collection, records))
        {
            keys.Add(key);
        }

        await db.RemoveBatchAsync(collection, keys);

        // Assert
        await foreach (var result in db.GetBatchAsync(collection, keys))
        {
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task DeletingNonExistentCollectionDoesNothingAsync()
    {
        // Arrange
        using var db = new OracleMemoryStore(ConnectionString, 3);
        var collection = this.GetTestCollectionName();

        // Act
        await db.DeleteCollectionAsync(collection);
    }

    public void Dispose()
    {
        // Delete all tables in the database
        using var oracleConnection = new OracleConnection(ConnectionString);
        oracleConnection.Open();
        using var command = oracleConnection.CreateCommand();

        command.CommandText = "select 'drop table ', table_name, ' cascade constraints' from user_tables";
        using var dataReader = command.ExecuteReader();
        var statements = new List<string>();
        while (dataReader.Read())
        {
            statements.Add(dataReader.GetString(0) + dataReader.GetString(1) + dataReader.GetString(2));
        }

        foreach (var statement in statements)
        {
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
    }

    private string GetTestCollectionName([CallerMemberName] string testName = "")
    {
        return testName + this.collectionNumber++;
    }

    private IEnumerable<MemoryRecord> CreateBatchRecords(int numRecords)
    {
        Assert.True(numRecords % 2 == 0, "Number of records must be even");
        Assert.True(numRecords > 0, "Number of records must be greater than 0");

        IEnumerable<MemoryRecord> records = new List<MemoryRecord>(numRecords);
        for (var i = 0; i < numRecords / 2; i++)
        {
            var testRecord = MemoryRecord.LocalRecord(
                id: "test" + i,
                text: "text" + i,
                description: "description" + i,
                embedding: new float[] { 1, 1, 1 });
            records = records.Append(testRecord);
        }

        for (var i = numRecords / 2; i < numRecords; i++)
        {
            var testRecord = MemoryRecord.ReferenceRecord(
                externalId: "test" + i,
                sourceName: "sourceName" + i,
                description: "description" + i,
                embedding: new float[] { 1, 2, 3 });
            records = records.Append(testRecord);
        }

        return records;
    }
}
