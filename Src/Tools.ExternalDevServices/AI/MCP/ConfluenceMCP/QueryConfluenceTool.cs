using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Tools.ExternalDevServices.AI.Embeddings.ConfluenceEmbeddingsService;
using Tools.ExternalDevServices.AI.Orchestration.Flows.Confluence;
using Tools.ExternalDevServices.Integrations.Confluence;

namespace Tools.ExternalDevServices.AI.MCP.ConfluenceMCP;

[McpServerToolType, McpServerPromptType]
public static class QueryConfluenceTool
{
    public const string ConfluenceUrl = "https://v-indigo-confluence.inr.rd.hpicorp.net:6443";
    public const string OllamaHostUri = "http://localhost:11434";
    public const string EmbeddingModelId = "mxbai-embed-large:335m";

    [McpServerPrompt(Name = "confluence_prompt"),
     Description("Prompt for chatting with Confluence S6 SW Requirements documents")]
    public static ChatMessage SessionInstructionsPrompt() =>
        new(ChatRole.User,
            """
            We are starting a session where you must answer questions using information from Confluence documents.

            Follow these rules:

            1. **Session Flow**  
               - At the **start of the session**, **after** the user requested information or asked a question, you have no context — you **must call the Query Confluence tool** using the **user's prompt exactly as-is** (do not rephrase).  
               - Treat **all following questions as follow-up questions**, unless I **explicitly indicate** that we are starting a **new topic**. Only I can reset the context by clearly stating that we are beginning a new topic or session.

            2. **Tool Use**  
               - If you **don't have enough context**, use the **Query Confluence tool** to fetch relevant documents.  
               - If it's a **follow-up question** and you lack enough context, **rephrase it** using relevant known context, then use the tool.  
               - When rephrasing, **keep the question as short as possible**, while still clear enough to retrieve relevant documents.  
               - If a prompt can be answered using one or more documents you already have **URIs for**, you may use the **Query Specific Confluence Documents tool** to query only those documents.  
               - If you need the **full Markdown content** of a known document, you may use the **Get Confluence Document Markdown tool**, providing its URI.

            3. **When to Answer Directly**  
               - If you **have enough context**, answer directly without using the tool.  
               - If you're **unsure** whether it's a follow-up, ask the user. If confirmed and you have enough information, answer directly.

            4. **After Tool Response**  
               - If document similarity scores are **low**, inform the user and suggest refining the question.  
               - When answering using tool results, include **hyperlinked document titles**. Next to each link, display the **actual similarity score** (e.g., `Rank: 0.87`) in **descending order** if it was specified.  
               - Do **not** include the rank value inside the hyperlink.

            Understood?
            """);


    [McpServerTool,
     Description(
         """
         Gets the response for the user prompt about Confluence content (e.g. requirements, feature explanations, user stories etc.).
         User prompt should be passed as-is for best matches.
         """)]
    public static async Task<string> QueryConfluenceAsync(McpServer mcpServer, string query)
    {
        ValidateForSampling(mcpServer, out var personalAccessToken, out var series, out var samplingChatClient);
        
        var loggerFactory = mcpServer.Services?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        var logger = loggerFactory?.CreateLogger(nameof(QueryConfluenceTool));

        using var confluenceRestApiClient = new ConfluenceRestApiClient(ConfluenceUrl, personalAccessToken);
        using var softwareRequirementsEmbeddingsService =
            new SoftwareRequirementsEmbeddingsService("EmbeddingsDB.db", EmbeddingGeneratorType.Ollama, OllamaHostUri, EmbeddingModelId, samplingChatClient);
        
        var flow = new QueryConfluenceForBestMatchingDocumentsFlow(
            samplingChatClient,
            confluenceRestApiClient,
            softwareRequirementsEmbeddingsService,
            logger);

        return await flow.QueryConfluenceAsync(series, query, CancellationToken.None);
    }

    [McpServerTool, Description("Gets the markdown version for the specified Confluence document.")]
    public static async Task<string> GetConfluenceDocumentMarkdownVersionAsync(string documentsUri)
    {
        Validate(out var personalAccessToken, out _);
        using var confluenceRestApiClient = new ConfluenceRestApiClient(ConfluenceUrl, personalAccessToken);

        return await confluenceRestApiClient.GetDocumentAsMarkdownByUrlAsync(documentsUri);
    }

    [McpServerTool, Description("Gets the Html version for the specified Confluence document.")]
    public static async Task<string> GetConfluenceDocumentHtmlVersionAsync(string documentsUri)
    {
        Validate(out var personalAccessToken, out _);
        using var confluenceRestApiClient = new ConfluenceRestApiClient(ConfluenceUrl, personalAccessToken);

        return await confluenceRestApiClient.GetDocumentHtmlByUrlAsync(documentsUri);
    }

    [McpServerTool,
     Description(
         """
         Gets the response for the user prompt using specific Confluence documents (e.g. requirements, feature explanations, user stories etc.).
         User prompt should be passed as-is for best matches.
         """)]
    public static async Task<string> QuerySpecificConfluenceDocumentsAsync(McpServer mcpServer, string prompt,
        string[] documentsUris)
    {
        ValidateForSampling(mcpServer, out var personalAccessToken, out _, out var samplingChatClient);

        var loggerFactory = mcpServer.Services?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        var logger = loggerFactory?.CreateLogger(nameof(QueryConfluenceTool));

        using var confluenceRestApiClient = new ConfluenceRestApiClient(ConfluenceUrl, personalAccessToken);

        var flow = new QuerySpecificConfluenceDocumentsFlow(
            samplingChatClient,
            confluenceRestApiClient,
            logger);

        return await flow.QueryConfluenceAsync(prompt, documentsUris, CancellationToken.None);
    }

    private static void ValidateForSampling(McpServer mcpServer,
        out string personalAccessToken,
        out string series,
        out IChatClient samplingChatClient)
    {
        Validate(out personalAccessToken, out series);

        if (mcpServer.ClientCapabilities?.Sampling is null)
        {
            throw new McpException("Sampling is not supported.");
        }

        samplingChatClient = mcpServer.AsSamplingChatClient();
        if (samplingChatClient is null)
        {
            throw new McpException("Sampling is not supported.");
        }
    }

    private static void Validate(out string personalAccessToken, out string series)
    {
        personalAccessToken = "";
        series = "";

        if (string.IsNullOrWhiteSpace(HostArguments.PersonalAccessToken))
        {
            throw new InvalidOperationException("PersonalAccessToken is not set.");
        }
        personalAccessToken = HostArguments.PersonalAccessToken;

        if (string.IsNullOrWhiteSpace(HostArguments.Series))
        {
            throw new InvalidOperationException("Series is not set.");
        }
        series = HostArguments.Series;
    }
}