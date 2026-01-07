using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V2;
using Tools.ExternalDevServices.AI.Orchestration.Utils;
using Tools.ExternalDevServices.Integrations.GitHub;
using GitHubAddOrUpdatePullRequestOverviewCommentFlowV1 = Tools.ExternalDevServices.AI.Orchestration.Flows.GitHub.V1.GitHubAddOrUpdatePullRequestOverviewCommentFlow;
using GitHubAddOrUpdatePullRequestOverviewCommentFlowV2 = Tools.ExternalDevServices.AI.Orchestration.Flows.GitHub.V2.GitHubAddOrUpdatePullRequestOverviewCommentFlow;
using GitHubPullRequestCodeReviewFlowV1 = Tools.ExternalDevServices.AI.Orchestration.Flows.GitHub.V1.GitHubPullRequestCodeReviewFlow;
using GitHubPullRequestCodeReviewFlowV2 = Tools.ExternalDevServices.AI.Orchestration.Flows.GitHub.V2.PullRequestCodeReviewFlow;
using GitHubPullRequestOverviewCommentFlowV1 = Tools.ExternalDevServices.AI.Orchestration.Flows.GitHub.V1.GitHubPullRequestOverviewCommentFlow;
using GitHubPullRequestOverviewCommentFlowV2 = Tools.ExternalDevServices.AI.Orchestration.Flows.GitHub.V2.PullRequestOverviewFlow;
using PullRequestReviewPlanAgentV2 = Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V2.PullRequestReviewPlanAgent;
using PullRequestReviewMarkdownUtilsV1 = Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V1.PullRequestReviewMarkdownUtils;
namespace Tools.ExternalDevServices.AI.MCP.GitHubMcp;

[McpServerToolType]
public static class PullRequestsReviewTool
{
    public class PullRequestDetailsResponse
    {
        public string Repo { get; set; } = "";
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public string? PullRequestOverview { get; set; }
        public string User { get; set; } = ""; 
        public string FromBranch { get; set; } = "";
        public string ToBranch { get; set; } = "";
        public string[] LinkedJiraIssues { get; set; } = [];
        public PullRequestLifeCycle LifeCycle { get; set; } = null!;
        public PullRequestFile[] Files { get; set; } = [];

        public static PullRequestDetailsResponse FromPullRequestDetails(PullRequestDetails pullRequestDetails)
        {
            var files = pullRequestDetails.PullRequestFiles;

            var overview = pullRequestDetails.Comments.FirstOrDefault(c =>
                c.Body.StartsWith(PullRequestCommentUtils.AiGeneratedPullRequestOverviewCommentMarker))?.Body;

            return new PullRequestDetailsResponse
            {
                Repo = pullRequestDetails.Repo,
                Number = pullRequestDetails.Number,
                Title = pullRequestDetails.Title,
                Body = pullRequestDetails.Body ?? "",
                PullRequestOverview = overview,
                User = pullRequestDetails.User,
                FromBranch = pullRequestDetails.Branches.From.Name,
                ToBranch = pullRequestDetails.Branches.To.Name,
                LinkedJiraIssues = pullRequestDetails.LinkedJiraIssues,
                LifeCycle = pullRequestDetails.LifeCycle,
                Files = files.Select(file =>
                    new PullRequestFile
                    {
                        Name = file.FileName,
                        PreviousFileName = file.PreviousFilename,
                        Status = file.CommitFileStatus
                    }).ToArray()
            };
        }
    }

    public class PullRequestFile
    {
        public string Name { get; set; } = "";
        public string? PreviousFileName { get; set; }
        public CommitFileStatus Status { get; set; }
    }

    public class PullRequestOverviewResponse
    {
        public string Summary { get; set; } = "";
        public string[] KeyChangesAndAdditions { get; set; } = [];
        public FileClassification[] FilesClassifications { get; set; } = [];
        public string ReviewPlan { get; set; } = "";
    }

    public class FileClassification
    {
        public string FileName { get; set; } = "";
        public FileChangeType ChangeType { get; set; }
        public DiffClassification[] Classifications { get; set; } = [];
    }

    public class DiffClassificationValue
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    private const int CacheThresholdInMinutes = 10;
    
    private static readonly Dictionary<(int pullRequestNumber, string repo), (PullRequestDetails pullRequestDetails,
            DateTime createdTime)> PullRequestsDetailsCache = new();

