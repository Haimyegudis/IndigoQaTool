using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tools.ExternalDevServices.AI.Embeddings.ConfluenceEmbeddingsService;
using Tools.ExternalDevServices.Integrations.Confluence;

namespace Tools.ExternalDevServices.AI.Orchestration.Agents.Confluence;

public class ConfluenceSpecificDocumentQueryAgent
{
    private readonly ConfluenceRestApiClient _confluenceRestApiClient;
    private readonly IChatClient _chatClient;
    private readonly ILogger? _logger;

    public ConfluenceSpecificDocumentQueryAgent(ConfluenceRestApiClient confluenceRestApiClient,
        IChatClient chatClient,
        ILogger? logger)
    {
        _confluenceRestApiClient = confluenceRestApiClient;
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<string> QueryDocumentFromEmbeddingsAsync(string query, DocumentEmbeddingsResponse documentEmbeddingsResponse, CancellationToken ct)
    {
        _logger?.LogInformation($"Fetching markdown for document '{documentEmbeddingsResponse.DocumentEmbeddings.DocumentTitle}'...");
        var (metadata, markdown) =
            await _confluenceRestApiClient.GetDocumentMetadataAndMarkdownByPageIdAsync(documentEmbeddingsResponse
                .DocumentEmbeddings.DocumentId);
        _logger?.LogInformation($"Fetching markdown for document '{documentEmbeddingsResponse.DocumentEmbeddings.DocumentTitle}' completed.");
        
        var sw = Stopwatch.StartNew();

        var response = await QueryDocumentAsync(query, metadata, markdown, ct);
        response = string.IsNullOrWhiteSpace(response)
            ? response
            : $"Document: {metadata.Title}\r\nSimilarity Rank: {documentEmbeddingsResponse.Similarity}\r\nLast Embeddings Date: {documentEmbeddingsResponse.DocumentEmbeddings.TimestampAsDate}\r\nProcessing: {sw.Elapsed}\r\nUri: {new Uri(new Uri(_confluenceRestApiClient.ConfluenceUrl, UriKind.Absolute), new Uri(metadata.Links.WebUI, UriKind.Relative))}\r\n\r\nAnswer from '{metadata.Title}' document:\r\n{response}\r\n\r\n========================================";
        sw.Stop();

        _logger?.LogInformation(
            $"Response for document {documentEmbeddingsResponse.DocumentEmbeddings.DocumentId} '{documentEmbeddingsResponse.DocumentEmbeddings.DocumentTitle}' received after {sw.Elapsed}.");
        _logger?.LogInformation($"**Response:**\r\n{response}");
        return response;
    }

    public async Task<string> QueryDocumentByUrlAsync(string query, string documentUrl, CancellationToken ct)
    {
        _logger?.LogInformation($"Fetching markdown for document {documentUrl}...");
        var (metadata, markdown) =
            await _confluenceRestApiClient.GetDocumentMetadataAndMarkdownByUrlAsync(documentUrl);
        _logger?.LogInformation($"Fetching markdown for document {documentUrl} '{metadata.Title}' completed.");

        var sw = Stopwatch.StartNew();

        var response = await QueryDocumentAsync(query, metadata, markdown, ct);
        response = string.IsNullOrWhiteSpace(response)
            ? response
            : $"Document: {metadata.Title}\r\nProcessing: {sw.Elapsed}\r\nUri: {documentUrl}\r\n\r\nAnswer from '{metadata.Title}' document:\r\n{response}\r\n\r\n========================================";
        sw.Stop();

        _logger?.LogInformation(
            $"Response for document {documentUrl} '{metadata.Title}' received after {sw.Elapsed}.");
        _logger?.LogInformation($"**Response:**\r\n{response}");
        return response;
    }

    public async Task<string> QueryDocumentByPageIdAsync(string query, string pageId, CancellationToken ct)
    {
        _logger?.LogInformation($"Fetching markdown for document {pageId}...");
        var (metadata, markdown) =
            await _confluenceRestApiClient.GetDocumentMetadataAndMarkdownByPageIdAsync(pageId);
        _logger?.LogInformation($"Fetching markdown for document {pageId} '{metadata.Title}' completed.");

        var sw = Stopwatch.StartNew();

        var response = await QueryDocumentAsync(query, metadata, markdown, ct);
        response = string.IsNullOrWhiteSpace(response)
            ? response
            : $"Document: {metadata.Title}\r\nProcessing: {sw.Elapsed}\r\nUri: {new Uri(new Uri(_confluenceRestApiClient.ConfluenceUrl, UriKind.Absolute), new Uri(metadata.Links.WebUI, UriKind.Relative))}\r\n\r\nAnswer from '{metadata.Title}' document:\r\n{response}\r\n\r\n========================================";
        sw.Stop();

        _logger?.LogInformation(
            $"Response for document {pageId} '{metadata.Title}' received after {sw.Elapsed}.");
        _logger?.LogInformation($"**Response:**\r\n{response}");
        return response;
    }

    private async Task<string> QueryDocumentAsync(string query, ConfluenceDocumentMetadata metadata, string markdown, CancellationToken ct)
    {
        ChatMessage[] summaryRequestChatMessages =
        [
            new(ChatRole.System,
                $"""
                 You are a technical writer, excelling is answering user queries based on the document content.
                 You will be given a user request/question and a document to answer the user query.
                 
                 You must follow these rules strictly:
                 1. If the document does not contain enough directly relevant information to fully answer the question, respond with exactly: ""
                 2. Do not include any content that is not explicitly present in the document.
                 3. Do not infer, assume, guess, speculate, or use external knowledge.
                 4. Your answer must contain only the response and verbatim citations — no extra commentary, labels, or formatting.

                 Answer requirements:
                 - Be clear and concise.
                 - Address only the exact user request/question.
                 - Use only information explicitly stated in the document.
                 - Include verbatim citations from the document that directly support the answer. 
                   • Citations must be exact sentences copied from the document without alteration.
                   • Include only sentences that directly answer the question — no paraphrasing.
                 """)
            ,new (ChatRole.User, 
                $"""
                Task:
                Based on the document titled '{metadata.Title}', answer the request/question below.
                
                **User request/question**:
                {query}
                
                **Document (Markdown format)**:
                {markdown}
                """)
        ];

        return (await _chatClient.GetResponseAsync(summaryRequestChatMessages, cancellationToken: ct))
            .Text.Trim().Trim('\"');
    }
}