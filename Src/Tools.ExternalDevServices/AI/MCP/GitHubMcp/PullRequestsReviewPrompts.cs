using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace Tools.ExternalDevServices.AI.MCP.GitHubMcp;

[McpServerPromptType]
public static class PullRequestsReviewPrompts
{
    [McpServerPrompt(Name = "specific-pull-request-review-instructions-prompt"),
     Description("Optimized prompt for reviewing a specific pull request")]
    public static async Task<string> GetSpecificPullRequestReviewInstructionsAsync(string repo, int pullRequest)
    {
        var prompt = await File.ReadAllTextAsync(@"Resources\Prompts\SpecificPullRequestReviewInstructions.prompt.md");
        return prompt.Replace("[$$REPO$$]", repo.Trim()).Replace("[$$PR$$]", pullRequest.ToString());
    }

    [McpServerPrompt(Name = "general-pull-request-review-instructions-prompt"),
     Description("Optimized prompt for reviewing pull request")]
    public static async Task<string> GetGeneralPullRequestReviewInstructionsAsync() =>
        await File.ReadAllTextAsync(@"Resources\Prompts\GeneralPullRequestReviewInstructions.prompt.md");

    // For now no need to expose this via MCP, but it's here for reference.
    public static async Task<string> ElicitPullRequestReviewInstructionsAsync(McpServer mcpServer)
    {
        if (mcpServer.ClientCapabilities?.Elicitation is null)
            return await GetGeneralPullRequestReviewInstructionsAsync();

        var schema = new ElicitRequestParams.RequestSchema
        {
            Properties =
            {
                ["repository"] = new ElicitRequestParams.StringSchema
                {
                    Description = "Repository name (e.g. org/repo or repo)"
                },
                ["pullRequestNumber"] = new ElicitRequestParams.NumberSchema
                {
                    Description = "Pull request number (integer)"
                }
            }
        };

        // Ask the client (host UI) to collect the values:
        var elicitResult = await mcpServer.ElicitAsync(
            new ElicitRequestParams
            {
                Message = "Please provide the repository and pull request number.",
                RequestedSchema = schema
            },
            CancellationToken.None
        );

        if (elicitResult.Action != "accept" || elicitResult.Content is null)
        {
            return ""; // User canceled or closed the UI.
        }

        // Read and validate:
        var repo = elicitResult.Content.TryGetValue("repository", out var repoEl) &&
                   repoEl.ValueKind == JsonValueKind.String
            ? repoEl.GetString()
            : throw new McpException("Missing 'repository'.");
        if (string.IsNullOrWhiteSpace(repo))
            throw new McpException("Repository cannot be empty.");

        var pullRequest = elicitResult.Content.TryGetValue("pullRequestNumber", out var prEl) &&
                          prEl.ValueKind == JsonValueKind.Number
            ? prEl.GetInt32()
            : throw new McpException("Missing 'pullRequestNumber'.");

        return await GetSpecificPullRequestReviewInstructionsAsync(repo, pullRequest);
    }
}