    private static readonly Dictionary<(int pullRequestNumber, string repo), (PullRequestOverviewResponse pullRequestOverviewResponse,
            DateTime createdTime)> PullRequestsOverviewCache = new();

    [McpServerTool, Description("Gets diff classifications values, where values represent priorities (highest priority has lowest value).")]
    public static DiffClassificationValue[] GetDiffClassificationValues() =>
        Enum.GetValues<DiffClassificationType>().Except([DiffClassificationType.None, DiffClassificationType.MaxValue])
            .Select(d => new DiffClassificationValue{Name = d.ToString(), Value = (int)d})
            .ToArray();

    [McpServerTool, Description("Gets pull request details.")]
    public static async Task<PullRequestDetailsResponse> GetPullRequestDetailsAsync(string repo, int pullRequestNumber)
    {
        if (PullRequestsDetailsCache.TryGetValue((pullRequestNumber, repo), out var cachedResponse) &&
            cachedResponse.createdTime < DateTime.UtcNow.AddMinutes(-CacheThresholdInMinutes))
        {
            return PullRequestDetailsResponse.FromPullRequestDetails(cachedResponse.pullRequestDetails);
        }
        
        Validate(out var personalAccessToken);
        using var gitHubRestApiClient = new GitHubRestApiClient(personalAccessToken,
            HostArguments.ApiBaseUrl, HostArguments.Owner);
        var pullRequestDetails = await gitHubRestApiClient.GetPullRequestDetailsAsync(repo, pullRequestNumber);
        PullRequestsDetailsCache[(pullRequestNumber, repo)] =
            (pullRequestDetails, DateTime.UtcNow);

        return PullRequestDetailsResponse.FromPullRequestDetails(pullRequestDetails);
    }

    [McpServerTool, Description("Gets pull request overview and file classifications.")]
    public static async Task<PullRequestOverviewResponse> GetPullRequestOverviewAsync(McpServer mcpServer, string repo, int pullRequestNumber)
    {
        if (PullRequestsOverviewCache.TryGetValue((pullRequestNumber, repo), out var cachedResponse) &&
            cachedResponse.createdTime < DateTime.UtcNow.AddMinutes(-CacheThresholdInMinutes))
        {
            return cachedResponse.pullRequestOverviewResponse;
        }

        ValidateForSampling(mcpServer, out var personalAccessToken, out var samplingChatClient);
        var loggerFactory = mcpServer.Services?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        var logger = loggerFactory?.CreateLogger(nameof(PullRequestsReviewTool));
        using var gitHubRestApiClient = new GitHubRestApiClient(personalAccessToken,
            HostArguments.ApiBaseUrl, HostArguments.Owner);
        
        if (HostArguments.PullRequestReviewVersion != PullRequestReviewVersion.V2)
            throw new McpException($"Version {HostArguments.PullRequestReviewVersion} is not supported");

        var overviewFlow = new GitHubPullRequestOverviewCommentFlowV2();
        var overview = await overviewFlow.GeneratePullRequestOverviewAsync(samplingChatClient,
            chatClientSupportsStructuredOutputWithoutExplicitSystemMessage: false,
            gitHubRestApiClient,
            repo,
            pullRequestNumber,
            logger,
             callerDiagnostics: null);

        var codeReviewFlow = new GitHubPullRequestCodeReviewFlowV2();
        var codeReviewResponse =
            await codeReviewFlow.GenerateFileCodeReviewAsync(samplingChatClient,
                chatClientSupportsStructuredOutputWithoutExplicitSystemMessage: false,
                overview,
                logger,
                callerDiagnostics: null);

        var reviewPlanAgent = new PullRequestReviewPlanAgentV2();
        var reviewPlan = await reviewPlanAgent.GeneratePullRequestReviewPlanAsync(samplingChatClient,
            chatClientSupportsStructuredOutputWithoutExplicitSystemMessage: false,
            overview,
            codeReviewResponse,
            logger,
            callerDiagnostics: null);

        var overviewResponse = new PullRequestOverviewResponse
        {
            Summary = overview.PullRequestOverviewResponse.Summary,
            KeyChangesAndAdditions = overview.PullRequestOverviewResponse.KeyChangesAndAdditions,
            FilesClassifications = overview.FilesClassifications.AddedFiles
                .Concat(overview.FilesClassifications.ModifiedFiles)
                .Select(f => new FileClassification
                {
                    FileName = f.FileName,
                    ChangeType = f.FileDiffs.ChangeType,
                    Classifications = f.DiffClassifications
                })
                .Concat(overview.FilesClassifications.DeletedFiles.Select(f => new FileClassification
                {
                    FileName = f,
                    ChangeType = FileChangeType.Deleted,
                    Classifications = []
                })).ToArray(),
            ReviewPlan = reviewPlan
        };

        PullRequestsOverviewCache[(pullRequestNumber, repo)] =
            (overviewResponse, DateTime.UtcNow);
        return overviewResponse;
    }

