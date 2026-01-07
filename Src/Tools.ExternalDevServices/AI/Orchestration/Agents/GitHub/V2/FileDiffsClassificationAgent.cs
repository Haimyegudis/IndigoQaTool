using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using Newtonsoft.Json;
using Tools.ExternalDevServices.AI.Orchestration.Utils;
using Tools.ExternalDevServices.Integrations.GitHub;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V2;

public class FileDiffsClassificationAgent
{
    public async Task<PullRequestFilesClassifications> ClassifyPullRequestFilesAsync(
        IChatClient chatClient,
        bool chatClientSupportsStructuredOutputWithoutExplicitSystemMessage,
        GitHubRestApiClient gitHubRestApiClient, 
        string repo, 
        int pullRequestNumber,
        ILogger? logger,
        DiagnosticsHelper? callerDiagnostics)
    {
        using var diagnostics = new DiagnosticsHelper(GetType(), logger) {Parent = callerDiagnostics};
        diagnostics.AddInformation(
            $"Classifying pull request files for PR #{pullRequestNumber} in {repo} repo.");

        var sw = Stopwatch.StartNew();
        diagnostics.AddInformation("Getting pull request details.");
        var pullRequestDetails = await gitHubRestApiClient.GetPullRequestDetailsAsync(repo, pullRequestNumber);
        sw.Stop();
        diagnostics.AddInformation($"Got pull request details in {sw.Elapsed}.");

        diagnostics.AddInformation("Getting commit files.");
        sw.Restart();
        var commitFiles = pullRequestDetails.PullRequestFiles;

        var diffs = new ConcurrentBag<(string fileName, FileDiffs fileDiffs)>();
        await Parallel.ForEachAsync(commitFiles, async (commitFile, _) =>
        {
            var fileDiffs = await gitHubRestApiClient.GetFileDiffsAsync(pullRequestDetails, commitFile.FileName,
                addCsharpContext: commitFile.FileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase), contextSizeBefore: 0, contextSizeAfter: 0,
                Prompts.AddedLinesMarker, Prompts.DeletedLinesMarker, Prompts.UnchangedLinesMarker);
            diffs.Add((commitFile.FileName, fileDiffs));
        });
        var diffsMap = diffs.ToDictionary(diffTuple => diffTuple.fileName, diffTuple => diffTuple.fileDiffs);
        sw.Stop();
        diagnostics.AddInformation($"Got commit files in {sw.Elapsed}.");

        diagnostics.AddInformation("Classifying files.");
        sw.Restart();
        var filesClassifications =
            new List<FileDiffsClassification>(
                diffsMap.Values.Count(fd => fd.ChangeType is not FileChangeType.Deleted));

        var deletedFiles = new List<string>(diffsMap.Values.Count(fd => fd.ChangeType is FileChangeType.Deleted));
        var chatOptions = ChatOptionsUtils.CreateGreedyChatOptions();
        chatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<DiffClassificationResponse>();

        var changedFileReviewSystemPrompt =
            new ChatMessage(ChatRole.System, Prompts.AddedOrChangedFileClassificationSystemPrompt()).WithDiagnostics(
                "ChangedFileReviewSystemPrompt", diagnostics);
        var addedFileReviewSystemPrompt =
            new ChatMessage(ChatRole.System, Prompts.AddedOrChangedFileClassificationSystemPrompt()).WithDiagnostics(
                "AddedFileReviewSystemPrompt", diagnostics);
        var fileClassificationSchemaSystemPrompt =
            Prompts
                .GetStructuredOutputChatMessage<DiffClassificationResponse>(
                    chatClientSupportsStructuredOutputWithoutExplicitSystemMessage).ToArray()
                .WithDiagnostics($"{nameof(DiffClassificationResponse)} Schema", diagnostics);

        foreach (var commitFile in commitFiles)
        {
            var fileDiffs = diffsMap[commitFile.FileName];
            if (fileDiffs.ChangeType is FileChangeType.Deleted)
            {
                deletedFiles.Add(commitFile.FileName);
            }
            else
            {
                var diffsString = fileDiffs.ChangeType is FileChangeType.Added
                    ? $"""
                       **Added File Contents:**:
                       ```
                       {fileDiffs.FullDiff}
                       ```
                       """
                    : $"""
                        **Full File Contents Including Diffs**:
                        ```diff
                        {fileDiffs.FullDiff}
                        ```
                        
                        **Diff Blocks**:
                        {string.Join("\r\n\r\n", fileDiffs.Diffs.Select(diff => $"```diff\r\n{diff}\r\n```"))}
                        """;

                IReadOnlyCollection<ChatMessage> messages =
                [
                    fileDiffs.ChangeType is FileChangeType.Added? addedFileReviewSystemPrompt : changedFileReviewSystemPrompt,
                    ..fileClassificationSchemaSystemPrompt,
                    ChatMessageUtils.SystemChatMessages.MaxReasoning,
                    new ChatMessage(ChatRole.User,
                        $"""
                         **Pull Request**: {pullRequestDetails.Title} #{pullRequestNumber}
                         **Description**: {pullRequestDetails.PullRequestBodyWithoutDevopsText}

                         **File**: {fileDiffs.FileName}
                         **Change Type**: File was {fileDiffs.ChangeType}

                         {diffsString}
                         """).WithDiagnostics(fileDiffs.FileName, diagnostics)
                ];
                var response = (await chatClient.GetResponseAsync(messages, chatOptions))
                    .WithDiagnostics($"{fileDiffs.FileName} Response (with thinking)", diagnostics)
                    .RemoveThinking(out _);

                var diffClassification = await ChatClientUtils.ToStructuredResponseAsync<DiffClassificationResponse>(chatClient, response);
                if (diffClassification is null)
                    throw new InvalidOperationException($"Failed to get Json classification response for {fileDiffs.FileName}");

                filesClassifications.Add(FileDiffsClassification.FromResponse(fileDiffs, diffClassification));
            }
        }

        var pullRequestFileClassifications = new PullRequestFilesClassifications
        {
            PullRequest = pullRequestDetails,
            DeletedFiles = deletedFiles.ToArray(),
            AddedFiles = filesClassifications.Where(fc => fc.FileDiffs.ChangeType is FileChangeType.Added).ToArray(),
            ModifiedFiles = filesClassifications.Where(fc => fc.FileDiffs.ChangeType is FileChangeType.Modified)
                .ToArray()
        };
        diagnostics.AddInformation(
            $"Files Classifications:\r\n{JsonConvert.SerializeObject(new
            {
                MinDiffClassificationType = pullRequestFileClassifications.MinDiffClassificationType.ToString(),
                pullRequestFileClassifications.AddedFiles,
                pullRequestFileClassifications.ModifiedFiles
            }, Formatting.Indented)}");

        sw.Stop();
        diagnostics.AddInformation($"Classified files in {sw.Elapsed}.");
        return pullRequestFileClassifications;
    }
}