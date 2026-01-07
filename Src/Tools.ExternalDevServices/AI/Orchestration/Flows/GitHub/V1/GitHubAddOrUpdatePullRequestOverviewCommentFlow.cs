using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tools.ExternalDevServices.AI.Orchestration.Utils;
using Tools.ExternalDevServices.Integrations.GitHub;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Flows.GitHub.V1;

public class GitHubAddOrUpdatePullRequestOverviewCommentFlow
{
    private readonly IChatClient _chatClient;
    private readonly ILogger? _logger;

    public GitHubAddOrUpdatePullRequestOverviewCommentFlow(IChatClient chatClient,
        ILogger? logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<string> AddOrUpdatePullRequestOverviewCommentAsync(GitHubRestApiClient gitHubRestApiClient, string repo, int pullRequestNumber, int? maxContextSizeInTokens)
    {
        using var diagnostics = new DiagnosticsHelper(GetType(), _logger);
        try
        {
            var pullRequestDetails = await gitHubRestApiClient.GetPullRequestDetailsAsync(repo, pullRequestNumber);
            var overviewCommentFlow = new GitHubPullRequestOverviewCommentFlow(_chatClient, _logger);
            var pullRequestOverviewCommentResponse = await overviewCommentFlow.GeneratePullRequestOverviewCommentAsync(gitHubRestApiClient, pullRequestDetails, maxContextSizeInTokens, diagnostics);
            var gitHubOverviewComment = pullRequestOverviewCommentResponse.OverviewCommentForHumanReviewers.StartsWith("```markdown")
                ? pullRequestOverviewCommentResponse.OverviewCommentForHumanReviewers["```markdown".Length..].Trim()
                : pullRequestOverviewCommentResponse.OverviewCommentForHumanReviewers;
            gitHubOverviewComment = gitHubOverviewComment.TrimEnd('`');
            var commentDetails = await PullRequestCommentUtils.AddOrUpdateCommentWithPrefixMarkerAndPostfixToPullRequestAsync(
                gitHubRestApiClient, 
                pullRequestDetails, 
                postCommentAsReviewer: false,
                comment: gitHubOverviewComment, 
                prefix: PullRequestCommentUtils.AiGeneratedPullRequestOverviewCommentMarker,
                postfix: PullRequestCommentUtils.AiGeneratedPullRequestOverviewCommentDisclaimer,
                diagnostics);

            return
                $"Comment {commentDetails.commentId} {(commentDetails.added ? "added" : "updated")} at {commentDetails.htmlUrl}.\r\n\r\nComment contents:\r\n{pullRequestOverviewCommentResponse}";
        }
        catch (Exception ex)
        {
            diagnostics.AddError($"Failed to add or update comment to pull request {pullRequestNumber}", ex);
            return $"Generating pull request {pullRequestNumber} overview failed, check diagnostics file for more information."; 
        }
    }
}