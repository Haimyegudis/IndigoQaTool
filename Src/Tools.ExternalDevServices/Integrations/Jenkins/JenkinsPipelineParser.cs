using HtmlAgilityPack;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using OpenQA.Selenium;

namespace Tools.ExternalDevServices.Integrations.Jenkins;

public static class JenkinsPipelineParser
{
    private const string RootElementId = "out";

    public static async Task<JenkinsPipeline> ParseUrl(string jenkinsPipelineUrl)
    {
        // If the input matches a Jenkins job/build URL, normalize to consoleFull
        var consoleMatch = JenkinsPipelineRegex.JenkinsConsoleUrlRegex().Match(jenkinsPipelineUrl);
        string? consoleHtml;
        if (consoleMatch.Success)
        {
            var rootUrl = consoleMatch.Groups["root"].Value;
            var pipeline = consoleMatch.Groups["pipeline"].Value;
            var build = consoleMatch.Groups["build"].Value;
            var normalizedUrl = $"{rootUrl}/job/{pipeline}/{build}/consoleFull";
            consoleHtml = await GetUrlContentAsync(normalizedUrl);
            return !string.IsNullOrEmpty(consoleHtml)
                ? ParseHtml(consoleHtml)
                : throw new InvalidOperationException($"Failed to get build console output from URL {normalizedUrl}");
        }

        // Use the generated regex to parse the pipeline URL
        var match = JenkinsPipelineRegex.JenkinsUrlRegex().Match(jenkinsPipelineUrl);
        if (!match.Success)
        {
            throw new ArgumentException(
                $"Failed to construct Jenkins pipeline console output URL from {jenkinsPipelineUrl}");
        }

        var rootUrl2 = match.Groups["root"].Value;
        var pipeline2 = match.Groups["pipeline"].Value;
        var build2 = match.Groups["build"].Value;
        var consoleUrl = $"{rootUrl2}/job/{pipeline2}/{build2}/consoleFull";
        consoleHtml = await GetUrlContentAsync(consoleUrl);
        return !string.IsNullOrEmpty(consoleHtml)
            ? ParseHtml(consoleHtml)
            : throw new InvalidOperationException($"Failed to get build console output from URL {consoleUrl}");
    }

    private static async Task<string?> GetUrlContentAsync(string url)
    {
        var options = new ChromeOptions();
        options.AddArgument("--headless=new");     // run without UI
        options.AddArgument("--disable-gpu");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");

        using var driver = new ChromeDriver(options);

        await driver.Navigate().GoToUrlAsync(url);

        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(120));

        wait.Until(d =>
            ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString() == "complete");
        wait.Until(d => d.FindElement(By.Id(RootElementId)));

