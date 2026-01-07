using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tools.ExternalDevServices.AI.Orchestration.Utils;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V2;

public class PullRequestReviewPlanAgent
{
    public async Task<string> GeneratePullRequestReviewPlanAsync(
        IChatClient chatClient,
        bool chatClientSupportsStructuredOutputWithoutExplicitSystemMessage,
        PullRequestOverview pullRequestOverview,
        IReadOnlyCollection<FileCodeReview> fileCodeReviews,
        ILogger? logger,
        DiagnosticsHelper? callerDiagnostics,
        DiffClassificationType maxClassificationType = DiffClassificationType.MaxValue)
    {
        using var diagnostics = new DiagnosticsHelper(GetType(), logger) { Parent = callerDiagnostics };
        var pullRequestDetails = pullRequestOverview.FilesClassifications.PullRequest;
        var minClassificationType = pullRequestOverview.FilesClassifications.MinDiffClassificationType;

        diagnostics.AddInformation($"Generating review plan for #{pullRequestDetails.Number} in {pullRequestDetails.Repo} repo");

        var filesInfos = fileCodeReviews.Count > 0
            ? $"""
               **Files Reviews**:
               {string.Join("\r\n\r\n", fileCodeReviews.OrderBy(fc => fc.FileCodeReviewResponse.RiskType).Select(fc => $"**File**: {fc.File.FileName}\r\n**Review**: {fc.FileCodeReviewResponse.FileReview}\r\n**Risk**: {fc.FileCodeReviewResponse.RiskType.ToString()}\r\n**Risk Justification**: {fc.FileCodeReviewResponse.RiskJustification}"))}
               """
            : minClassificationType > DiffClassificationType.None && minClassificationType <= maxClassificationType
                ? $"""
                   **Files Classifications**:
                   {pullRequestOverview.FilesClassifications.ToJson(maxClassificationType)}
                   """
                : "";
        diagnostics.AddInformation($"Min Classification type: {minClassificationType}, Files Info:\r\n{filesInfos}");

        if (string.IsNullOrWhiteSpace(filesInfos)) return "";

        var reviewPlan = (await chatClient.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, Prompts.ReviewPlanSystemPrompt).WithDiagnostics($"{nameof(Prompts.ReviewPlanSystemPrompt)}", diagnostics),
            ChatMessageUtils.SystemChatMessages.MaxReasoning,
            new ChatMessage(ChatRole.User,
                $"""
                 **Pull Request**: {pullRequestDetails.Title} #{pullRequestDetails.Number}
                 **Pull Request Overview**:
                 {pullRequestOverview.PullRequestOverviewResponse.Summary}

                 **Key Changes and Additions**:
                 {string.Join("\n", pullRequestOverview.PullRequestOverviewResponse.KeyChangesAndAdditions.Select(k => $"- {k}"))}

                 {filesInfos}
                 """.Trim()).WithDiagnostics("User Input", diagnostics)
        ])).RemoveThinking(out _);

        return reviewPlan;
    }
}