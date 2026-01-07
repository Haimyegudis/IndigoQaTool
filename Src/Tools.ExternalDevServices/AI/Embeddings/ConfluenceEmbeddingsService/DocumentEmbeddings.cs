using Microsoft.Extensions.VectorData;

namespace Tools.ExternalDevServices.AI.Embeddings.ConfluenceEmbeddingsService;

public class DocumentEmbeddings
{
    [VectorStoreKey]
    public string Sampling { get; set; } = "";

    [VectorStoreData]
    public long Timestamp { get; set; }

    public DateTimeOffset TimestampAsDate => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp);

    [VectorStoreData]
    public string DocumentId { get; set; } = "";

    [VectorStoreData]
    public string DocumentTitle { get; set; } = "";

    [VectorStoreData]
    public string DocumentFolder { get; set; } = "";

    [VectorStoreData]
    public string DocumentMarkdownFilePath { get; set; } = "";

    [VectorStoreVector(Dimensions: 1024, DistanceFunction = DistanceFunction.CosineDistance)]
    public ReadOnlyMemory<float>? SamplingEmbedding { get; set; }
}