using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tools.ExternalDevServices.AI.Embeddings.ConfluenceEmbeddingsService;

namespace Tools.ExternalDevServices.AI.Orchestration.Agents.Confluence;

public class ConfluenceEmbeddingsQueryAgent
{
    private readonly SoftwareRequirementsEmbeddingsService _softwareRequirementsEmbeddingsService;
    private readonly IChatClient _chatClient;
    private readonly ILogger? _logger;

    public ConfluenceEmbeddingsQueryAgent(SoftwareRequirementsEmbeddingsService softwareRequirementsEmbeddingsService,
        IChatClient chatClient, 
        ILogger? logger)
    {
        _softwareRequirementsEmbeddingsService = softwareRequirementsEmbeddingsService;
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<ConfluenceQueryEmbeddingsResponse> QueryConfluenceAsync(string series, string query, CancellationToken ct, double similarityThreshold = 0.6)
    {
        var queryOptimizationForEmbeddingMessage = new ChatMessage(ChatRole.User, $"""
             Your response will be used directly as the embedding text. 
             Return **only** the optimized query text — no extra words, labels, punctuation, quotes, markdown, or explanations.

             Optimize the query below for semantic embeddings to retrieve the most relevant Confluence documents.

             Rules:
             - Keep only meaningful search terms: key phrases, domain-specific keywords, product/service names, acronyms, versions, error codes, component/module names, page titles.
             - Remove filler or irrelevant words: stopwords, politeness, pronouns, hedging, instructions, formatting artifacts.
             - Do not add, change, or translate terms; preserve original casing for names.
             - Deduplicate similar terms; keep the canonical form.
             - Output must be a single concise phrase with no extra characters or formatting.

             Input query:
             {query}
             """);

        var queryOptimizationForEmbeddingResponse =
            await _chatClient.GetResponseAsync(queryOptimizationForEmbeddingMessage, cancellationToken: ct);
        var embeddings =
            await _softwareRequirementsEmbeddingsService.FindBestDocumentsForPromptBySimilarityThreshold(
                series, query, similarityThreshold, _logger);
        _logger?.LogInformation(
            $"Prompt optimization for prompt :'{query}': '{queryOptimizationForEmbeddingResponse.Text}'");
        _logger?.LogInformation(
            $"Total {embeddings.Count} embeddings with similarity above {similarityThreshold} for optimized prompt: {queryOptimizationForEmbeddingResponse.Text}");

        return embeddings.Count switch
        {
            0 => new ConfluenceQueryEmbeddingsResponse(queryOptimizationForEmbeddingResponse.Text, []),
            _ => ExtractAnswerFromTopMatchingDocuments(query, queryOptimizationForEmbeddingResponse.Text, embeddings)
        };
    }

    private ConfluenceQueryEmbeddingsResponse ExtractAnswerFromTopMatchingDocuments(string query,
        string promptOptimizationForEmbedding, IReadOnlyCollection<DocumentEmbeddingsResponse> embeddings)
    {
        _logger?.LogInformation(
            $"Best match for prompt \"{query}\":\r\nDocument: {Path.GetFileNameWithoutExtension(embeddings.First().DocumentEmbeddings.DocumentMarkdownFilePath)}\r\nGrade: {embeddings.First().Similarity}");
        _logger?.LogInformation(
            $"Worst match for prompt \"{query}\":\r\nDocument: {Path.GetFileNameWithoutExtension(embeddings.Last().DocumentEmbeddings.DocumentMarkdownFilePath)}\r\nGrade: {embeddings.Last().Similarity}");

        return new ConfluenceQueryEmbeddingsResponse(promptOptimizationForEmbedding, embeddings
            .GroupBy(e => e.DocumentEmbeddings.DocumentId)
            .OrderByDescending(g => g.First().Similarity)
            .Select(g => g.First()).ToArray());
    }
}