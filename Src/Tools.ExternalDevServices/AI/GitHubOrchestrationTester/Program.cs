using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text;
using CDB;
using JetBrains.Annotations;
using Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V1;
using Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V2;
using Tools.ExternalDevServices.AI.Orchestration.Flows.GitHub.V1;
using Tools.ExternalDevServices.AI.Orchestration.Utils;
using Tools.ExternalDevServices.Integrations.Confluence;
using Tools.ExternalDevServices.Integrations.GitHub;
using Tools.ExternalDevServices.Integrations.Jira;
using FileCodeReview = Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V1.FileCodeReview;

// ReSharper disable UnusedVariable

namespace GitHubOrchestrationTester;

internal class Program
{
    private static async Task Main()
    {
        var inst = Prompts.DiffClassificationTypeEnumInstructions;
        
        Console.WriteLine("Enter GitHub API token:");
        var githubApiToken = Console.ReadLine()?.Trim() ??
                             throw new InvalidOperationException("GitHub API token is required.");
        using var githubRestApiClient = new GitHubRestApiClient(githubApiToken, "https://github.azc.ext.hp.com/api/v3", "Indigo-RnD");

        var pullRequestDetails = await githubRestApiClient.GetPullRequestDetailsAsync("S6-PC", 1615);

        Console.WriteLine("Enter Confluence API token:");
        var confluenceApiToken = Console.ReadLine()?.Trim() ??
                                 throw new InvalidOperationException("Confluence API token is required.");

        using var confluenceRestApiClient = new ConfluenceRestApiClient(
            "https://v-indigo-confluence.inr.rd.hpicorp.net:6443", confluenceApiToken);
        var html = await confluenceRestApiClient.GetDocumentHtmlByUrlAsync(
            "https://v-indigo-confluence.inr.rd.hpicorp.net:6443/display/CPUX/Reflection+of+Messages-UI-EN-US.json");

        Console.WriteLine("Tester started");

        Console.WriteLine("Enter Jira API token:");
        var jiraApiToken = Console.ReadLine()?.Trim() ??
                           throw new InvalidOperationException("Jira API token is required.");

        Console.WriteLine("Enter Jira user:");
        var jiraUser = Console.ReadLine()?.Trim() ??
                       throw new InvalidOperationException("Jira user is required.");

        var cdb = new CdbInstance();
        var dumpInfo = await cdb.LoadDumpFileAsync(@"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe",
            @"C:\Temp\Logs\Client_disconnected_during_printing\Press.Host.AppDev.DMP");

        while (true)
        {
            Console.WriteLine("Enter CDB command:");
            var command = Console.ReadLine();
            if(string.IsNullOrEmpty(command))
                break;

            Console.WriteLine(await cdb.SendCommandAsync(command));
        }

        using var jiraClient = new JiraRestApiClient("https://hp-jira.external.hp.com", "2", jiraUser,
            jiraApiToken);
        var issue = await IssueInfo.GetIssueInformationAsync(jiraClient, "ISW-51053");

        var document =
            await confluenceRestApiClient.GetDocumentMetadataAndMarkdownByUrlAsync(
                issue.AllConfluenceLinksArray.First());

        //DebugDiffResolver();

        string? pta;
        do
        {
            Console.WriteLine("Enter PTA:");
            pta = Console.ReadLine()?.Trim();
        } while (string.IsNullOrWhiteSpace(pta));

        Console.WriteLine("Enter host url, or leave blank for http://hila-aaasim:443");
        var host = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(host))
            host = "http://hila-aaasim:443";

