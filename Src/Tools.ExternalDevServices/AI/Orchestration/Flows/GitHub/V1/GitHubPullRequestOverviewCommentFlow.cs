using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V1;
using Tools.ExternalDevServices.AI.Orchestration.Utils;
using Tools.ExternalDevServices.Integrations.GitHub;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Flows.GitHub.V1;

public class GitHubPullRequestOverviewCommentFlow
{
    private readonly IChatClient _chatClient;
    private readonly ILogger? _logger;

    private static readonly Dictionary<(string, int), (PullRequestOverviewCommentResponse pullRequestOverviewCommentResponse, DateTime? lastUserCommitDate)> Cache = new();

    public GitHubPullRequestOverviewCommentFlow(
        IChatClient chatClient,
        ILogger? logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<PullRequestOverviewCommentResponse> GeneratePullRequestOverviewCommentAsync(GitHubRestApiClient gitHubRestApiClient, string repo, int pullRequestNumber, int? maxContextSizeInTokens)
    {
        using var diagnostics = new DiagnosticsHelper(GetType(), _logger);
        diagnostics.AddInformation($"Getting pull request {pullRequestNumber} details...");
        var pullRequestDetails = await gitHubRestApiClient.GetPullRequestDetailsAsync(repo, pullRequestNumber);
        diagnostics.AddInformation($"Got pull request {pullRequestDetails.Number} details");

        try
        {
            return await GeneratePullRequestOverviewCommentAsync(gitHubRestApiClient, pullRequestDetails, maxContextSizeInTokens, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.AddError($"**Error generating pull request {pullRequestDetails.Number} overview:**\r\n{ex}");
            throw;
        }
    }

    internal async Task<PullRequestOverviewCommentResponse> GeneratePullRequestOverviewCommentAsync(GitHubRestApiClient gitHubRestApiClient, PullRequestDetails pullRequestDetails, int? maxContextSizeInTokens, DiagnosticsHelper? diagnostics)
    {
        diagnostics?.AddInformation(
            $"Pull Request: {pullRequestDetails.Title} #{pullRequestDetails.Number}\r\nAuthor: {pullRequestDetails.User}\r\nUser Commits: {pullRequestDetails.Commits.Length}\r\nUser-Changed Files: {pullRequestDetails.PullRequestFiles.Length}");
        diagnostics?.AddInformation($"Generating pull request {pullRequestDetails.Number} overview comment...");

        if (Cache.TryGetValue((pullRequestDetails.Repo.Trim().ToLower(), pullRequestDetails.Number), out var cachedResult) &&
            cachedResult.lastUserCommitDate >= pullRequestDetails.Commits.LastOrDefault()?.Date)
        {
            diagnostics?.AddInformation("Returning overview comment from cache");
            return cachedResult.pullRequestOverviewCommentResponse;
        }

        var maxContextSizeInTokensForClassifier = maxContextSizeInTokens.HasValue
            ? maxContextSizeInTokens.Value -
              ContextUtils.EstimateTokens(GitHubPullRequestOverviewAgent.Step2_RefineOverviewWithCoreChangesSystemPrompt.Text, BytesPerToken.Typical)
            : maxContextSizeInTokens;
        diagnostics?.AddInformation(
            $"Max context size after {nameof(GitHubPullRequestOverviewAgent)} system prompt estimation: {maxContextSizeInTokensForClassifier}");

        var pullRequestFilesClassifierFlow =
            new GitHubPullRequestFilesClassifierFlow(_chatClient, _logger);
        var pullRequestFilesClassificationResponse = await pullRequestFilesClassifierFlow.ClassifyPullRequestFilesAsync(gitHubRestApiClient, pullRequestDetails, maxContextSizeInTokensForClassifier,
            addExcludedFilesInternalMessage: false, diagnostics);

        var pullRequestOverviewCommentAgent = new GitHubPullRequestOverviewAgent(_chatClient);
        diagnostics?.AddInformation("Generating pull request review comment...");
        var sw = Stopwatch.StartNew();
        var pullRequestOverviewCommentResponse =
            await pullRequestOverviewCommentAgent.GeneratePullRequestOverviewCommentAsync(pullRequestFilesClassificationResponse, maxContextSizeInTokens, diagnostics);
        sw.Stop();
        diagnostics?.AddInformation($"Generated pull request review comment, duration: {sw.Elapsed}");
        Cache[(pullRequestDetails.Repo.Trim().ToLower(), pullRequestDetails.Number)] = (pullRequestOverviewCommentResponse,
            pullRequestDetails.Commits.LastOrDefault()?.Date);
        return pullRequestOverviewCommentResponse;
    }
}