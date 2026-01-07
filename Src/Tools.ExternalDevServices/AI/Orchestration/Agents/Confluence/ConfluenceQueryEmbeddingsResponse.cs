using Tools.ExternalDevServices.AI.Embeddings.ConfluenceEmbeddingsService;

namespace Tools.ExternalDevServices.AI.Orchestration.Agents.Confluence;

public record ConfluenceQueryEmbeddingsResponse(
    string PromptOptimizationForEmbedding,
    IReadOnlyCollection<DocumentEmbeddingsResponse> DocumentEmbeddings);