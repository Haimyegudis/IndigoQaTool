using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Text;
using Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V2;
using Tools.ExternalDevServices.AI.Orchestration.Utils;
using Tools.ExternalDevServices.Integrations.GitHub;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Flows.GitHub.V2;

public class GitHubAddOrUpdatePullRequestOverviewCommentFlow
{
    public bool GenerateFileCodeReviewsForReviewPlan { get; set; } = true;
    public bool PostCommentAsReviewer { get; set; }
    public bool DebugModeEnabled { get; set; }

    public Action<string>? Progress { get; set; }

    public async Task<GitHubAddOrUpdatePullRequestOverviewCommentResponse> AddOrUpdatePullRequestOverviewCommentAsync(GitHubRestApiClient gitHubRestApiClient,
        IChatClient chatClient,
        bool chatClientSupportsStructuredOutputWithoutExplicitSystemMessage,
        string repo, 
        int pullRequestNumber,
        ILogger? logger)
    {
        using var diagnostics = new DiagnosticsHelper(GetType(), logger,
            diagnosticsDirectoryName: $"PR_Overview_{repo}_{pullRequestNumber}");
        try
        {
            var pullRequestOverviewCommentResponse = await GeneratePullRequestOverviewCommentAsync(gitHubRestApiClient,
                chatClient, chatClientSupportsStructuredOutputWithoutExplicitSystemMessage, repo, pullRequestNumber,
                logger, diagnostics);

            if (DebugModeEnabled)
            {
                diagnostics.AddInformation("Debug mode enabled, not posting comment to PR.");
                Progress?.Invoke("Debug mode enabled, not posting comment to PR.");
                diagnostics.AddInformation($"Pull Request Overview Comment:\r\n{pullRequestOverviewCommentResponse.Comment}");
                return pullRequestOverviewCommentResponse;
            }

            Progress?.Invoke("Posting pull request overview comment to PR...");
            var commentDetails = await PullRequestCommentUtils.AddOrUpdateCommentWithPrefixMarkerAndPostfixToPullRequestAsync(
                gitHubRestApiClient,
                pullRequestDetails: pullRequestOverviewCommentResponse.PullRequestOverview.FilesClassifications.PullRequest,
                PostCommentAsReviewer,
                comment: pullRequestOverviewCommentResponse.Comment,
                prefix: PullRequestCommentUtils.AiGeneratedPullRequestOverviewCommentMarker,
                postfix: PullRequestCommentUtils.AiGeneratedPullRequestOverviewCommentDisclaimer,
                diagnostics);

            pullRequestOverviewCommentResponse.CommentId = commentDetails.commentId;
            pullRequestOverviewCommentResponse.CommentHtmlUrl = commentDetails.htmlUrl;

            var debug =
                $"Comment {commentDetails.commentId} {(commentDetails.added ? "added" : "updated")} at {commentDetails.htmlUrl}.\r\n\r\nComment contents:\r\n{pullRequestOverviewCommentResponse.Comment}";

            diagnostics.AddInformation(debug);
            return pullRequestOverviewCommentResponse;
        }
        catch (Exception ex)
        {
            diagnostics.AddError("Error occured while generating pull request overview", ex);
            throw;
        }
    }