        Console.WriteLine("Enter model, 1 for gpt-oss-20b, 2 for qwen/qwen3-coder-30b, 3 for mistralai/devstral-small-2507 or leave blank for openai/gpt-oss-20b");
        var model = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(model) || model == "1")
        {
            model = "openai/gpt-oss-20b";
            Console.WriteLine("Model set to openai/gpt-oss-20b");
        }
        else if (model == "2")
        {
            model = "qwen/qwen3-coder-30b";
            Console.WriteLine("Model set to qwen/qwen3-coder-30b");
        }
        else if (model == "3")
        {
            model = "mistralai/devstral-small-2507";
            Console.WriteLine("Model set to mistralai/devstral-small-2507");
        }

        int? contextSizeInKb;
        do
        {
            Console.WriteLine("Enter context size in KB:");
            var input = Console.ReadLine()?.Trim();
            contextSizeInKb = int.TryParse(input, out var size) ? size : null;
        } while (!contextSizeInKb.HasValue);

        contextSizeInKb = contextSizeInKb.Value * 1024;
        Console.WriteLine($"Context set to {contextSizeInKb.Value} tokens");

        using var gitHubRestApiClient =
            new GitHubRestApiClient(pta, "https://github.azc.ext.hp.com/api/v3", "Indigo-RnD");

        //await DebugAddReviewComment(gitHubRestApiClient);

        while (true)
        {
            try
            {
                int? pr;
                do
                {
                    Console.WriteLine("Enter PR number, or type 'q' to quit:");
                    var input = Console.ReadLine()?.Trim();
                    if ("q".Equals(input, StringComparison.OrdinalIgnoreCase)) Environment.Exit(0);
                    pr = int.TryParse(input, out var prNum) ? prNum : null;
                } while (!pr.HasValue);

                using var httpClient = new HttpClient();
                httpClient.Timeout = Timeout.InfiniteTimeSpan;
                var openAiClient = new OpenAIClient(new ApiKeyCredential("no_key_is_needed"), new OpenAIClientOptions
                {
                    Endpoint = new Uri($"{host.TrimEnd('/')}/v1"),
                    Transport = new HttpClientPipelineTransport(httpClient),
                    NetworkTimeout = Timeout.InfiniteTimeSpan
                });
                var chatClient = openAiClient.GetChatClient(model)
                    .AsIChatClient()
                    .AsBuilder()
                    .Build();
                var sw = Stopwatch.StartNew();
                var pullRequestOverviewCommentResponse =
                    await GetPullRequestOverviewCommentResponseAsync(gitHubRestApiClient: gitHubRestApiClient, 
                        model: model, 
                        pta: pta, 
                        chatClient: chatClient,
                        contextSizeInKb: contextSizeInKb, 
                        prNumber: pr.Value);
                sw.Stop();

                var groupedClassifications = pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.ClassifiedFiles
                    .GroupBy(c => c.DiffClassificationResponse.DiffChangeType)
                    .OrderBy(g => g.Key)
                    .ToArray();

                Console.WriteLine(
                    $"Generate overview duration: {sw.Elapsed}, Classifications:{string.Join(", ", groupedClassifications.Select(g => $"{g.Key}: {g.Count()}"))}");
                Console.WriteLine("Press enter to continue");
                Console.ReadLine();
                Console.Clear();

                Console.WriteLine($"Overview Comment:\r\n{pullRequestOverviewCommentResponse.OverviewCommentForHumanReviewers}");

                Console.WriteLine("Add/Update Pull Request Overview comment to PR? (Y/N)");
                var addComment = Console.ReadLine()?.Trim() ?? "";
                if (addComment.Equals("Y", StringComparison.OrdinalIgnoreCase))
                {
                    var commentResponse = await PullRequestCommentUtils
                        .AddOrUpdateCommentWithPrefixMarkerAndPostfixToPullRequestAsync(
                            gitHubRestApiClient,
                            pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.PullRequest,
                            postCommentAsReviewer: false,
                            comment: pullRequestOverviewCommentResponse.OverviewCommentForHumanReviewers,
                            prefix: PullRequestCommentUtils.AiGeneratedPullRequestOverviewCommentMarker,
                            postfix: PullRequestCommentUtils.AiGeneratedPullRequestOverviewCommentDisclaimer,
                            diagnostics: null);
                    Console.WriteLine($"Comment added/updated: {commentResponse}");
                }

                Console.WriteLine("Press enter to continue");
                Console.ReadLine();
                Console.Clear();

                Console.WriteLine($"Summary for LLM reviewer:\r\n{pullRequestOverviewCommentResponse.OverviewForLLMReviewers}");
                Console.WriteLine("Press enter to continue");
                Console.ReadLine();
                Console.Clear();

                bool continueToClassifications;
                while (true)
                {
                    Console.WriteLine("Review files classifications? (Y/N/Empty for No)");
                    var response = Console.ReadLine()?.Trim() ?? "";
                    if (response.Equals("Y", StringComparison.OrdinalIgnoreCase))
                    {
                        continueToClassifications = true;
                        break;
                    }

                    if (string.IsNullOrEmpty(response) || response.Equals("N", StringComparison.OrdinalIgnoreCase))
                    {
                        continueToClassifications = false;
                        break;
                    }

                    Console.WriteLine($"Response '{response}' is not a valid response");
                }

                if (continueToClassifications)
                {
                    Console.Clear();
                    foreach (var groupedClassification in groupedClassifications)
                    {
                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine($"Classification: {groupedClassification.Key}, Count: {groupedClassification.Count()}");
                        Console.ResetColor();
                        foreach (var classification in groupedClassification)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine(classification.File.FileName);
                            Console.ResetColor();

                            Console.WriteLine(classification.DiffClassificationResponse);
                            Console.WriteLine(classification.Diff);
                            Console.WriteLine("\r\n\r\n");
                        }

                        Console.WriteLine("Press enter to continue");
                        Console.ReadLine();
                    }
                }

                Console.Clear();
                Console.WriteLine("Generating code reviews, Press enter to continue");
                Console.ReadLine();

                var gitHubPullRequestCodeReviewFlow = new GitHubPullRequestCodeReviewFlow(chatClient, logger: null);
                var pullRequestFilesCodeReviewResponse = await gitHubPullRequestCodeReviewFlow.GeneratePullRequestCodeReviewsAsync(
                    pullRequestOverviewCommentResponse);

                var codeReviews = pullRequestFilesCodeReviewResponse.ClassifiedFilesCodeReview
                    .Where(c => !c.FileCodeReview.IsEmpty).ToArray();
                Console.WriteLine($"Code Reviews: {codeReviews.Length}");
                if(codeReviews.Length == 0) continue;

                foreach (var classifiedFileCodeReview in codeReviews)
                {
                    Console.Clear();
                    Console.WriteLine($"[{classifiedFileCodeReview.ClassifiedFile.FileChangeType}] {classifiedFileCodeReview.ClassifiedFile.File.FileName}");
                    Console.WriteLine($"Type: {classifiedFileCodeReview.ClassifiedFile.DiffClassificationResponse.DiffChangeType}");
                    Console.WriteLine($"Summary: {classifiedFileCodeReview.ClassifiedFile.DiffClassificationResponse.DiffSummary}");
                    Console.WriteLine();

                    foreach (var codeComment in classifiedFileCodeReview.FileCodeReview.CodeComments)
                    {
                        Console.WriteLine(
                            $"File: {classifiedFileCodeReview.ClassifiedFile.File.FileName}\r\nLine: {codeComment.Line}\r\nComment: {codeComment.Comment}\r\nFix Suggestion: {codeComment.FixSuggestion}");
                        Console.WriteLine();
                        Console.WriteLine("Add code comment to pull request? (Y/N)");
                        var addCodeComment = Console.ReadLine()?.Trim() ?? "";
                        if (!addCodeComment.Equals("Y", StringComparison.OrdinalIgnoreCase)) continue;

                        var comment = string.IsNullOrWhiteSpace(codeComment.FixSuggestion)
                            ? codeComment.Comment
                            : $"{codeComment.Comment}\r\n\r\n**Fix Suggestion**:\r\n{codeComment.FixSuggestion}";
                        var formattedComment = $"{PullRequestCommentUtils.AiGeneratedPullRequestCodeCommentMarker}\r\n\r\n{comment}\r\n\r\n{PullRequestCommentUtils.AiGeneratedPullRequestCodeCommentDisclaimer}";
                        var reviewComment = await gitHubRestApiClient.AddFileCodeReviewCommentAsync(
                            pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.PullRequest,
                            classifiedFileCodeReview.ClassifiedFile.File.FileName, codeComment.Line,
                            formattedComment);
                        Console.WriteLine($"Review comment added:\r\n{JsonConvert.SerializeObject(reviewComment, Formatting.Indented)}");
                    }
                }

                /*var overviewCommentFlow = new GitHubPullRequestOverviewCommentFlow(
                    "https://github.azc.ext.hp.com/api/v3", "Indigo-RnD",
                    pta, chatClient, logger: null);
                var review =
                    await overviewCommentFlow.GeneratePullRequestOverviewCommentAsync("S6-PC", pr.Value, contextSizeInKb.Value);
                Console.WriteLine($"Pull Request Review Comment Response:\r\n\r\n{review}");
                Console.WriteLine("\r\n\r\n");

                var codeReviewCommentsFlow = new GitHubPullRequestCodeReviewFlow("https://github.azc.ext.hp.com/api/v3",
                    "Indigo-RnD",
                    pta, chatClient, logger: null);
                var codeReviewComments =
                    await codeReviewCommentsFlow.GeneratePullRequestCodeReviewCommentsAsync("S6-PC", pr.Value, contextSizeInKb.Value);
                Console.WriteLine($"Pull Request Code Review Comments Response:\r\n\r\n{codeReviewComments}");
                Console.WriteLine("\r\n\r\n");*/
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error occurred: {ex.Message}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"{ex}\r\n\r\n");
                Console.ResetColor();

            }
        } 
    }

    [UsedImplicitly]
    private static async Task DebugAddReviewComment(GitHubRestApiClient gitHubRestApiClient)
    {
        Console.WriteLine("Write file path");
        var filePath = Console.ReadLine()?.Trim()!;

        Console.WriteLine("Write line for comment");
        var line = int.Parse(Console.ReadLine()?.Trim()!);

        Console.WriteLine("Write comment");
        var comment = Console.ReadLine()?.Trim()!;

        var prDetails = await gitHubRestApiClient.GetPullRequestDetailsAsync("S6-PC", 1239);
        var reviewComment = await gitHubRestApiClient.AddFileCodeReviewCommentAsync(prDetails, filePath, line, comment);
        Console.WriteLine(JsonConvert.SerializeObject(reviewComment, Formatting.Indented));

        Console.WriteLine("Press enter to continue");
        Console.ReadLine();
    }

    [UsedImplicitly]
    private static void DebugDiffResolver()
    {
        const string diff =
            """
            @@ -5,5 +5,6 @@
            [L1] using Press.BL.InkPlumbing.InkStation.InkTank;
            [L2]  using Press.Common.GlobalConfigurations;
            [L3] +using Press.IBL.Calibrations.ConcreteCalibrations.InkFlow;
            [L4]  using Press.IBL.Common;
            [L5]  using Press.IBL.Common.ErrorEvents;
            [L6]  using Press.IBL.ConsumableReplacements;
            [L7]
            @@ -15,5 +16,7 @@
            [L8] using Press.IBL.InkPlumbing.InkStation.Enums;
            [L9]  using Press.IBL.InkPlumbing.InkStation.History;
            [L10] +using Press.IBL.InkPlumbing.InkStation.InkTank;
            [L11] +using Press.IBL.InkPlumbing.InkStation.Operations;
            [L12]  using Press.IBL.PressControl.StateControl;
            [L13]  using Press.IEngine.PLC.Common.Counters;
            [L14]  using Press.IEngine.PLC.Common.Enumerations;
            [L15]
            @@ -21,5 +24,6 @@
            [L16] using Press.IEngine.PLC.Common.Tree;
            [L17]  using Press.IEngine.PLC.Subsystems.Inks.Ink.ExportedData.Sys;
            [L18] +using PressSdk.Common.BaseTypes;
            [L19]  using PressSdk.Common.Managers;
            [L20]  using PressSdk.Infra.DataManagement.Events;
            [L21]  using PressSdk.Infra.DataManagement.History;
            [L22]
            @@ -84,5 +88,8 @@ internal class InkStationEntityManager : IInkStationEntityManager
            [L23] private readonly ExternalCommunicationConfig _externalCommunicationConfig;
            [L24]      private readonly IHistoryDAL<InkTankBoardReplacementHistory> _inkTankBoardReplacementHistoryDal;
            [L25] +    private readonly IInkCalibrationsRequiredStatusEntityManager _inkCalibrationsRequiredStatusEntityManager;
            [L26] +    private readonly InkTankLevelSensorFactoryCalibrationOperation.IDataProvider _inkTankLevelSensorFactoryCalibrationOperationDp;
            [L27] +    private readonly InkFlowCalibrationOperation.IDataProvider _inkFlowCalibrationOperationDp;
            [L28]
            [L29]      public class PrivateData : IPrivateDataMarker
            [L30]      {
            [L31]
            @@ -123,6 +130,9 @@     public InkStationEntityManager(
            [L32] InkSysMonitorEntity.IDataProvider inkSysMonitorEntityDp,
            [L33]          ExternalCommunicationConfig externalCommunicationConfig,
            [L34] -        IHistoryDAL<InkTankBoardReplacementHistory> inkTankBoardReplacementHistoryDal)
            [L35] +        IHistoryDAL<InkTankBoardReplacementHistory> inkTankBoardReplacementHistoryDal,
            [L36] +        IInkCalibrationsRequiredStatusEntityManager inkCalibrationsRequiredStatusEntityManager,
            [L37] +        InkTankLevelSensorFactoryCalibrationOperation.IDataProvider inkTankLevelSensorFactoryCalibrationOperationDp,
            [L38] +        InkFlowCalibrationOperation.IDataProvider inkFlowCalibrationOperationDp)
            [L39]      {
            [L40]          _privateData = privateData;
            [L41]          _privateData.PlcTree = new Lazy<PlcTree>(hwInputDP.GetTree);
            [L42]
            @@ -152,5 +162,8 @@     public InkStationEntityManager(
            [L43] _externalCommunicationConfig = externalCommunicationConfig;
            [L44]          _inkTankBoardReplacementHistoryDal = inkTankBoardReplacementHistoryDal;
            [L45] +        _inkCalibrationsRequiredStatusEntityManager = inkCalibrationsRequiredStatusEntityManager;
            [L46] +        _inkTankLevelSensorFactoryCalibrationOperationDp = inkTankLevelSensorFactoryCalibrationOperationDp;
            [L47] +        _inkFlowCalibrationOperationDp = inkFlowCalibrationOperationDp;
            [L48]          _privateData.GlobalConfigurationTree = new Lazy<GlobalConfigurationsTree>(globalConfigurationTreeProvider.GetTree);
            [L49]      }
            [L50]
            @@ -300,5 +313,10 @@     private async Task HandleInkTankBoardReplacementAsync(
            [L51] var savedInkTankBoardSN = tankInfo.InstalledTankBoardInfo?.SerialNumber ?? string.Empty;
            [L52]              if (monitor.InkTankBoardSN == savedInkTankBoardSN) continue;
            [L53] +
            [L54] +            await UpdateInkTankLevelSensorCalibrationRequiredAsync(plcInkID, false); //TODO: check if needed to be true?
            [L55] +
            [L56] +            _inkCalibrationsRequiredStatusEntityManager.UpdateCalibrationsRequired(
            [L57] +                new InkCalibrationsRequiredStatusEntity.KeyType(activeBid.InkStationKey), true, true);
            [L58]
            [L59]              await UpdateInstalledTankBoardInfoAsync(activeBid.InkStationKey, monitor.InkTankBoardSN,
            [L60]                  monitor.EngineTime);
            [L61]
            @@ -319,8 +337,39 @@     private async Task HandleInkTankBoardReplacementAsync(
            [L62] PublishInkTankBoardReplacementHistoryReport(activeBid.InkStationKey, inkTankBoardType, monitor.EngineTime,
            [L63]                  savedInkTankBoardSN, monitor.InkTankBoardSN);
            [L64] +
            [L65] +            //Subscribe to ink flow calibration operation completed to update the calibration required status
            [L66] +            _inkFlowCalibrationOperationDp
            [L67] +                .GetObservable(this, new InkFlowCalibrationOperation.KeyType(activeBid.InkStationKey))
            [L68] +                .Select(d => d.Data.State)
            [L69] +                .AddPreviousValue()
            [L70] +                .Where(payload => !payload.previousValue.IsFinishedState() &&
            [L71] +                                  payload.currentValue == StartStopOperationState.Succeeded)
            [L72] +                .FirstAsync()
            [L73] +                .SubscribeProtected(_utils.Logger, _ =>
            [L74] +                    _inkCalibrationsRequiredStatusEntityManager.UpdateInkFlowCalibrationRequired(
            [L75] +                        new InkCalibrationsRequiredStatusEntity.KeyType(activeBid.InkStationKey), false));
            [L76] +
            [L77] +            //Subscribe to ink tank level sensor calibration operation completed to update the calibration required statuses.
            [L78] +            _inkTankLevelSensorFactoryCalibrationOperationDp.GetObservable(this,
            [L79] +                    new InkTankLevelSensorFactoryCalibrationOperation.KeyType(activeBid.InkStationKey))
            [L80] +                .Select(d => d.Data.State)
            [L81] +                .AddPreviousValue()
            [L82] +                .Where(payload => !payload.previousValue.IsFinishedState() &&
            [L83] +                                  payload.currentValue == StartStopOperationState.Succeeded)
            [L84] +                .FirstAsync()
            [L85] +                .SubscribeProtected(_utils.Logger, async _ =>
            [L86] +                {
            [L87] +                    await UpdateInkTankLevelSensorCalibrationRequiredAsync(plcInkID,
            [L88] +                        true); //TODO: check if needed to be false?
            [L89] +                    _inkCalibrationsRequiredStatusEntityManager.UpdateInkTankLevelSensorCalibrationRequired(
            [L90] +                        new InkCalibrationsRequiredStatusEntity.KeyType(activeBid.InkStationKey), false);
            [L91] +                });
            [L92]          }
            [L93]      }
            [L94]
            [L95] +    /// <summary>
            [L96] +    /// Update the installed ink tank board info and tube replaced indication in DAL.
            [L97] +    /// </summary>
            [L98]      private async Task UpdateInstalledTankBoardInfoAsync(InkStationEntity.KeyType key, string serialNumber,
            [L99]          DateTimeOffset engineTime)
            [L100]      {
            [L101]
            @@ -328,8 +377,22 @@     private async Task HandleInkTankBoardReplacementAsync(
            [L102] {
            [L103]              _dal.Update(key,
            [L104] -                entity => entity.OwnerModify(d => d.TankInfo.InstalledTankBoardInfo =
            [L105] -                    new InstalledConsumableInfo(serialNumber, engineTime)));
            [L106] +                entity => entity.OwnerModify(d =>
            [L107] +                {
            [L108] +                    d.TankInfo.InstalledTankBoardInfo =
            [L109] +                        new InstalledConsumableInfo(serialNumber, engineTime);
            [L110] +                    d.TankInfo.TankReplaced = true;
            [L111] +                }));
            [L112] +        }
            [L113]      }
            [L114] +
            [L115] +    /// <summary>
            [L116] +    /// Update the ink tank level sensor calibration required imported data in PLC.
            [L117] +    /// </summary>
            [L118] +    private Task UpdateInkTankLevelSensorCalibrationRequiredAsync(InkID plcInkID, bool inkTankLevelSensorCalibrationRequired)
            [L119] +    {
            [L120] +        using var cts = new CancellationTokenSource(_config.EngineCommunicationTimeout);
            [L121] +        return _plcInkEntityManager.UpdateInkTankLevelSensorCalibrationRequiredAsync(
            [L122] +            new PlcImportedInkEntity.KeyType(plcInkID), inkTankLevelSensorCalibrationRequired, cts.Token);
            [L123]      }
            [L124]
            [L125]      /// <summary>
            [L126]
            @@ -633,6 +696,5 @@     private static InkStationEntity UpdateInkStationEntity(InkStationEntity inkStationEntity,
            [L127] await UpdatePlcAsync(inkStations, ct);
            [L128]          await UpdateInkFlowCalibrationFeatureRequiredAsync(inkStations, ct);
            [L129] -
            [L130]          if (_privateData.GlobalConfigurationTree.Value.InkCabinetType.GlobalConfigInfo.Value is InkCabinetType
            [L131]                  .IC_8_SAS_Stations)
            [L132]              await UpdateSasIndexToPlcAsync(inkStations, ct);
            [L133]
            @@ -764,5 +826,12 @@     private async Task OnBypassChangedAsync((IDataNotificationPayloadMany<BypassEntity, BypassEntity.KeyType> bypasses,
            [L134] await UpdateAsync(inkStationEntity.OwnerModify(d => d.InkKey = inkKey.NotNull()), ct);
            [L135] +    }
            [L136] +
            [L137] +    public async Task<InkStationEntity> TankReplacedConfirmedAsync(InkStationEntity.KeyType inkStationKey, CancellationToken ct)
            [L138] +    {
            [L139] +        using var locker = await _privateData.AsyncLock.LockAsync(ct);
            [L140] +        return _dal.Update(inkStationKey,
            [L141] +            entity => entity.OwnerModify(d => d.TankInfo.TankReplaced = false));
            [L142]      }
            [L143]
            [L144]      public async Task<InkStationEntity> UpdateAsync(InkStationEntity entity, CancellationToken ct) =>
            """;

        var rawDiff = new StringBuilder();
        var sr = new StringReader(diff);
        while (sr.ReadLine() is { } diffLine)
        {
            if (!diffLine.StartsWith('[')) rawDiff.AppendLine(diffLine);
            else if (diffLine.StartsWith('[') && diffLine.EndsWith(']')) rawDiff.AppendLine();
            else
            {
                var index = diffLine.IndexOf(']');
                rawDiff.AppendLine(index > 0 ? diffLine[(index + 2)..] : diffLine);
            }
        }
        foreach (var (resolvedLineType, lineIndex, line) in DiffLineResolver.ResolveLines(rawDiff.ToString().TrimEnd()))
        {
            Console.WriteLine($"{(resolvedLineType is DiffLineResolver.ResolvedLineType.AdditionOrContext? $"[L{lineIndex}] " : "")}{line}");
        }

        Console.WriteLine("Press enter to continue");
        Console.ReadLine();

        var fileCodeReview = new FileCodeReview
        {
            FileComments = "", CodeComments =
            [
                new CodeComment { Line = 54, Comment = "Comment", FixSuggestion = "" },
                new CodeComment { Line = 88, Comment = "Comment", FixSuggestion = "" },
                new CodeComment { Line = 141, Comment = "Comment", FixSuggestion = "" }
            ]
        };
        DiffLineResolver.ResolveCodeReviewLines(diff, fileCodeReview);
    }

    // ReSharper disable once UnusedParameter.Local
    private static async Task<PullRequestOverviewCommentResponse> GetPullRequestOverviewCommentResponseAsync(GitHubRestApiClient gitHubRestApiClient, string model, string pta, IChatClient chatClient, int? contextSizeInKb, int prNumber)
    {
        Directory.CreateDirectory("Cache");
        var cachedFile = Path.Combine($"Cache\\{nameof(PullRequestOverviewCommentResponse)}_model_{model.Replace('/','_').Replace(':', '_')}_pr_{prNumber}.json");
        if (File.Exists(cachedFile))
        {
            Console.WriteLine("Getting overview comment from cache...");
            var cached =  JsonConvert.DeserializeObject<PullRequestOverviewCommentResponse>(
                await File.ReadAllTextAsync(cachedFile))!;

            WritePullRequestDetailsToConsole(cached.PullRequestFilesClassificationResponse.PullRequest);
            return cached;
        }

        var prDetails = await gitHubRestApiClient.GetPullRequestDetailsAsync("S6-PC", prNumber);
        WritePullRequestDetailsToConsole(prDetails);

        var agent = new GitHubPullRequestOverviewCommentFlow(chatClient, logger: null);

        var pullRequestOverviewCommentResponse = await agent.GeneratePullRequestOverviewCommentAsync( gitHubRestApiClient,"S6-PC", prNumber, maxContextSizeInTokens: contextSizeInKb);
        await File.WriteAllTextAsync(cachedFile, JsonConvert.SerializeObject(pullRequestOverviewCommentResponse));
        return pullRequestOverviewCommentResponse;
    }

    private static void WritePullRequestDetailsToConsole(PullRequestDetails pullRequestDetails)
    {
        Console.Clear();
        Console.WriteLine($"Pull Request: {pullRequestDetails.Title} - #{pullRequestDetails.Number}");
        Console.WriteLine($"Description: {pullRequestDetails.StripDevopsCommentFromBody()}");
        Console.WriteLine($"Author: {pullRequestDetails.User}");
    }
}