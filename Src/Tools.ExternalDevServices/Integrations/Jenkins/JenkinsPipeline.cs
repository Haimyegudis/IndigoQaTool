using System.Text;

namespace Tools.ExternalDevServices.Integrations.Jenkins;

public class JenkinsPipeline
{
    public string BuildTag { get; init; } = "";
    public IReadOnlyCollection<JenkinsPipelineStage> Stages { get; init; } = [];
    public string Summary { get; init; } = "";

    public IReadOnlyCollection<string> FailedStagesNames { get; init; } = [];
    public IReadOnlyCollection<string> SkippedStagesNames { get; init; } = [];

    public string ReportedFinishStatus { get; init; } = "";

    public string ToMarkdown()
    {
        var markdownSb = new StringBuilder();

        markdownSb.AppendLine($"# {BuildTag} ({ReportedFinishStatus})<br>");

        markdownSb.AppendLine("## Failed Stages<br>");
        foreach (var failedStageName in FailedStagesNames)
        {
            markdownSb.AppendLine($"- {failedStageName}<br>");
        }

        markdownSb.AppendLine("## Skipped Stages<br>");
        foreach (var skippedStageName in SkippedStagesNames)
        {
            markdownSb.AppendLine($"- {skippedStageName}");
        }

        markdownSb.AppendLine("## Summary<br>");
        markdownSb.AppendLine(Summary.Replace("\n", "\n<br>"));

        markdownSb.AppendLine("## Stages<br>");
        foreach (var stage in Stages)
        {
            markdownSb.AppendLine(stage.ToMarkdown(""));
        }

        return markdownSb.ToString();
    }
}