    public async Task<GitHubAddOrUpdatePullRequestOverviewCommentResponse> GeneratePullRequestOverviewCommentAsync(GitHubRestApiClient gitHubRestApiClient, 
        IChatClient chatClient, 
        bool chatClientSupportsStructuredOutputWithoutExplicitSystemMessage, 
        string repo, 
        int pullRequestNumber, 
        ILogger? logger, 
        DiagnosticsHelper? diagnostics)
    {
        var disposeDiagnostics = diagnostics is null;
        diagnostics ??= new DiagnosticsHelper(GetType(), logger,
            diagnosticsDirectoryName: $"PR_Overview_{repo}_{pullRequestNumber}");
        using var disposable = disposeDiagnostics ? diagnostics : Disposable.Empty;

        diagnostics.AddInformation(
                $"""
             Adding or updating pull request overview comment for PR #{pullRequestNumber} in {repo} repo.");
             
             Settings: 
             {nameof(GenerateFileCodeReviewsForReviewPlan)}={GenerateFileCodeReviewsForReviewPlan}, 
             {nameof(PostCommentAsReviewer)}={PostCommentAsReviewer}
             """);

        var sw = Stopwatch.StartNew();

        Progress?.Invoke("Generating pull request overview comment...");
        var pullRequestOverviewFlow = new PullRequestOverviewFlow();
        var pullRequestOverview = await pullRequestOverviewFlow.GeneratePullRequestOverviewAsync(chatClient,
            chatClientSupportsStructuredOutputWithoutExplicitSystemMessage, gitHubRestApiClient, repo,
            pullRequestNumber, logger, diagnostics);

        Progress?.Invoke($"{(GenerateFileCodeReviewsForReviewPlan ? "Generating" : "Skipping")} pull request code reviews...");
        var codeReviewsFlow = new PullRequestCodeReviewFlow();
        var codeReviews = GenerateFileCodeReviewsForReviewPlan
            ? await codeReviewsFlow.GenerateFileCodeReviewAsync(chatClient,
                chatClientSupportsStructuredOutputWithoutExplicitSystemMessage, pullRequestOverview, logger, diagnostics)
            : [];

        WriteCodeReviewsToDiagnostics(pullRequestOverview, codeReviews, diagnostics);

        var pullRequestOverviewComment = pullRequestOverview.PullRequestOverviewResponse.Summary;
        if (pullRequestOverview.PullRequestOverviewResponse.KeyChangesAndAdditions.Length > 0)
        {
            pullRequestOverviewComment =
                $"""
                  {pullRequestOverviewComment}

                  ## Key Changes/Additions:
                  {string.Join("\r\n", pullRequestOverview.PullRequestOverviewResponse.KeyChangesAndAdditions.Select(k => $"- {k}"))}
                  """;
        }

        var codeReviewsStringBuilder = new StringBuilder();
        var first = true;
        foreach (var codeReview in codeReviews.OrderBy(codeReview => codeReview.FileCodeReviewResponse.RiskType)
                     .ThenBy(codeReview => codeReview.File.DiffClassifications.Min(dc => dc.GetMinDiffClassificationType())))
        {
            var codeReviewString =
                first ? codeReview.ToCodeReviewMarkdown() : "\r\n\r\n" + codeReview.ToCodeReviewMarkdown();
            first = false;
            if (codeReviewString.Length + codeReviewsStringBuilder.Length > 64 * 1024) // Max comment length is 64KB
            {
                diagnostics.AddInformation("Code reviews comment length exceeds 64KB, truncating.");
                break;
            }

            codeReviewsStringBuilder.Append(codeReviewString);
        }

        pullRequestOverviewComment =
            $"""
                  {pullRequestOverviewComment}
                  
                  ## Files Review Plan:
                  <details>
                  <summary>Click to expand</summary>
                  
                  {codeReviewsStringBuilder.ToString().Trim()}
                  </details>
                  """;

        diagnostics.WriteContentToSeparateFile("PullRequestComment.md", pullRequestOverviewComment);

        sw.Stop();
        diagnostics.AddInformation($"Generated pull request overview comment, duration: {sw.Elapsed}.");

        return new GitHubAddOrUpdatePullRequestOverviewCommentResponse
        {
            Comment = pullRequestOverviewComment,
            PullRequestOverview = pullRequestOverview,
            FileCodeReviews = codeReviews,
            ReviewPlan = ""
        };
    }

    private void WriteCodeReviewsToDiagnostics(PullRequestOverview pullRequestOverview, IReadOnlyCollection<FileCodeReview> codeReviews, DiagnosticsHelper diagnostics)
    {
        var codeReviewsMarkdown = string.Join("\r\n\r\n",
            codeReviews.OrderBy(cr => cr.FileCodeReviewResponse.RiskType).Select(cr => cr.ToCodeReviewMarkdown()));
        diagnostics.WriteContentToSeparateFile("AllFileCodeReviews.md", codeReviewsMarkdown);

        diagnostics.AddInformation(
            $"Generated {codeReviews.Count} file code reviews for PR #{pullRequestOverview.FilesClassifications.PullRequest.Number} in {pullRequestOverview.FilesClassifications.PullRequest.Repo} repo.");
    }
}