using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V1;
using Tools.ExternalDevServices.Integrations.GitHub;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Flows.GitHub.V1;

public class GitHubPullRequestCodeReviewFlow
{
    private readonly IChatClient _chatClient;
    private readonly ILogger? _logger;

    private static readonly Dictionary<(string, int), (PullRequestFilesCodeReviewResponse codeReview, DateTime? lastUserCommitDate)> Cache = new();

    public GitHubPullRequestCodeReviewFlow(IChatClient chatClient,
        ILogger? logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<PullRequestFilesCodeReviewResponse> GeneratePullRequestCodeReviewsAsync(GitHubRestApiClient gitHubRestApiClient, string repo, int pullRequestNumber, int? maxContextSizeInTokens)
    {
        using var diagnostics = new DiagnosticsHelper(GetType(), _logger);

        diagnostics.AddInformation($"Getting pull request {pullRequestNumber} details...");
        var pullRequestDetails = await gitHubRestApiClient.GetPullRequestDetailsAsync(repo, pullRequestNumber);
        diagnostics.AddInformation(
            $"Pull Request: {pullRequestDetails.Title} #{pullRequestDetails.Number}\r\nAuthor: {pullRequestDetails.User}\r\nUser Commits: {pullRequestDetails.Commits.Count(c => c.CommitType is CommitType.UserCommit)}\r\nUser-Changed Files: {pullRequestDetails.Commits.Where(c => c.CommitType is CommitType.UserCommit).SelectMany(c => c.Files.Select(f => f.FileName)).Distinct().Count()}");

        if (Cache.TryGetValue((pullRequestDetails.Repo.Trim().ToLower(), pullRequestDetails.Number), out var cachedResult) &&
            cachedResult.lastUserCommitDate >= pullRequestDetails.Commits.LastOrDefault()?.Date)
        {
            diagnostics.AddInformation("Returning code review from cache");
            return cachedResult.codeReview;
        }

        try
        {
            diagnostics.AddInformation("Generating pull request code review...");
            var sw = Stopwatch.StartNew();
            var response = await GeneratePullRequestCodeReviewsAsync(gitHubRestApiClient, pullRequestDetails, maxContextSizeInTokens, diagnostics);
            Cache[(pullRequestDetails.Repo.Trim().ToLower(), pullRequestDetails.Number)] = (response,
                pullRequestDetails.Commits.LastOrDefault()?.Date);
            sw.Stop();
            diagnostics.AddInformation($"Generated pull request code review, duration: {sw.Elapsed}");
            return response;
        }
        catch (Exception ex)
        {
            diagnostics.AddError($"**Error generating pull request {pullRequestDetails.Number} overview:**\r\n{ex}");
            throw;
        }
    }

    public async Task<PullRequestFilesCodeReviewResponse> GeneratePullRequestCodeReviewsAsync(
        GitHubRestApiClient gitHubRestApiClient, PullRequestDetails pullRequestDetails, int? maxContextSizeInTokens)
    {
        using var diagnostics = new DiagnosticsHelper(GetType(), _logger);

        if (Cache.TryGetValue((pullRequestDetails.Repo.Trim().ToLower(), pullRequestDetails.Number), out var cachedResult) &&
            cachedResult.lastUserCommitDate >= pullRequestDetails.Commits.LastOrDefault()?.Date)
        {
            diagnostics.AddInformation("Returning code review from cache");
            return cachedResult.codeReview;
        }

        try
        {
            diagnostics.AddInformation("Generating pull request code review...");
            var sw = Stopwatch.StartNew();
            var response = await GeneratePullRequestCodeReviewsAsync(gitHubRestApiClient, pullRequestDetails,
                maxContextSizeInTokens, diagnostics);
            Cache[(pullRequestDetails.Repo.Trim().ToLower(), pullRequestDetails.Number)] = (response,
                pullRequestDetails.Commits.LastOrDefault()?.Date);
            sw.Stop();
            diagnostics.AddInformation($"Generated pull request code review, duration: {sw.Elapsed}");
            return response;
        }
        catch (Exception ex)
        {
            diagnostics.AddError($"**Error generating pull request {pullRequestDetails.Number} overview:**\r\n{ex}");
            throw;
        }
    }

    private async Task<PullRequestFilesCodeReviewResponse> GeneratePullRequestCodeReviewsAsync(GitHubRestApiClient gitHubRestApiClient, PullRequestDetails pullRequestDetails, int? maxContextSizeInTokens, DiagnosticsHelper? diagnostics)
    {
        diagnostics?.AddInformation($"Generating pull request {pullRequestDetails.Number} overview comment...");

        var gitHubPullRequestOverviewCommentFlow =
            new GitHubPullRequestOverviewCommentFlow(_chatClient, _logger);
        var pullRequestOverviewCommentResponse =
            await gitHubPullRequestOverviewCommentFlow.GeneratePullRequestOverviewCommentAsync(gitHubRestApiClient,
                pullRequestDetails, maxContextSizeInTokens, diagnostics);

        return await GeneratePullRequestCodeReviewsAsync(pullRequestOverviewCommentResponse, diagnostics);
    }

    public async Task<PullRequestFilesCodeReviewResponse> GeneratePullRequestCodeReviewsAsync(
        PullRequestOverviewCommentResponse pullRequestOverviewCommentResponse)
    {
        using var diagnostics = new DiagnosticsHelper(GetType(), _logger);

        if (Cache.TryGetValue(
                (pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.PullRequest.Repo.Trim().ToLower(),
                    pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.PullRequest.Number),
                out var cachedResult) &&
            cachedResult.lastUserCommitDate >= pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse
                .PullRequest.Commits.LastOrDefault()?.Date)
        {
            diagnostics.AddInformation("Returning code review from cache");
            return cachedResult.codeReview;
        }

        try
        {
            diagnostics.AddInformation("Generating pull request code review...");
            var sw = Stopwatch.StartNew();
            var response = await GeneratePullRequestCodeReviewsAsync(pullRequestOverviewCommentResponse, diagnostics);
            Cache[
                (pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.PullRequest.Repo.Trim().ToLower(),
                    pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.PullRequest.Number)] = (
                response,
                pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.PullRequest.Commits
                    .LastOrDefault()?.Date);
            sw.Stop();
            diagnostics.AddInformation($"Generated pull request code review, duration: {sw.Elapsed}");
            return response;
        }
        catch (Exception ex)
        {
            diagnostics.AddError($"**Error generating pull request {pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.PullRequest.Number} overview:**\r\n{ex}");
            throw;
        }
    }

    private async Task<PullRequestFilesCodeReviewResponse> GeneratePullRequestCodeReviewsAsync(PullRequestOverviewCommentResponse pullRequestOverviewCommentResponse, DiagnosticsHelper? diagnostics)
    {
        var pullRequestOverviewCommentAgent = new GitHubPullRequestCodeReviewAgent(_chatClient);
        var pullRequestFilesCodeReviewResponse =
            await pullRequestOverviewCommentAgent.GeneratePullRequestCodeReviewAsync(
                pullRequestOverviewCommentResponse, diagnostics);
        
        return pullRequestFilesCodeReviewResponse;
    }
}