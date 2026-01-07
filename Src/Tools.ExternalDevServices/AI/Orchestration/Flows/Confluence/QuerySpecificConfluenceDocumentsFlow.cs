using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tools.ExternalDevServices.AI.Orchestration.Agents.Confluence;
using Tools.ExternalDevServices.Integrations.Confluence;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Flows.Confluence;

public class QuerySpecificConfluenceDocumentsFlow
{
    private readonly ILogger? _logger;

    private readonly ConfluenceSpecificDocumentQueryAgent _confluenceSpecificDocumentQueryAgent;
    private readonly MergeConfluenceResponsesAgent _mergeConfluenceResponsesAgent;

    public QuerySpecificConfluenceDocumentsFlow(IChatClient chatClient,
        ConfluenceRestApiClient confluenceRestApiClient,
        ILogger? logger)
    {
        _logger = logger;

        _confluenceSpecificDocumentQueryAgent = new ConfluenceSpecificDocumentQueryAgent(confluenceRestApiClient, chatClient, logger);
        _mergeConfluenceResponsesAgent = new MergeConfluenceResponsesAgent(chatClient, logger);
    }

    public async Task<string> QueryConfluenceAsync(string query, IReadOnlyCollection<string> documentsUrls,
        CancellationToken ct)
    {
        using var diagnostics = new DiagnosticsHelper(GetType(), _logger);

        try
        {
            var specificDocumentQueryResponses = await Task.WhenAll(
                documentsUrls.Select(async url =>
                    {
                        var specificDocumentQueryResponse =
                            await _confluenceSpecificDocumentQueryAgent.QueryDocumentByUrlAsync(query, url, ct);
                        // ReSharper disable once AccessToDisposedClosure
                        diagnostics.AddInformation(
                            $"**Specific Confluence document query response for {url}**:\r\n{specificDocumentQueryResponse}");
                        return specificDocumentQueryResponse;
                    }));

            var mergedResponse =
                await _mergeConfluenceResponsesAgent.MergeResponsesAsync(query, specificDocumentQueryResponses);
            diagnostics.AddInformation($"**Merged response:**\r\n{mergedResponse}");

            return mergedResponse;
        }
        catch (Exception ex)
        {
            diagnostics.AddError($"**Error querying Confluence documents:**\r\n{ex}");
            return "Query Confluence failed, check diagnostics file for more information.";
        }
    }
}