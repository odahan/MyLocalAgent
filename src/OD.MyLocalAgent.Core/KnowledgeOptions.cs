namespace OD.MyLocalAgent.Core;

/// <summary>Defines bounded embedding, chunking, import, and retrieval settings.</summary>
public sealed record KnowledgeOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "Knowledge";
    /// <summary>Gets the independent embedding provider.</summary>
    public string EmbeddingProvider { get; init; } = "Ollama";
    /// <summary>Gets the independent embedding model.</summary>
    public string EmbeddingModel { get; init; } = "embeddinggemma:300m";
    /// <summary>Gets the embedding batch size.</summary>
    public int EmbeddingBatchSize { get; init; } = 16;
    /// <summary>Gets the embedding timeout.</summary>
    public int EmbeddingTimeoutSeconds { get; init; } = 120;
    /// <summary>Gets the target chunk size in estimated tokens.</summary>
    public int TargetChunkTokens { get; init; } = 384;
    /// <summary>Gets the maximum chunk size in estimated tokens.</summary>
    public int MaximumChunkTokens { get; init; } = 512;
    /// <summary>Gets the overlap in estimated tokens.</summary>
    public int OverlapChunkTokens { get; init; } = 48;
    /// <summary>Gets the default search result count.</summary>
    public int DefaultMaxResults { get; init; } = 5;
    /// <summary>Gets the maximum search result count.</summary>
    public int MaximumResults { get; init; } = 20;
    /// <summary>Gets the default result token budget.</summary>
    public int DefaultMaximumResultTokens { get; init; } = 2048;
    /// <summary>Gets the hard result token budget ceiling.</summary>
    public int MaximumResultTokens { get; init; } = 8192;
    /// <summary>Gets the minimum accepted cosine similarity.</summary>
    public float MinimumSimilarity { get; init; } = 0.25f;
    /// <summary>Gets the maximum imported byte count.</summary>
    public long MaximumImportBytes { get; init; } = 104857600;
    /// <summary>Gets the maximum extracted character count.</summary>
    public int MaximumExtractedCharacters { get; init; } = 1000000;
    /// <summary>Gets the maximum chunks in one document.</summary>
    public int MaximumChunksPerDocument { get; init; } = 4096;
    /// <summary>Gets the maximum query length.</summary>
    public int MaximumQueryCharacters { get; init; } = 4000;
    /// <summary>Gets the maximum excerpt length.</summary>
    public int MaximumExcerptCharacters { get; init; } = 4000;
    /// <summary>Gets the maximum serialized tool result length.</summary>
    public int MaximumToolResultCharacters { get; init; } = 64000;

    /// <summary>Returns whether all configured bounds are internally consistent.</summary>
    public static bool IsValid(KnowledgeOptions options) =>
        !string.IsNullOrWhiteSpace(options.EmbeddingProvider) &&
        !string.IsNullOrWhiteSpace(options.EmbeddingModel) &&
        options.EmbeddingBatchSize is >= 1 and <= 128 &&
        options.EmbeddingTimeoutSeconds is >= 1 and <= 600 &&
        options.TargetChunkTokens >= 32 &&
        options.MaximumChunkTokens >= options.TargetChunkTokens &&
        options.OverlapChunkTokens >= 0 && options.OverlapChunkTokens < options.TargetChunkTokens &&
        options.DefaultMaxResults >= 1 && options.DefaultMaxResults <= options.MaximumResults &&
        options.MaximumResults is >= 1 and <= 20 &&
        options.DefaultMaximumResultTokens >= 128 &&
        options.DefaultMaximumResultTokens <= options.MaximumResultTokens &&
        options.MaximumResultTokens is >= 128 and <= 32768 &&
        options.MinimumSimilarity is >= -1 and <= 1 &&
        options.MaximumImportBytes > 0 && options.MaximumExtractedCharacters > 0 &&
        options.MaximumChunksPerDocument > 0 && options.MaximumQueryCharacters > 0 &&
        options.MaximumExcerptCharacters > 0 && options.MaximumToolResultCharacters >= 4096;
}