        return driver.PageSource;
    }

    public static JenkinsPipeline ParseHtml(string html) =>
        new HtmlParser(html).Parse();

    private class HtmlParser
    {
        private class ParserState
        {
            public string? BuildTag { get; set; }
            public List<JenkinsPipelineStage> PipelineStages { get; } = [];
            public StringBuilder PipelineSummarySb { get; } = new();
            public List<string> FailedStagesNames { get; } = [];
            public List<string> SkippedStagesNames { get; } = [];

            public Dictionary<int, (JenkinsPipelineStage stage, StringBuilder stageOutputSb)> StagesMap { get; } = [];
            public HashSet<string> StageNamesHash { get; } = [];
            /// <summary>
            /// Mapping the start id of a Jenkins Pipeline node to its enclosing id necessary for tracking back to the parent stage.
            /// </summary>
            public Dictionary<int, int> StartIdToEnclosingIdMap { get; } = [];

            /// <summary>
            /// Last enclosing id of a Jenkins Pipeline node. This is necessary for tracking back to the parent stage and for identifying the correct stage in the output when it is not specified (e.g. in text output).
            /// </summary>
            public int? LastEnclosingId { get; set; }

            /// <summary>
            /// Last text that was processed from a Text node. This is necessary for identifying the correct failure reason.
            /// </summary>
            public string LastTextNodeLine { get; set; } = "";

            public string FinishStatus { get; set; } = "";
        }

        private HtmlNode Root { get; }
        private ParserState State { get; } = new();

        public HtmlParser(string html)
        {
            Root = GetRootHtmlNode(html);
        }

        public JenkinsPipeline Parse()
        {
            foreach (var child in Root.Descendants()
                         .Where(FilterHtmlNodes))
            {
                if (ProcessContextSwitch(child)) continue;

                var text = child.InnerText.Trim();

                if (TryParsePipelineFinishStatus(text, out var finishStatus))
                    State.FinishStatus = finishStatus;

                ParseStageSkipOrFail(text);

                if (child.NodeType == HtmlNodeType.Text)
                {
                    ProcessHtmlTextNode(child, text);
                    continue;
                }

                // Attributes we care about
                var spanLabelAttribute = GetAttribute(child, "label");

                var nodeIdAttribute = GetAttribute(child, "nodeId");

                var nodeStartIdAttribute = GetAttribute(child, "startId");
                var enclosingNodeIdAttribute = GetAttribute(child, "enclosingId");

                int? nodeId = int.TryParse(nodeStartIdAttribute, out var nId) ? nId : null;
                int? nodeStartId = int.TryParse(nodeStartIdAttribute, out var sId) ? sId : null;
                int? enclosingNodeId = int.TryParse(enclosingNodeIdAttribute, out var eId) ? eId : null;

                // Map nodeId attribute from pipeline-new-node spans for tracking enclosing ids
                // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
                if (child.Attributes["class"]?.Value == "pipeline-new-node" && nodeId.HasValue && enclosingNodeId.HasValue)
                {
                    State.StartIdToEnclosingIdMap[int.Parse(nodeIdAttribute!)] = int.Parse(enclosingNodeIdAttribute!);
                }

                //Map node start id to enclosing id for tracking parent stages
                if (nodeStartId.HasValue && enclosingNodeId.HasValue)
                {
                    State.StartIdToEnclosingIdMap[nodeStartId.Value] = enclosingNodeId.Value;
                }

                if (!string.IsNullOrEmpty(spanLabelAttribute) &&
                    nodeStartId.HasValue &&
                    enclosingNodeId.HasValue)
                {
                    ProcessStageOpen(spanLabelAttribute, nodeStartId.Value, enclosingNodeId.Value);
                }

                else if (nodeStartId.HasValue && JenkinsPipelineRegex.IsStageCloseText(text))
                {
                    ProcessStageClose(nodeStartId.Value);
                }

                else if (nodeStartId.HasValue) // Regular stage output
                {
                    if (State.StagesMap.TryGetValue(nodeStartId.Value, out var stageTuple))
                    {
                        stageTuple.stageOutputSb.AppendLine(text);
                    }
                }

                else if (enclosingNodeId.HasValue)
                {
                    State.LastEnclosingId = enclosingNodeId.Value;
                    if (TryGetStageTupleOrParentStageTuple(State.LastEnclosingId.Value, out var stageTuple))
                    {
                        stageTuple.stageOutputSb.AppendLine(text);
                    }
                }

                //When we don't have any direct reference to the stage, we can try to use the last enclosing id we have. 
                else if (!nodeId.HasValue && !nodeStartId.HasValue && enclosingNodeId.HasValue &&
                         State.LastEnclosingId.HasValue && TryGetStageTupleOrParentStageTuple(State.LastEnclosingId.Value, out var stageTuple))
                {
                    stageTuple.stageOutputSb.AppendLine(text);
                }
            }

            // Set the output of each stage
            foreach (var (stage, stageOutputSb) in State.StagesMap.Values)
            {
                stage.Output = stageOutputSb.ToString();
            }

            var pipeline = new JenkinsPipeline
            {
                BuildTag = State.BuildTag ?? $"CustomTag_{Guid.CreateVersion7()}",
                Stages = State.PipelineStages.ToArray(),
                Summary = State.PipelineSummarySb.ToString().Trim(),
                FailedStagesNames = State.FailedStagesNames.Distinct().ToArray(),
                SkippedStagesNames = State.SkippedStagesNames.Except(State.FailedStagesNames).Distinct().ToArray(),
                ReportedFinishStatus = State.FinishStatus
            };
            return pipeline;
        }

        private void ProcessStageClose(int nodeStartId)
        {
            State.LastEnclosingId = null;
            if (!State.StagesMap.TryGetValue(nodeStartId, out var stageTuple)) return;

            var (stage, stageOutputSb) = stageTuple;
            stage.IsFinished = true;
            var parentStage = GetParentStage(stage.Id);
            var status = stage.IsFailed ? "finished with failure" :
                stage.IsSkipped ? "skipped" : "finished";
            var childStatus = "";
            if (stage.HasFailedChildStages)
            {
                childStatus += " with failed child stages";
                if (stage.HasSkippedChildStages)
                    childStatus += " and skipped child stages";
            }
            else if (stage.HasSkippedChildStages)
            {
                childStatus += " with skipped child stages";
            }

            var detectedFailuresStatus = "";
            if (stage.DetectedFailureTypes != DetectedStageFailureTypes.None)
            {
                var detectedFailures = Enum.GetValues<DetectedStageFailureTypes>()
                    .Where(f => f is not DetectedStageFailureTypes.None &&
                                stage.DetectedFailureTypes.HasFlag(f));
                detectedFailuresStatus += string.IsNullOrEmpty(childStatus)
                    ? " with detected failures: "
                    : " and detected failures: ";
                detectedFailuresStatus += string.Join(", ", detectedFailures.Select(f => f.ToString()));
            }

            status += childStatus + detectedFailuresStatus;
            var stageCloseOutput =
                $"{(stage.IsParallelStage ? "Parallel Stage" : "Stage")} \"{stage.Name}\" {status}{(parentStage is not null ? $", Parent stage: \"{parentStage.Name}\"" : "")}";
            stageOutputSb.AppendLine(stageCloseOutput);
            State.PipelineSummarySb.AppendLine(stageCloseOutput);
        }

        private void ProcessStageOpen(string spanLabelAttribute, int nodeStartId, int enclosingNodeId)
        {
            var parentStage = GetParentStage(nodeStartId);

            var isParallel = spanLabelAttribute.Contains("Branch: ");
            var stageName = spanLabelAttribute.Replace("Branch: ", "");
            if (!State.StagesMap.ContainsKey(nodeStartId) && !State.StageNamesHash.Contains(stageName))
            {
                State.StagesMap.Add(nodeStartId,
                    (new JenkinsPipelineStage { Id = nodeStartId, Name = stageName, IsParallelStage = isParallel },
                        new StringBuilder()));
                State.StageNamesHash.Add(stageName);
                if (parentStage is not null)
                {
                    parentStage.ChildStages.Add(State.StagesMap[nodeStartId].stage);
                }
                else
                {
                    State.PipelineStages.Add(State.StagesMap[nodeStartId].stage);
                }

                State.PipelineSummarySb.AppendLine(
                    $"{(isParallel ? "Parallel Stage" : "Stage")} \"{stageName}\" started{(parentStage is not null ? $", Parent stage: \"{parentStage.Name}\"" : "")}");
            }

            State.LastEnclosingId = enclosingNodeId;
        }

        private void ProcessHtmlTextNode(HtmlNode child, string text)
        {
            if (JenkinsPipelineRegex.BuildTagRegex().Match(text) is { Success: true } buildTagMatch)
            {
                State.BuildTag = buildTagMatch.Groups["buildTag"].Value;
                return;
            }

            var stage = State.LastEnclosingId.HasValue ? GetStageOrParentStage(State.LastEnclosingId.Value) : null;
            if (State.LastEnclosingId.HasValue && stage is not null)
            {
                var stageTuple = State.StagesMap[stage.Id];
                stageTuple.stageOutputSb.Append(child.InnerText);

                if (JenkinsPipelineRegex.DotNetTestsRunFailureRegex().IsMatch(text))
                {
                    stageTuple.stage.DetectedFailureTypes |= DetectedStageFailureTypes.DotNetTestsRunFailure;
                }
                else if (JenkinsPipelineRegex.DotNetBuildFailedRegex().IsMatch(text))
                {
                    stageTuple.stage.DetectedFailureTypes |= DetectedStageFailureTypes.DotNetBuildFailure;
                }
                else if (text.Equals("Cancelling nested steps due to timeout",
                             StringComparison.OrdinalIgnoreCase) ||
                         text.Equals("Attempting to cancel the build...", StringComparison.OrdinalIgnoreCase))
                {
                    stageTuple.stage.DetectedFailureTypes |= DetectedStageFailureTypes.CanceledDueToTimeout;

                    //When a leaf stage is canceled due to Long Running Test and its parent was also canceled because of it,
                    //and the leaf stage is the only child of the parent, we can assume that the leaf stage is the one that caused the parent to be canceled.
                    if (State.LastTextNodeLine.Contains("[Long Running Test]", StringComparison.OrdinalIgnoreCase)
                        || (stageTuple.stage.ChildStages.Count == 0 &&
                            GetParentStage(stageTuple.stage.Id) is { ChildStages.Count: 1 } parentStage &&
                            parentStage.DetectedFailureTypes.HasFlag(DetectedStageFailureTypes
                                .TimeoutDueToLongRunningTest)))
                    {
                        stageTuple.stage.DetectedFailureTypes |=
                            DetectedStageFailureTypes.TimeoutDueToLongRunningTest;
                    }
                }
            }

            if (JenkinsPipelineRegex.IsoDateRegex().Replace(text, "").Trim() != "" &&
                JenkinsPipelineRegex.HhMmSsTimeRegex().Replace(text, "").Trim() != "")
            {
                State.LastTextNodeLine = text;
            }
        }

        private void ParseStageSkipOrFail(string text)
        {
            if (JenkinsPipelineRegex.TextStageSkippedRegex().Match(text) is { Success: true } isSkippedStageMessageMatch)
            {
                var skippedStage = isSkippedStageMessageMatch.Groups["name"].Value;
                State.SkippedStagesNames.Add(skippedStage);
                var stageName = isSkippedStageMessageMatch.Groups["name"].Value.Replace("Branch: ", "").Trim();
                if (State.StagesMap.Values.FirstOrDefault(s => s.stage.Name == stageName) is { } stageTuple)
                {
                    stageTuple.stage.IsSkipped = !stageTuple.stage.IsFailed;
                    stageTuple.stage.IsFinished = true;
                    stageTuple.stage.FinishReason ??= text;
                    stageTuple.stageOutputSb.AppendLine(text);
                }

                State.PipelineSummarySb.AppendLine(text);
            }

            else if (JenkinsPipelineRegex.FailedInBranchRegex().Match(text) is { Success: true } isFailedStageMessageMatch)
            {
                var failedStage = isFailedStageMessageMatch.Groups["name"].Value;
                State.FailedStagesNames.Add(failedStage);

                var stageName = isFailedStageMessageMatch.Groups["name"].Value.Replace("Branch: ", "").Trim();
                if (State.StagesMap.Values.FirstOrDefault(s => s.stage.Name == stageName) is { } stageTuple)
                {
                    stageTuple.stage.IsFailed = stageTuple.stage.IsFinished = true;
                    stageTuple.stage.IsSkipped = false;
                    stageTuple.stage.FinishReason = text;
                    stageTuple.stageOutputSb.AppendLine(text);
                }

                State.PipelineSummarySb.AppendLine($"Stage \"{stageName}\" failed");
            }
        }

        private static bool TryParsePipelineFinishStatus(string text, [NotNullWhen(true)] out string? finishStatus)
        {
            if (JenkinsPipelineRegex.PipelineFinishStatusRegex().Match(text) is
                { Success: true } pipelineFinishStatusMatch)
            {
                finishStatus = pipelineFinishStatusMatch.Groups["status"].Value;
                return true;
            }

            finishStatus = null;
            return false;
        }

        /// <summary>
        /// In Jenkins Pipeline, a context switch is reflected in the following way:
        /// &lt;span class="pipeline-node-791"&gt; - context is switched to node 791
        /// The node id we extract from the class attribute is the actual enclosing id of the active stage.
        /// </summary>
        /// <param name="child"></param>
        /// <returns></returns>
        private bool ProcessContextSwitch(HtmlNode child)
        {
            // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
            if (child.Attributes["class"]?.Value is not { } classValue ||
                JenkinsPipelineRegex.PipelineNodeRegex().Match(classValue) is not { Success: true } pipelineNodeMatch)
            {
                return false;
            }

            if (State.StartIdToEnclosingIdMap.TryGetValue(
                    int.Parse(pipelineNodeMatch.Groups["NodeId"].Value), out var actualEnclosingId))
            {
                State.LastEnclosingId = actualEnclosingId;
            }

            return true;

        }

        private static bool FilterHtmlNodes(HtmlNode node) =>
             IsRelevantNode(node) && !IsTimestampElement(node);

        private static bool IsRelevantNode(HtmlNode node) =>
            IsSpanElement(node) || IsTextOutputElement(node);

        private static bool IsSpanElement(HtmlNode node) =>
            node.NodeType == HtmlNodeType.Element && node.Name.Equals("span", StringComparison.OrdinalIgnoreCase);

        private static bool IsTextOutputElement(HtmlNode node) =>
            node.NodeType == HtmlNodeType.Text && !string.IsNullOrWhiteSpace(((HtmlTextNode)node).Text);

        private static bool IsTimestampElement(HtmlNode node) =>
            // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
            (node.Attributes["class"]?.Value == "timestamp" &&
             JenkinsPipelineRegex.HhMmSsTimeRegex().IsMatch(node.InnerText.AsSpan().Trim())) ||
            // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
            (node.Attributes["style"]?.Value == "display: none" &&
             JenkinsPipelineRegex.IsoDateRegex().IsMatch(node.InnerText.AsSpan().Trim()));

        private static HtmlNode GetRootHtmlNode(string html)
        {
            html = Regex.Replace(html, "<script[\\s\\S]*?</script>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, "<template[\\s\\S]*?</template>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, "<dialog[\\s\\S]*?</dialog>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, "<svg[\\s\\S]*?</svg>", "", RegexOptions.IgnoreCase);

            var doc = new HtmlDocument
            {
                OptionFixNestedTags = true,
                OptionAutoCloseOnEnd = true,
                OptionUseIdAttribute = true,
                OptionEmptyCollection = true,
                OptionReadEncoding = false
            };
            doc.LoadHtml(html);

            var root = doc.GetElementbyId(RootElementId);
            return root ??
                   throw new ArgumentException($"Root element with id '{RootElementId}' was not found.",
                       nameof(RootElementId));
        }

        private bool TryGetStageTupleOrParentStageTuple(int stageId,
            out (JenkinsPipelineStage stage, StringBuilder stageOutputSb) stageTuple)
        {
            stageTuple = default;
            return GetStageOrParentStage(stageId) is { } stage &&
                   State.StagesMap.TryGetValue(stage.Id, out stageTuple);
        }

        private JenkinsPipelineStage? GetStageOrParentStage(int stageId) =>
            State.StagesMap.TryGetValue(stageId, out var stageTuple) ? stageTuple.stage : GetParentStage(stageId);

        private JenkinsPipelineStage? GetParentStage(int stageId)
        {
            var currentStageStartId = stageId;
            while (true)
            {
                if (!State.StartIdToEnclosingIdMap.TryGetValue(currentStageStartId, out var mappedEnclosingId))
                    return null;
                if (State.StagesMap.TryGetValue(mappedEnclosingId, out var mappedStageTuple))
                {
                    return mappedStageTuple.stage;
                }

                currentStageStartId = mappedEnclosingId;
            }
        }

        private static string? GetAttribute(HtmlNode node, string name)
            => node.Attributes.Contains(name) ? node.Attributes[name].Value : null;
    }
}
