using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tools.ExternalDevServices.AI.Orchestration.Utils;
using Tools.ExternalDevServices.Integrations.GitHub;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V2;

public class FileCodeReviewAgent
{
    public async Task<FileCodeReview> GenerateFileCodeReviewAsync(
        IChatClient chatClient,
        bool chatClientSupportsStructuredOutputWithoutExplicitSystemMessage,
        PullRequestOverview pullRequestOverview,
        FileDiffsClassification fileDiffsClassification,
        ILogger? logger,
        DiagnosticsHelper? callerDiagnostics)
    {
        using var diagnostics = new DiagnosticsHelper(GetType(), logger) { Parent = callerDiagnostics };
        diagnostics.AddInformation($"Generating file code review for file {fileDiffsClassification.FileName}", diagnosticsFileOnly: true);

        var sw = Stopwatch.StartNew();

        var pullRequestDetails = pullRequestOverview.FilesClassifications.PullRequest;
        var diffsString = fileDiffsClassification.FileDiffs.ChangeType is FileChangeType.Added
            ? $"""
               **Added File Contents:**:
               
               ```
               {fileDiffsClassification.FileDiffs.FullDiff}
               ```
               """
            : $"""
                **Full File Content Including Diffs**:
                ```diff
                {fileDiffsClassification.FileDiffs.FullDiff}
                ```
                
                **Diff Blocks**:
                {string.Join("\r\n\r\n", fileDiffsClassification.FileDiffs.Diffs.Select(diff => $"```diff\r\n{diff}\r\n```"))}
                """;

        var changedFileReviewSystemPrompt =
            new ChatMessage(ChatRole.System,
                Prompts.ModifiedFileReviewSystemPrompt(Prompts.AddedLinesMarker, Prompts.DeletedLinesMarker,
                    Prompts.UnchangedLinesMarker));
        var fileReviewChatOptions = ChatOptionsUtils.CreateGreedyChatOptions();
        fileReviewChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<FileCodeReviewResponse>();
        var fileReviewSchemaSystemPrompt = Prompts
            .GetStructuredOutputChatMessage<FileCodeReviewResponse>(
                chatClientSupportsStructuredOutputWithoutExplicitSystemMessage).ToArray();

        var addedFileReviewSystemPrompt =
            new ChatMessage(ChatRole.System, Prompts.AddedFileReviewSystemPrompt);
        
        
        ChatMessage[] fileReviewMessages =
        [
            fileDiffsClassification.FileDiffs.ChangeType is FileChangeType.Modified? changedFileReviewSystemPrompt :addedFileReviewSystemPrompt,
            ..fileReviewSchemaSystemPrompt,
            ChatMessageUtils.SystemChatMessages.MaxReasoning,
            new(ChatRole.User,
                $"""
                 Pull Request: {pullRequestDetails.Title} #{pullRequestDetails.Number}
                 **Description**: {pullRequestDetails.PullRequestBodyWithoutDevopsText}

                 Pull Request Overview:
                 {pullRequestOverview.PullRequestOverviewResponse.Summary}
                 
                 Key changes and additions:
                 {string.Join("\r\n", pullRequestOverview.PullRequestOverviewResponse.KeyChangesAndAdditions.Select(k => $"- {k}"))}

                 File: {fileDiffsClassification.FileDiffs.FileName}
                 Summary: 
                 {fileDiffsClassification.DiffSummary}

                 {diffsString}
                 """)
        ];
        diagnostics.AddInformation($"File Review Input:\r\n{fileReviewMessages.Last().Text}");

        var fileReviewResponseJson = (await chatClient.GetResponseAsync(fileReviewMessages, fileReviewChatOptions))
            .WithDiagnostics($"{fileDiffsClassification.FileName} Review Response", diagnostics).RemoveThinking(out _);
        var fileReviewResponse = await ChatClientUtils.ToStructuredResponseAsync<FileCodeReviewResponse>(chatClient, fileReviewResponseJson);
        
        var fileReview = new FileCodeReview
        {
            File = fileDiffsClassification,
            FileCodeReviewResponse = fileReviewResponse
        };

        sw.Stop();
        diagnostics.AddInformation(
            $"Generated file code review for file {fileDiffsClassification.FileName} in {sw.Elapsed}, Review:\r\n{JsonConvert.SerializeObject(fileReview, Formatting.Indented)}");

        return fileReview;
    }
}