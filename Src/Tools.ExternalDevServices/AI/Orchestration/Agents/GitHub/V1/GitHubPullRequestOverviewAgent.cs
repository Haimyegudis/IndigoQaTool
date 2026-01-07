using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tools.ExternalDevServices.AI.Orchestration.Utils;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V1;

public partial class GitHubPullRequestOverviewAgent
{
    private readonly IChatClient _chatClient;

    public GitHubPullRequestOverviewAgent(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<PullRequestOverviewCommentResponse> GeneratePullRequestOverviewCommentAsync(
        PullRequestFilesClassificationResponse pullRequestFilesClassificationResponse,
        int? maxContextSize,
        DiagnosticsHelper? diagnosticsHelper = null)
    {
        var step1Response = await GetStep1ResponseAsync(pullRequestFilesClassificationResponse, diagnosticsHelper);
        var step2Response =
            await GetStep2ResponseAsync(pullRequestFilesClassificationResponse, step1Response, maxContextSize, diagnosticsHelper);
        var step3Response =
            await GetStep3ResponseAsync(pullRequestFilesClassificationResponse, step2Response, maxContextSize, diagnosticsHelper);
        var step4Response = await GetStep4ResponseAsync(pullRequestFilesClassificationResponse, step3Response,
            maxContextSize, diagnosticsHelper);

        var overviewCommentForHumanReviewers =
            await GenerateOverviewCommentForHumanReviewersAsync(pullRequestFilesClassificationResponse, step4Response, diagnosticsHelper);
        var overviewCommentForHumanLLMs =
            await GenerateOverviewCommentForLLMsAsync(pullRequestFilesClassificationResponse, step4Response, diagnosticsHelper);

        return new PullRequestOverviewCommentResponse(pullRequestFilesClassificationResponse,
            overviewCommentForHumanReviewers, overviewCommentForHumanLLMs);
    }

    private async Task<string> GetStep1ResponseAsync(
        PullRequestFilesClassificationResponse pullRequestFilesClassificationResponse,
        DiagnosticsHelper? diagnosticsHelper)
    {
        diagnosticsHelper?.AddInformation("Step1 - generating initial general pull request overview...");

        var reviewPrompt =
            $"""
              **Pull Request**: {pullRequestFilesClassificationResponse.PullRequest.Title}
              **Description**: {pullRequestFilesClassificationResponse.PullRequest.StripDevopsCommentFromBody()}
              ---
              **Pull Request Files**:
              {new JArray(pullRequestFilesClassificationResponse.ClassifiedFiles.OrderBy(c => c.DiffClassificationResponse.DiffChangeType).Select(c => JObject.FromObject(new{Name = c.File.FileName, FileChangeType = c.FileChangeType.ToString(), ChangeClassification = c.DiffClassificationResponse.DiffChangeType.ToString(), ChangeSummary = c.DiffClassificationResponse.DiffSummary})).Cast<object>().ToArray()).ToString(Formatting.Indented)}
             """;

        diagnosticsHelper?.WriteContentToSeparateFile("GenerateOverviewComment_Step1_Prompt.md", reviewPrompt);

        ChatMessage[] chatMessages =
        [
            Step1_GenerateGeneralOverviewSystemPrompt,
            new(ChatRole.User, reviewPrompt)
        ];

        var response = (await _chatClient.GetResponseAsync(chatMessages)).Text;
        diagnosticsHelper?.WriteContentToSeparateFile("GenerateOverviewComment_Step1_Response.md", response);

        return response;
    }

    private async Task<string> GetStep2ResponseAsync(
        PullRequestFilesClassificationResponse pullRequestFilesClassificationResponse,
        string step1Response,
        int? maxContextSize,
        DiagnosticsHelper? diagnosticsHelper)
    {
        diagnosticsHelper?.AddInformation("Step2 - Adding core changes to pull request overview...");

        var coreChangeFiles = pullRequestFilesClassificationResponse.ClassifiedFiles
            .Where(cf => cf.DiffClassificationResponse.DiffChangeType is DiffChangeType.CoreChange)
            .OrderByDescending(cf => cf.Diff.Length)
            .ToArray();
        if (coreChangeFiles.Length == 0) return step1Response;

        return await ExecuteFilesDiffStepWithContextAwarenessAsync(step: 2, 
            pullRequestFilesClassificationResponse, 
            coreChangeFiles,
            Step2_RefineOverviewWithCoreChangesSystemPrompt,
            step1Response,
            includeDiffsInReviewPrompt: true,
            maxContextSize,
            diagnosticsHelper);
    }

    private async Task<string> GetStep3ResponseAsync(
        PullRequestFilesClassificationResponse pullRequestFilesClassificationResponse,
        string step2Response,
        int? maxContextSize,
        DiagnosticsHelper? diagnosticsHelper)
    {
        diagnosticsHelper?.AddInformation("Step3 - Adding behavioral adapt changes to pull request overview...");

        var behavioralAdaptChangeFiles = pullRequestFilesClassificationResponse.ClassifiedFiles
            .Where(cf => cf.DiffClassificationResponse.DiffChangeType is DiffChangeType.BehavioralAdapt)
            .OrderByDescending(cf => cf.Diff.Length)
            .ToArray();
        if (behavioralAdaptChangeFiles.Length == 0) return step2Response;

        return await ExecuteFilesDiffStepWithContextAwarenessAsync(step: 3,
            pullRequestFilesClassificationResponse,
            behavioralAdaptChangeFiles,
            Step3_RefineOverviewWithBehavioralAdaptChangesSystemPrompt,
            step2Response,
            includeDiffsInReviewPrompt: true,
            maxContextSize,
            diagnosticsHelper);
    }

    private async Task<string> GetStep4ResponseAsync(
        PullRequestFilesClassificationResponse pullRequestFilesClassificationResponse,
        string step3Response,
        int? maxContextSize,
        DiagnosticsHelper? diagnosticsHelper)
    {
        diagnosticsHelper?.AddInformation("Step4 - Adding remaining changes to pull request overview...");

        var remainingChangeFiles = pullRequestFilesClassificationResponse.ClassifiedFiles
            .Where(cf => cf.DiffClassificationResponse.DiffChangeType > DiffChangeType.BehavioralAdapt)
            .OrderByDescending(cf => cf.Diff.Length)
            .ToArray();
        if (remainingChangeFiles.Length == 0) return step3Response;

        return await ExecuteFilesDiffStepWithContextAwarenessAsync(step: 4,
            pullRequestFilesClassificationResponse,
            remainingChangeFiles,
            Step4_RefineOverviewWithRemainingFilesSystemPrompt,
            step3Response,
            includeDiffsInReviewPrompt: false,
            maxContextSize,
            diagnosticsHelper);
    }

    private async Task<string> GenerateOverviewCommentForHumanReviewersAsync(
        PullRequestFilesClassificationResponse pullRequestFilesClassificationResponse, 
        string step4Response,
        DiagnosticsHelper? diagnosticsHelper) =>
        await GenerateOverviewCommentForReviewersAsync(pullRequestFilesClassificationResponse, 
            step4Response, 
            GenerateFinalOverviewCommentForHumanReviewersSystemPrompt, 
            reviewerType: "Human",
            generatedOverviewType: "Comment",
            diagnosticsHelper: diagnosticsHelper);

    // ReSharper disable once InconsistentNaming
    private async Task<string> GenerateOverviewCommentForLLMsAsync(
        PullRequestFilesClassificationResponse pullRequestFilesClassificationResponse, 
        string step4Response, 
        DiagnosticsHelper? diagnosticsHelper) =>
        await GenerateOverviewCommentForReviewersAsync(pullRequestFilesClassificationResponse,
            step4Response,
            GenerateFinalOverviewCommentForLLMReviewerSystemPrompt,
            reviewerType: "LLM",
            generatedOverviewType: "Summary",
            diagnosticsHelper);

    private async Task<string> GenerateOverviewCommentForReviewersAsync(
        PullRequestFilesClassificationResponse pullRequestFilesClassificationResponse, 
        string step4Response,
        ChatMessage systemPrompt, 
        string reviewerType, 
        string generatedOverviewType,
        DiagnosticsHelper? diagnosticsHelper)
    {
        diagnosticsHelper?.AddInformation($"Generating pull request overview {generatedOverviewType.ToLower()} for {reviewerType} reviewers...");
        var includedFiles = pullRequestFilesClassificationResponse.ClassifiedFiles.Where(cf =>
                cf.DiffClassificationResponse.DiffChangeType <= DiffChangeType.BehavioralAdapt)
            .OrderBy(cf => cf.DiffClassificationResponse.DiffChangeType)
            .ToArray();

        ChatMessage[] chatMessages =
        [
            systemPrompt,
            new (ChatRole.User,
                $"""
                 **Pull Request**: {pullRequestFilesClassificationResponse.PullRequest.Title}
                 **Description**: {pullRequestFilesClassificationResponse.PullRequest.StripDevopsCommentFromBody()}
                 **Detailed Pull Request Overview**: {step4Response}
                 **Files**:
                 {ClassifiedFile.ToClassificationString(includedFiles, includeClassificationDetails: true, DiffChangeType.None)}
                 """)
        ];

        diagnosticsHelper?.WriteContentToSeparateFile(
            $"GenerateOverview{generatedOverviewType}For{reviewerType}ReviewersPrompt.md", chatMessages.Last().Text);
        var response = (await _chatClient.GetResponseAsync(chatMessages, ChatOptionsUtils.CreateLowEntropyChatOptions())).Text;
        diagnosticsHelper?.WriteContentToSeparateFile(
            $"GenerateOverview{generatedOverviewType}For{reviewerType}ReviewersResponse.md", response);

        return response;
    }

    private async Task<string> ExecuteFilesDiffStepWithContextAwarenessAsync(int step, 
        PullRequestFilesClassificationResponse pullRequestFilesClassificationResponse, 
        ClassifiedFile[] classifiedFiles,
        ChatMessage systemPrompt,
        string currentPullRequestOverview, 
        bool includeDiffsInReviewPrompt,
        int? maxContextSize,
        DiagnosticsHelper? diagnosticsHelper)
    {
        var reviewPromptPrefix =
            $"""
             **Pull Request Title**: {pullRequestFilesClassificationResponse.PullRequest.Title}
             **Pull Request Description**: {pullRequestFilesClassificationResponse.PullRequest.StripDevopsCommentFromBody()}
             """;
        var detailedPullRequestReview = $"**Detailed Pull Request Overview**: {currentPullRequestOverview}";

        var filesDiffs = "**Files**:\r\n";
        var iterations = 0;
        foreach (var coreChangeFile in classifiedFiles)
        {
            var classifiedFileString = coreChangeFile
                .ToClassificationString(includeClassificationDetails: true,
                    includeDiffsInReviewPrompt ? DiffChangeType.Any : DiffChangeType.None);
            if (maxContextSize.HasValue)
            {
                ContextUtils.EstimateContextFit(
                [
                    systemPrompt.Text,
                    reviewPromptPrefix,
                    detailedPullRequestReview,
                    filesDiffs.TrimEnd(),
                    classifiedFileString
                ], maxContextSize.Value, BytesPerToken.Typical, BytesPerToken.CodeSafeFactor, out var estimatedTokens);
                if (estimatedTokens >= maxContextSize.Value * 0.9)
                {
                    if (string.IsNullOrWhiteSpace(filesDiffs))
                    {
                        throw new InvalidOperationException(
                            $"Reached {estimatedTokens} tokens while aggregated diff is empty");
                    }

                    ChatMessage[] chatMessages =
                    [
                        systemPrompt,
                        new(ChatRole.User,
                            $"""
                             {reviewPromptPrefix}
                             {detailedPullRequestReview}
                             {filesDiffs.Trim()}
                             """)
                    ];

                    iterations += 1;

                    diagnosticsHelper?.AddInformation(
                        $"Step {step} - Getting intermediate overview from model, estimated tokens: {estimatedTokens}, max tokens: {maxContextSize}, iteration: {iterations}");

                    diagnosticsHelper?.WriteContentToSeparateFile(
                        $"GenerateOverviewComment_Step{step}_Iteration{iterations}_Prompt.md", chatMessages.Last().Text);
                    var updatedResponse = (await _chatClient.GetResponseAsync(chatMessages, ChatOptionsUtils.CreateLowEntropyChatOptions())).Text;
                    diagnosticsHelper?.WriteContentToSeparateFile(
                        $"GenerateOverviewComment_Step{step}_Iteration{iterations}_Response.md", updatedResponse);
                    filesDiffs = "**Files**:\r\n";
                    detailedPullRequestReview = $"**Detailed Pull Request Overview**: {updatedResponse}";
                }
            }

            filesDiffs += classifiedFileString + "\r\n\r\n";
        }

        if (filesDiffs == "**Files**:\r\n") return detailedPullRequestReview;

        var filesPhase = iterations == 0 ? "remaining files" : "all files";
        diagnosticsHelper?.AddInformation($"Step {step} - Getting final response for {filesPhase}...");
        ChatMessage[] finalChatMessages =
        [
            systemPrompt,
            new(ChatRole.User,
                $"""
                 {reviewPromptPrefix}
                 {detailedPullRequestReview}
                 {filesDiffs.Trim()}
                 """)
        ];

        iterations += 1;
        diagnosticsHelper?.WriteContentToSeparateFile(
            $"GenerateOverviewComment_Step{step}_Iteration{iterations}_Prompt.md", finalChatMessages.Last().Text);
        detailedPullRequestReview = (await _chatClient.GetResponseAsync(finalChatMessages)).Text;
        diagnosticsHelper?.WriteContentToSeparateFile(
            $"GenerateOverviewComment_Step{step}_Iteration{iterations}_Response.md", detailedPullRequestReview);

        return detailedPullRequestReview;
    }
}