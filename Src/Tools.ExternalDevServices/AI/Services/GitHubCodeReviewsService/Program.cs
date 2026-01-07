using System.ClientModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using OpenAI;
using Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V2;
using Tools.ExternalDevServices.AI.Orchestration.Flows.GitHub.V2;
using Tools.ExternalDevServices.AI.Orchestration.Utils;
using Tools.ExternalDevServices.Integrations.GitHub;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Services.GitHubCodeReviewsService;

internal class Program
{
    private static async Task Main()
    {
        Console.WriteLine($"[{DateTime.Now:dd MMM yyyy HH:mm:ss}] GitHub Code Reviews Service started.");
        
        const string configFile = "Config.json";
        var config = await Config.LoadAsync(configFile);

        DiagnosticsHelper.ConsoleEnabled = config.DiagnosticsConsoleEnabled;

        using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (true)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(
                    $"[{DateTime.Now:dd MMM yyyy HH:mm:ss}] Checking for pull requests waiting for review...");
                Console.ResetColor();

                using var githubRestApiClient =
                    new GitHubRestApiClient(config.GitHubPersonalAccessToken, config.Organization.Url, config.Organization.Owner);
                var user = await githubRestApiClient.GetAuthenticatedUserAsync();
                
                var pullRequestsWaitingForMyReview =
                    config.DebugModeEnabled
                        ? config.DebugPullRequests.Select(prInfo => (prInfo.Number, prInfo.Repo)).ToArray()
                        : await githubRestApiClient.GetPullRequestsWaitingForMyReviewAsync();
                if (pullRequestsWaitingForMyReview.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[{DateTime.Now:dd MMM yyyy HH:mm:ss}] No pull requests waiting for review.");
                    Console.ResetColor();
                    continue;
                }

                Console.WriteLine(
                    $"[{DateTime.Now:dd MMM yyyy HH:mm:ss}] Found {pullRequestsWaitingForMyReview.Count} pull requests waiting for review.");

                var chatClient = config.Model.HostType switch
                {
                    LlmHostType.LMStudio => new OpenAIClient(
                        new ApiKeyCredential("no_key_is_needed"),
                        new OpenAIClientOptions
                        {
                            Endpoint = new Uri(config.Model.HostUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                                ? config.Model.HostUrl
                                : $"{config.Model.HostUrl}/v1"),
                            NetworkTimeout = TimeSpan.FromHours(1)
                        }).GetChatClient(config.Model.ModelId).AsIChatClient(),
                    LlmHostType.Ollama => new OllamaChatClient(config.Model.HostUrl, config.Model.ModelId),
                    _ => throw new ArgumentOutOfRangeException(nameof(config.Model.HostType), config.Model.HostType,
                        "Unsupported host type")
                };

                //Add comments placeholders
                var prsMap = new Dictionary<int, PullRequestDetails>();
                foreach (var pullRequest in pullRequestsWaitingForMyReview)
                {
                    var pullRequestDetails =
                        await githubRestApiClient.GetPullRequestDetailsAsync(pullRequest.repo, pullRequest.number);
                    prsMap.Add(pullRequest.number, pullRequestDetails);
                    if (!config.AddPlaceholderComment || config.DebugModeEnabled || PullRequestCommentUtils.HasCommentWithPrefix(
                            pullRequestDetails,
                            PullRequestCommentUtils.AiGeneratedPullRequestOverviewCommentMarker)) continue;

                    Console.WriteLine(
                        $"[{DateTime.Now:dd MMM yyyy HH:mm:ss}] Adding comment placeholder for pull request #{pullRequest}");
                    await PullRequestCommentUtils.AddOrUpdateCommentWithPrefixMarkerAndPostfixToPullRequestAsync(
                        githubRestApiClient, pullRequestDetails, false,
                        $"<sub>{user.Name ?? user.Login} is generating a pull request overview...</sub>",
                        PullRequestCommentUtils.AiGeneratedPullRequestOverviewCommentMarker,
                        PullRequestCommentUtils.AiGeneratedPullRequestOverviewCommentDisclaimer, null);
                }

                foreach (var pullRequest in pullRequestsWaitingForMyReview)
                {
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        Console.WriteLine(
                            $"[{DateTime.Now:dd MMM yyyy HH:mm:ss}] Getting pull request details for pull request #{pullRequest} in repo {pullRequest.repo}");
                        var pullRequestDetails = prsMap[pullRequest.number];

                        Console.WriteLine(
                            $"Reviewing pull request '{pullRequestDetails.Title}' #{pullRequestDetails.Number} in repo {pullRequestDetails.Repo} by author {pullRequestDetails.User}...");
                        var addOrUpdatePullRequestOverviewCommentFlow =
                            new GitHubAddOrUpdatePullRequestOverviewCommentFlow
                            {
                                GenerateFileCodeReviewsForReviewPlan = true,
                                PostCommentAsReviewer = false,
                                DebugModeEnabled = config.DebugModeEnabled,
                                Progress = progress =>
                                {
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.WriteLine($"[{DateTime.Now:dd MMM yyyy HH:mm:ss}] {progress}");
                                    Console.ResetColor();
                                }
                            };
                        var overviewResponse = await addOrUpdatePullRequestOverviewCommentFlow.AddOrUpdatePullRequestOverviewCommentAsync(
                            githubRestApiClient,
                            chatClient,
                            chatClientSupportsStructuredOutputWithoutExplicitSystemMessage: true,
                            pullRequestDetails.Repo,
                            pullRequestDetails.Number,
                            null);
                        Console.WriteLine(
                            $"[{DateTime.Now:dd MMM yyyy HH:mm:ss}] Finished reviewing pull request '{pullRequestDetails.Title}' #{pullRequestDetails.Number} in repo {pullRequestDetails.Repo}.");

                        var pullRequestOutputFolder = await WriteResponseToOutputFolderAsync(config.OutputFolder, overviewResponse);
                        Console.WriteLine(
                            $"[{DateTime.Now:dd MMM yyyy HH:mm:ss}] Written pull request overview and code reviews to output folder: {pullRequestOutputFolder}");

                        if (config.DebugModeEnabled) continue;

                        var review = await githubRestApiClient.AddReviewToPullRequestAsync(pullRequestDetails.Repo,
                            pullRequestDetails.Number,
                            $"**{user.Name ?? user.Login}** reviewed {pullRequestDetails.DistinctFilesCount} files and generated a pull request overview comment using model *{config.Model.ModelId}* (hosted on host {config.Model.HostUrl} via {config.Model.HostType})");

                        Console.WriteLine(
                            $"[{DateTime.Now:dd MMM yyyy HH:mm:ss}] Review (Id: {review.id} submitted for pull request '{pullRequestDetails.Title}' #{pullRequestDetails.Number} in repo {pullRequestDetails.Repo} using model {config.Model.ModelId} ({config.Model.HostType}) on host {config.Model.HostUrl}, Url: {review.url}.");
                        Console.WriteLine($"[{DateTime.Now:dd MMM yyyy HH:mm:ss}] Time taken: {sw.Elapsed}");
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[{DateTime.Now:dd MMM yyyy HH:mm:ss}]\r\n{ex}");
                        Console.ResetColor();

                        var review = await githubRestApiClient.AddReviewToPullRequestAsync(pullRequest.repo,
                            pullRequest.number,
                            $"{user.Name ?? user.Login} failed reviewing pull request #{pullRequest} using model *{config.Model.ModelId}* (hosted on host {config.Model.HostUrl} via {config.Model.HostType}), **check with AI focals for more details**");

                        Console.WriteLine(
                            $"[{DateTime.Now:dd MMM yyyy HH:mm:ss}] Failed review (Id: {review.id} submitted for pull request #{pullRequest} in repo {pullRequest.repo}, Url: {review.url}.");
                    }
                }

