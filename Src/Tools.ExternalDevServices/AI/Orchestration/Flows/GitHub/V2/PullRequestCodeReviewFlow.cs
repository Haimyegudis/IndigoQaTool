using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V2;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Flows.GitHub.V2;

public class PullRequestCodeReviewFlow
{
    public async Task<IReadOnlyCollection<FileCodeReview>> GenerateFileCodeReviewAsync(
        IChatClient chatClient,
        bool chatClientSupportsStructuredOutputWithoutExplicitSystemMessage,
        PullRequestOverview pullRequestOverview,
        ILogger? logger,
        DiagnosticsHelper? callerDiagnostics)
    {
        var codeReviews = new List<FileCodeReview>(pullRequestOverview.FilesClassifications.AddedFiles.Length +
                                                   pullRequestOverview.FilesClassifications.ModifiedFiles.Length);
        await foreach (var codeReview in GenerateFileCodeReviewAsAsyncEnumerable(chatClient,
                           chatClientSupportsStructuredOutputWithoutExplicitSystemMessage, 
                           pullRequestOverview,
                           pullRequestOverview.FilesClassifications.AddedFiles.Concat(
                               pullRequestOverview.FilesClassifications.ModifiedFiles),
                           logger,
                           callerDiagnostics))
        {
            codeReviews.Add(codeReview);
        }
        return codeReviews;
    }

    public async IAsyncEnumerable<FileCodeReview> GenerateFileCodeReviewAsAsyncEnumerable(
        IChatClient chatClient,
        bool chatClientSupportsStructuredOutputWithoutExplicitSystemMessage,
        PullRequestOverview pullRequestOverview,
        IEnumerable<FileDiffsClassification> filesClassifications,
        ILogger? logger,
        DiagnosticsHelper? callerDiagnostics)
    {
        using var diagnostics = new DiagnosticsHelper(GetType(), logger) { Parent = callerDiagnostics };
        diagnostics.AddInformation(
            $"Generating file code reviews for PR #{pullRequestOverview.FilesClassifications.PullRequest.Number} in {pullRequestOverview.FilesClassifications.PullRequest.Repo} repo.");

        var sw = Stopwatch.StartNew();

        var codeReviewAgent = new FileCodeReviewAgent();
        foreach (var filesClassification in filesClassifications)
        {
            yield return await codeReviewAgent.GenerateFileCodeReviewAsync(chatClient,
                chatClientSupportsStructuredOutputWithoutExplicitSystemMessage, pullRequestOverview,
                filesClassification, logger, callerDiagnostics);
        }

        sw.Stop();
        diagnostics.AddInformation(
            $"Generated file code reviews for PR #{pullRequestOverview.FilesClassifications.PullRequest.Number} in {pullRequestOverview.FilesClassifications.PullRequest.Repo} repo in {sw.Elapsed}.");
    }
}