using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V1;
using Tools.ExternalDevServices.Integrations.GitHub;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Flows.GitHub.V1;

public class GitHubPullRequestFilesClassifierFlow
{
    private readonly IChatClient _chatClient;
    private readonly ILogger? _logger;

    private static readonly Dictionary<(string, int), (PullRequestFilesClassificationResponse pullRequestFilesClassificationResponse, DateTime? lastUserCommitDate)> Cache = new();

    public GitHubPullRequestFilesClassifierFlow(IChatClient chatClient,
        ILogger? logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<PullRequestFilesClassificationResponse> ClassifyPullRequestFilesAsync(GitHubRestApiClient gitHubRestApiClient, string repo, int pullRequestNumber, int? maxContextSizeInTokens, bool addExcludedFilesInternalMessage, DiagnosticsHelper? diagnostics)
    {
        try
        {
            diagnostics?.AddInformation($"Getting pull request {pullRequestNumber} review...");
            var pullRequestDetails = await gitHubRestApiClient.GetPullRequestDetailsAsync(repo, pullRequestNumber);

            return await ClassifyPullRequestFilesAsync(gitHubRestApiClient, pullRequestDetails, maxContextSizeInTokens,
                addExcludedFilesInternalMessage, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics?.AddError($"**Error getting pull request {pullRequestNumber} review:**\r\n{ex}");
            throw;
        }
    }

    internal async Task<PullRequestFilesClassificationResponse> ClassifyPullRequestFilesAsync(GitHubRestApiClient gitHubRestApiClient, PullRequestDetails pullRequestDetails, int? maxContextSizeInTokens, bool addExcludedFilesInternalMessage, DiagnosticsHelper? diagnostics)
    {
        if (Cache.TryGetValue((pullRequestDetails.Repo.Trim().ToLower(), pullRequestDetails.Number), out var cachedResult) &&
                cachedResult.lastUserCommitDate >= pullRequestDetails.Commits.LastOrDefault()?.Date)
        {
            diagnostics?.AddInformation("Returning diffs from cache");
            return cachedResult.pullRequestFilesClassificationResponse;
        }

        var classifierAgent = new PullRequestFilesClassifierAgent(gitHubRestApiClient, _chatClient, diagnostics);
        var pullRequestFilesClassification = await classifierAgent.ClassifyPullRequestFilesAsync(pullRequestDetails, maxContextSizeInTokens);
        diagnostics?.AddInformation(diagnosticsFileOnly: true, message:
            $"{ClassifiedFile.ToClassificationString(pullRequestFilesClassification.ClassifiedFiles, includeClassificationDetails: true, includeDiffLevel: DiffChangeType.None)}");

        diagnostics?.WriteContentToSeparateFile("FilesClassification.md",
            $"""
            # {pullRequestFilesClassification.PullRequest.Title} #{pullRequestFilesClassification.PullRequest.Number}
            **Author**: {pullRequestFilesClassification.PullRequest.User}
            **Description**: {pullRequestFilesClassification.PullRequest.StripDevopsCommentFromBody()}
            
            ---
            
            # Files Classifications:
            {ClassifiedFile.ToClassificationString(pullRequestFilesClassification.ClassifiedFiles, includeClassificationDetails:true, includeDiffLevel: DiffChangeType.Any)}
            """
            );
            

        Cache[(pullRequestDetails.Repo.Trim().ToLower(), pullRequestDetails.Number)] = (pullRequestFilesClassification,
            pullRequestDetails.Commits.LastOrDefault()?.Date);
        return pullRequestFilesClassification;
    }
}