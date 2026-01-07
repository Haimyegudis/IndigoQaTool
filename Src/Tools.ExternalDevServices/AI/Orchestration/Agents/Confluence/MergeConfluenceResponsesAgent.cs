using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Tools.ExternalDevServices.AI.Orchestration.Agents.Confluence;

public class MergeConfluenceResponsesAgent
{
    private readonly IChatClient _chatClient;
    private readonly ILogger? _logger;

    public MergeConfluenceResponsesAgent(IChatClient chatClient,
        ILogger? logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<string> MergeResponsesAsync(string query, IReadOnlyCollection<string> singleDocumentResponses)
    {
        ChatMessage[] fineTuneRequestMessages =
        [
            new(ChatRole.System,
                """
                 You are a technical writer, excelling in crafting a unified answer to a user's query/request from a partial answers taken from several documents.
                 
                 You must follow these rules strictly:
                 1. Do not include any conversational phrasing, meta-commentary, or references to how the answer was generated.
                 2. If no document contains relevant information, state clearly and formally: 
                    "This question cannot be answered based on the available content."
                 3. If a document contains partial or insufficient information, mention it inline (e.g., in parentheses), but do not give it its own markdown heading.
                 4. At the end, include a markdown-formatted list of all relevant sources — even if mentioned earlier — with each entry showing:
                    - Title
                    - URL
                    - Last updated date
                    - Similarity rank  
                    Format each source as:  
                    `- **Title** — [URL](URL) — Last updated: YYYY-MM-DD — Similarity rank: X.XX`

                 Task:
                 Refine the draft answers from multiple documents into a single, clear, cohesive narrative that reads as if taken directly from a Confluence document.

                 Requirements:
                 - Merge all relevant content into one continuous, well-structured response.
                 - Maintain or add markdown formatting (`code blocks`, **bold**, _italics_, lists, headings, etc.) where appropriate.
                 - Use a formal, documentation-style tone (neutral, precise, technical) with no summaries or framing.
                 - Do not omit relevant details from the drafts.
                 - Do not explain or describe the merging process.
                 """),
            new (ChatRole.User, 
                $"""
                 **Request/question**:
                 {query}
                 
                 **Draft answers from documents**:
                 {string.Join("\r\n\r\n", singleDocumentResponses)}
                 """)
        ];

        var sw = Stopwatch.StartNew();
        var answer = (await _chatClient.GetResponseAsync(fineTuneRequestMessages)).Text;
        sw.Stop();

        _logger?.LogInformation($"Merged response received after {sw.Elapsed}.");
        return answer;
    }
}