    [McpServerTool(Name = "write-pr-overview-comment"), Description("Adds or updates pull request overview comment.")]
    public static async Task<string> AddOrUpdatePullRequestOverviewCommentAsync(McpServer mcpServer, string repo, int pullRequestNumber)
    {
        ValidateForSampling(mcpServer, out var personalAccessToken, out var samplingChatClient);
        var loggerFactory = mcpServer.Services?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        var logger = loggerFactory?.CreateLogger(nameof(PullRequestsReviewTool));
        using var gitHubRestApiClient = new GitHubRestApiClient(personalAccessToken,
            HostArguments.ApiBaseUrl, HostArguments.Owner);
        switch (HostArguments.PullRequestReviewVersion)
        {
            case PullRequestReviewVersion.V1:
                var flow = new GitHubAddOrUpdatePullRequestOverviewCommentFlowV1(samplingChatClient, logger);
                return await flow.AddOrUpdatePullRequestOverviewCommentAsync(gitHubRestApiClient, repo, pullRequestNumber, maxContextSizeInTokens: null);
            case PullRequestReviewVersion.V2:
                var flowV2 = new GitHubAddOrUpdatePullRequestOverviewCommentFlowV2();
                return (await flowV2.AddOrUpdatePullRequestOverviewCommentAsync(gitHubRestApiClient, samplingChatClient,
                    chatClientSupportsStructuredOutputWithoutExplicitSystemMessage: false, repo, pullRequestNumber,
                    logger)).Comment;
            default:
                throw new McpException($"Version {HostArguments.PullRequestReviewVersion} is not supported");
        }
    }

