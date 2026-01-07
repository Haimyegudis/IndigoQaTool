using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Tools.ExternalDevServices.Integrations.Jenkins;

namespace Tools.ExternalDevServices.AI.MCP.JenkinsMcp;

[McpServerToolType]
public static class JenkinsPipelineAnalysisTool
{
    private static readonly Dictionary<string, JenkinsPipeline> CacheByUrl = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, JenkinsPipeline> CacheByTag = new(StringComparer.OrdinalIgnoreCase);


    [McpServerTool, 
     Description("Gets an high level summary of the specified Jenkins pipeline, including details about of failed and skipped stages along with detected failure types")]
    public static async Task<string> GetJenkinsPipelineSummaryAsync(string jenkinsPipelineUrl)
    {
        try
        {
            if (CacheByUrl.TryGetValue(jenkinsPipelineUrl, out var cachedPipeline))
            {
                return GetPipelineSummary(cachedPipeline);
            }

            var pipeline = await JenkinsPipelineParser.ParseUrl(jenkinsPipelineUrl);
            CacheByUrl[jenkinsPipelineUrl] = pipeline;
            CacheByTag[pipeline.BuildTag] = pipeline;

            return GetPipelineSummary(pipeline);
        }
        catch (Exception ex)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool,
     Description("Gets the detailed information and output of the specified Jenkins Pipeline stage")]
    public static string GetJenkinsPipelineStageById(string jenkinsPipelineBuildTag, int stageId)
    {
        if (!CacheByTag.TryGetValue(jenkinsPipelineBuildTag, out var pipeline))
            throw new McpException(
                $"Jenkins Pipeline with build tag {jenkinsPipelineBuildTag} was not fetched yet using the {nameof(GetJenkinsPipelineSummaryAsync).Replace("Async", "")} tool");

        var byLevelQueue = new Queue<JenkinsPipelineStage>(pipeline.Stages);
        while (byLevelQueue.Count > 0)
        {
            var stage = byLevelQueue.Dequeue();
            if (stage.Id == stageId)
            {
                return stage.ToString();
            }
            foreach (var childStage in stage.ChildStages)
            {
                byLevelQueue.Enqueue(childStage);
            }
        }

        throw new McpException(
            $"Stage with ID {stageId} was not found in Jenkins Pipeline with build tag {jenkinsPipelineBuildTag}");
    }

    [McpServerTool,
     Description("Gets the detailed information and output of the specified Jenkins Pipeline stage")]
    public static string GetJenkinsPipelineStageByName(string jenkinsPipelineBuildTag, string stageName)
    {
        if(stageName.AsSpan().StartsWith("Parallel "))
            stageName = stageName.AsSpan()["Parallel ".Length..].TrimStart().ToString();

        if (!CacheByTag.TryGetValue(jenkinsPipelineBuildTag, out var pipeline))
            throw new McpException(
                $"Jenkins Pipeline with build tag {jenkinsPipelineBuildTag} was not fetched yet using the {nameof(GetJenkinsPipelineSummaryAsync).Replace("Async", "")} tool");

        var byLevelQueue = new Queue<JenkinsPipelineStage>(pipeline.Stages);
        while (byLevelQueue.Count > 0)
        {
            var stage = byLevelQueue.Dequeue();
            if (stage.Name == stageName)
            {
                return stage.ToString();
            }
            foreach (var childStage in stage.ChildStages)
            {
                byLevelQueue.Enqueue(childStage);
            }
        }

        throw new McpException(
            $"Stage '{stageName}' was not found in Jenkins Pipeline with build tag {jenkinsPipelineBuildTag}");
    }

    private static string GetPipelineSummary(JenkinsPipeline pipeline) =>
        JsonConvert.SerializeObject(
            new
            {
                pipeline.BuildTag,
                pipeline.ReportedFinishStatus,
                pipeline.FailedStagesNames,
                pipeline.SkippedStagesNames,
                pipeline.Summary
            });
}