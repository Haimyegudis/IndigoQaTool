using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Diagnostics;
using Tools.ExternalDevServices.AI.Orchestration.Utils;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V2;

public class PullRequestOverviewAgent
{
    public async Task<PullRequestOverview> GeneratePullRequestOverviewAsync(
        IChatClient chatClient,
        bool chatClientSupportsStructuredOutputWithoutExplicitSystemMessage,
        PullRequestFilesClassifications pullRequestFilesClassifications,
        ILogger? logger,
        DiagnosticsHelper? callerDiagnostics)
    {
        using var diagnostics = new DiagnosticsHelper(GetType(), logger) {Parent = callerDiagnostics};
        diagnostics.AddInformation(
            $"Generating pull request overview for PR #{pullRequestFilesClassifications.PullRequest.Number} in {pullRequestFilesClassifications.PullRequest.Repo} repo.");
        var sw = Stopwatch.StartNew();

        var chatOptions = ChatOptionsUtils.CreateGreedyChatOptions();
        chatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<PullRequestOverviewResponse>();

        var pullRequestJson = pullRequestFilesClassifications.ToJson();
        var overviewResponse = (await chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, Prompts.PullRequestOverviewSystemPrompt).WithDiagnostics(
                    nameof(Prompts.PullRequestOverviewSystemPrompt), diagnostics),
                ..Prompts.GetStructuredOutputChatMessage<PullRequestOverviewResponse>(
                        chatClientSupportsStructuredOutputWithoutExplicitSystemMessage).ToArray()
                    .WithDiagnostics("User Input", diagnostics),
                ChatMessageUtils.SystemChatMessages.MaxReasoning,
                new ChatMessage(ChatRole.User, pullRequestJson).WithDiagnostics("User Input", diagnostics)
            ], chatOptions))
            .RemoveThinking(out _);
        var pullRequestOverviewResponse = await ChatClientUtils.ToStructuredResponseAsync<PullRequestOverviewResponse>(chatClient, overviewResponse);

        var pullRequestOverview = new PullRequestOverview
        {
            PullRequestOverviewResponse = pullRequestOverviewResponse,
            FilesClassifications = pullRequestFilesClassifications
        };

        diagnostics.AddInformation(
            $"Pull Request Overview Response:\r\n{JsonConvert.SerializeObject(new
            {
                PullRequest = new
                {
                    pullRequestOverview.FilesClassifications.PullRequest.Title,
                    pullRequestOverview.FilesClassifications.PullRequest.Number,
                    pullRequestOverview.FilesClassifications.PullRequest.Repo,
                    pullRequestOverview.FilesClassifications.PullRequest.User,
                    Files = pullRequestOverview.FilesClassifications.PullRequest.DistinctFilesCount
                },
                pullRequestOverview.PullRequestOverviewResponse,
                FilesClassifications = new
                {
                    pullRequestOverview.FilesClassifications.AddedFiles,
                    pullRequestOverview.FilesClassifications.ModifiedFiles,
                    pullRequestOverview.FilesClassifications.DeletedFiles,
                    pullRequestOverview.FilesClassifications.MinDiffClassificationType

                }
            }, Formatting.Indented)}");

        sw.Stop();
        diagnostics.AddInformation($"Generated pull request overview in {sw.Elapsed}.");

        return pullRequestOverview;
    }
}