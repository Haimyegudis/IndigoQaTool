using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tools.ExternalDevServices.AI.Embeddings.ConfluenceEmbeddingsService;
using Tools.ExternalDevServices.AI.Orchestration.Agents.Confluence;
using Tools.ExternalDevServices.Integrations.Confluence;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Flows.Confluence;

public class QueryConfluenceForBestMatchingDocumentsFlow
{
    private readonly ILogger? _logger;
    
    private readonly ConfluenceEmbeddingsQueryAgent _confluenceEmbeddingsQueryAgent;
    private readonly ConfluenceSpecificDocumentQueryAgent _confluenceSpecificDocumentQueryAgent;
    private readonly MergeConfluenceResponsesAgent _mergeConfluenceResponsesAgent;

    public QueryConfluenceForBestMatchingDocumentsFlow(IChatClient chatClient, 
        ConfluenceRestApiClient confluenceRestApiClient,
        SoftwareRequirementsEmbeddingsService softwareRequirementsEmbeddingsService, 
        ILogger? logger)
    {
        _logger = logger;
        _confluenceEmbeddingsQueryAgent = new ConfluenceEmbeddingsQueryAgent(softwareRequirementsEmbeddingsService, chatClient, logger);
        _confluenceSpecificDocumentQueryAgent = new ConfluenceSpecificDocumentQueryAgent(confluenceRestApiClient, chatClient, logger);
        _mergeConfluenceResponsesAgent = new MergeConfluenceResponsesAgent(chatClient, logger);
    }

    public async Task<string> QueryConfluenceAsync(string series, string query, CancellationToken ct)
    {
        using var diagnostics = new DiagnosticsHelper(GetType(), _logger);

        try
        {
            diagnostics.AddInformation($"**Query:**\r\n{query}");

            var seriesOccurrences = SeriesHelper.GetSeriesOccurrences(series).ToArray();
            diagnostics.AddInformation($"**Series:**\r\n{string.Join(", ", seriesOccurrences)}");

            return seriesOccurrences.Length switch
            {
                0 => "No series specified",
                1 => await QueryConfluenceForSingleSeriesAsync(seriesOccurrences[0], query, diagnostics, ct),
                _ => await QueryConfluenceForMultipleSeriesAsync(seriesOccurrences, query, diagnostics, ct)
            };
        }
        catch (Exception ex)
        {
            diagnostics.AddError($"**Error querying Confluence embeddings:**\r\n{ex}");
            return "Query Confluence failed, check diagnostics file for more information.";
        }
    }

    private async Task<string> QueryConfluenceForMultipleSeriesAsync(string[] seriesOccurrences, string query, DiagnosticsHelper diagnostics, CancellationToken ct)
    {
        var seriesResponses = await Task.WhenAll(
            seriesOccurrences.Select(async seriesOccurrence =>
                await QueryConfluenceForSingleSeriesAsync(seriesOccurrence, query, diagnostics, ct)));

        return string.Join("\r\n\r\n",
            seriesOccurrences.Select((seriesOccurrence, index) =>
                $"**Response for series [{seriesOccurrence}]:**\r\n{seriesResponses[index]}"));
    }

    private async Task<string> QueryConfluenceForSingleSeriesAsync(string seriesOccurrence, string query, DiagnosticsHelper diagnostics, CancellationToken ct)
    {
        var embeddingsResponse = await _confluenceEmbeddingsQueryAgent.QueryConfluenceAsync(seriesOccurrence, query, ct);
        diagnostics.AddInformation(
            $"**Optimized prompt for embeddings**:\r\n{embeddingsResponse.PromptOptimizationForEmbedding}");
        if (embeddingsResponse.DocumentEmbeddings.Count == 0)
        {
            diagnostics.AddInformation("**No embeddings found.**");
            return "No relevant documents found.";
        }
        else
        {
            diagnostics.AddInformation(
                $"**{embeddingsResponse.DocumentEmbeddings.Count} Embeddings found:**\r\n{string.Join("\r\n", embeddingsResponse.DocumentEmbeddings.Select(x => x.DocumentEmbeddings.DocumentTitle))}");
        }

        var specificDocumentQueryResponses = await Task.WhenAll(
            embeddingsResponse.DocumentEmbeddings.OrderByDescending(e => e.Similarity)
                .Select(async documentEmbeddingsResponse =>
                {
                    var specificDocumentQueryResponse =
                        await _confluenceSpecificDocumentQueryAgent.QueryDocumentFromEmbeddingsAsync(query, documentEmbeddingsResponse, ct);
                    // ReSharper disable once AccessToDisposedClosure
                    diagnostics.AddInformation(
                        $"**Specific document query response for {documentEmbeddingsResponse.DocumentEmbeddings.DocumentTitle}**:\r\n{specificDocumentQueryResponse}");
                    return specificDocumentQueryResponse;
                }));

        var mergedResponse =
            await _mergeConfluenceResponsesAgent.MergeResponsesAsync(query, specificDocumentQueryResponses);
        diagnostics.AddInformation($"**Merged response:**\r\n{mergedResponse}");

        return mergedResponse;
    }
}