    [McpServerTool, Description("Gets the diffs array for a file in a pull request. The response is a collection of individual unified diffs of the file if it was changed, or the contents of the file if it was added. If a file was deleted, the response is empty.")]
    public static async Task<string[]> GetPullRequestFileDiffsForReviewAsync(McpServer mcpServer, string repo, int pullRequestNumber, string file)
    {
        Validate(out var personalAccessToken);

        if (!PullRequestsDetailsCache.TryGetValue((pullRequestNumber, repo), out var cachedResponse) ||
            cachedResponse.createdTime < DateTime.UtcNow.AddMinutes(-CacheThresholdInMinutes))
        {
            await GetPullRequestDetailsAsync(repo, pullRequestNumber);
        }

        using var gitHubRestApiClient = new GitHubRestApiClient(personalAccessToken,
            HostArguments.ApiBaseUrl, HostArguments.Owner);
        var pullRequestDetails = PullRequestsDetailsCache[(pullRequestNumber, repo)].pullRequestDetails;
        var diffs = await gitHubRestApiClient.GetFileDiffsAsync(pullRequestDetails, file,
            addCsharpContext: file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
        return diffs.ChangeType is FileChangeType.Deleted ? [] : diffs.Diffs.ToArray();
    }

    [McpServerTool, Description("Gets the full diff (a unified diff of the entire file) for a file in a pull request. If a file was deleted, the response is empty.")]
    public static async Task<string> GetPullRequestFileFullDiffForReviewAsync(McpServer mcpServer, string repo, int pullRequestNumber, string file)
    {
        Validate(out var personalAccessToken);

        if (!PullRequestsDetailsCache.TryGetValue((pullRequestNumber, repo), out var cachedResponse) ||
            cachedResponse.createdTime < DateTime.UtcNow.AddMinutes(-CacheThresholdInMinutes))
        {
            await GetPullRequestDetailsAsync(repo, pullRequestNumber);
        }

        using var gitHubRestApiClient = new GitHubRestApiClient(personalAccessToken,
            HostArguments.ApiBaseUrl, HostArguments.Owner);
        var pullRequestDetails = await gitHubRestApiClient.GetPullRequestDetailsAsync(repo, pullRequestNumber);
        var diffs = await gitHubRestApiClient.GetFileDiffsAsync(pullRequestDetails, file,
            addCsharpContext: file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
        return diffs.ChangeType is FileChangeType.Deleted? "" : diffs.FullDiff;
    }

    [McpServerTool, Description("Creates a comprehensive pull request review as a markdown file and returns its path.")]
    public static async Task<string> CreateComprehensivePullRequestReviewAsync(McpServer mcpServer, string repo,
        int pullRequestNumber)
    {
        ValidateForSampling(mcpServer, out var personalAccessToken, out var samplingChatClient);
        var loggerFactory = mcpServer.Services?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        var logger = loggerFactory?.CreateLogger(nameof(PullRequestsReviewTool));
        using var gitHubRestApiClient = new GitHubRestApiClient(personalAccessToken,
            HostArguments.ApiBaseUrl, HostArguments.Owner);

        switch (HostArguments.PullRequestReviewVersion)
        {
            case PullRequestReviewVersion.V1:
                return await CreateComprehensivePullRequestReviewAsyncV1(samplingChatClient, logger, gitHubRestApiClient, repo, pullRequestNumber);
            case PullRequestReviewVersion.V2:
                return await CreateComprehensivePullRequestReviewAsyncV2(samplingChatClient, logger, gitHubRestApiClient, repo, pullRequestNumber); 
            default:
                throw new McpException($"Version {HostArguments.PullRequestReviewVersion} is not supported");
        }
    }

    private static async Task<string> CreateComprehensivePullRequestReviewAsyncV1(IChatClient samplingChatClient,
        ILogger? logger,
        GitHubRestApiClient gitHubRestApiClient,
        string repo,
        int pullRequestNumber)
    {
        var overviewCommentFlow = new GitHubPullRequestOverviewCommentFlowV1(samplingChatClient, logger);
        var overviewCommentResponse =
            await overviewCommentFlow.GeneratePullRequestOverviewCommentAsync(gitHubRestApiClient, repo,
                pullRequestNumber, maxContextSizeInTokens: 131072);

        var codeReviewFlow = new GitHubPullRequestCodeReviewFlowV1(samplingChatClient, logger);
        var codeReviewResponse =
            await codeReviewFlow.GeneratePullRequestCodeReviewsAsync(overviewCommentResponse);

        var review = PullRequestReviewMarkdownUtilsV1.GetPullRequestReviewMarkdown(overviewCommentResponse, codeReviewResponse);
        var reportsFolder = $@"Reports\{repo}_PullRequest_{pullRequestNumber}_Review_{DateTime.Now:yyyy_MM_dd_HH_mm}";
        Directory.CreateDirectory(reportsFolder);
        var reviewFilePath = Path.Combine(reportsFolder, $"PullRequest_{pullRequestNumber}_Review.md");
        await File.WriteAllTextAsync(reviewFilePath, review);

        return
            $"Review created. Markdown file: {Path.GetFullPath(reviewFilePath)}";
    }

    private static async Task<string> CreateComprehensivePullRequestReviewAsyncV2(IChatClient samplingChatClient,
        ILogger? logger,
        GitHubRestApiClient gitHubRestApiClient,
        string repo,
        int pullRequestNumber)
    {
        var addOrUpdatePullRequestOverviewFlow =
            new GitHubAddOrUpdatePullRequestOverviewCommentFlowV2();

        return (await addOrUpdatePullRequestOverviewFlow.GeneratePullRequestOverviewCommentAsync(gitHubRestApiClient,
            samplingChatClient, chatClientSupportsStructuredOutputWithoutExplicitSystemMessage: false, repo,
            pullRequestNumber, logger, diagnostics: null)).Comment;
    }

    private static void ValidateForSampling(McpServer mcpServer,
        out string personalAccessToken,
        out IChatClient samplingChatClient)
    {
        Validate(out personalAccessToken);

        if (mcpServer.ClientCapabilities?.Sampling is null)
        {
            throw new McpException("Sampling is not supported.");
        }

        samplingChatClient = mcpServer.AsSamplingChatClient();
        if (samplingChatClient is null)
        {
            throw new McpException("Sampling is not supported.");
        }
    }

    private static void Validate(out string personalAccessToken)
    {
        personalAccessToken = "";

        if (string.IsNullOrWhiteSpace(HostArguments.PersonalAccessToken))
        {
            throw new InvalidOperationException("PersonalAccessToken is not set.");
        }
        personalAccessToken = HostArguments.PersonalAccessToken;
    }
}