using System.Numerics;

namespace SemanticKernel.Connectors.Oracle;

/// <summary>
/// An oracle memory entry.
/// </summary>
public record struct OracleMemoryEntry
{
    /// <summary>
    /// Unique identifier of the memory entry.
    /// </summary>
    public string Key { get; set; }

    /// <summary>
    /// Attributes as a string.
    /// </summary>
    public string MetadataString { get; set; }

    /// <summary>
    /// The embedding data.
    /// </summary>
    public float[] Embedding { get; set; }

    /// <summary>
    /// Optional timestamp. Its 'DateTimeKind' is <see cref="DateTimeKind.Utc"/>
    /// </summary>
    public DateTime? Timestamp { get; set; }
}