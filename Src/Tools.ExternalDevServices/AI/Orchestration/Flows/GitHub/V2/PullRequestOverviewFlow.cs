using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Newtonsoft.Json;
using Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V2;
using Tools.ExternalDevServices.Integrations.GitHub;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Flows.GitHub.V2;

public class PullRequestOverviewFlow
{
    public async Task<PullRequestOverview> GeneratePullRequestOverviewAsync(
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
            $"Generating pull request overview for PR #{pullRequestNumber} in {repo} repo.");

        var sw = Stopwatch.StartNew();

        var fileDiffsClassificationAgent = new FileDiffsClassificationAgent();
        var filesClassifications = await fileDiffsClassificationAgent.ClassifyPullRequestFilesAsync(chatClient,
            chatClientSupportsStructuredOutputWithoutExplicitSystemMessage, gitHubRestApiClient, repo,
            pullRequestNumber, logger, callerDiagnostics);

        var pullRequestOverviewAgent = new PullRequestOverviewAgent();
        var pullRequestOverview = await pullRequestOverviewAgent.GeneratePullRequestOverviewAsync(chatClient,
            chatClientSupportsStructuredOutputWithoutExplicitSystemMessage, filesClassifications, logger, callerDiagnostics);
        
        sw.Stop();
        diagnostics.AddInformation(
            $"Generated pull request overview for PR #{pullRequestNumber} in {repo} repo, duration: {sw.Elapsed}.");

        return pullRequestOverview;
    }
}