                Console.WriteLine(
                    $"[{DateTime.Now:dd MMM yyyy HH:mm:ss}] Finished processing pull requests for current cycle.");

                if(config.DebugModeEnabled) break;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[{DateTime.Now:dd MMM yyyy HH:mm:ss}]\r\n{ex}");
                Console.ResetColor();
            }
            finally
            {
                await Task.Delay(TimeSpan.FromSeconds(config.PollingIntervalInSeconds));
            }
        }
    }

    private static async Task<string> WriteResponseToOutputFolderAsync(string outputFolder, GitHubAddOrUpdatePullRequestOverviewCommentResponse overviewResponse)
    {
         var prOutputFolder = Directory.CreateDirectory(Path.Combine(outputFolder,
            overviewResponse.PullRequestOverview.FilesClassifications.PullRequest.Repo,
            overviewResponse.PullRequestOverview.FilesClassifications.PullRequest.Number.ToString())).FullName;

        Console.ForegroundColor = ConsoleColor.Red;

        try
        {
            var overviewFilePath = Path.Combine(prOutputFolder, "PullRequestOverview.md");
            await File.WriteAllTextAsync(overviewFilePath, overviewResponse.Comment);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:dd MMM yyyy HH:mm:ss}] Error writing pull request overview comment: {ex}");
        }

        await Parallel.ForEachAsync(overviewResponse.FileCodeReviews, async (fileCodeReview, ct) =>
        {
            try
            {
                var filePath = Path.Combine(prOutputFolder,
                        fileCodeReview.File.FileName.Replace('/', '_').Replace(' ', '_').Replace('.', '_') + ".md");
                await File.WriteAllTextAsync(filePath, fileCodeReview.ToCodeReviewMarkdown(), ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:dd MMM yyyy HH:mm:ss}] Error processing file {fileCodeReview.File.FileName}: {ex}");
            }
        });

        Console.ResetColor();
        return prOutputFolder;
    }
}