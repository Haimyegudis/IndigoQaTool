using System.Text;
using Newtonsoft.Json;

namespace Tools.ExternalDevServices.Integrations.Jenkins;

public class JenkinsPipelineStage
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public bool IsParallelStage { get; init; }
    [JsonIgnore]
    public List<JenkinsPipelineStage> ChildStages { get; } = [];
    public IReadOnlyCollection<string> ChildStagesNames => ChildStages.Select(c => c.Name).ToArray();
    public string Output { get; set; } = "";
    public bool IsFinished { get; set; }
    public string? FinishReason { get; set; }
    public bool IsFailed { get; set; }
    public bool HasFailedChildStages => ChildStages.Any(c => c.IsFailed || c.HasFailedChildStages);
    public bool IsSkipped { get; set; }
    public bool HasSkippedChildStages => ChildStages.Any(c => c.IsSkipped || c.HasSkippedChildStages);

    public DetectedStageFailureTypes DetectedFailureTypes { get; set; } = DetectedStageFailureTypes.None;

    public override string ToString() =>
        JsonConvert.SerializeObject(this, Formatting.None);

    public string ToMarkdown(string parentStageString, StringBuilder? markdownSb = null)
    {
        markdownSb ??= new StringBuilder();

        var stageString = string.IsNullOrEmpty(parentStageString) ? Name : $"{parentStageString} > {Name}";
        markdownSb.AppendLine($"### [{(string.IsNullOrEmpty(parentStageString)? "" : "Child ")}Stage #{Id}] {Name}{(IsFailed? " [FAILED]" : IsSkipped? " [SKIPPED]" : "")}<br>");
        if(!string.IsNullOrEmpty(parentStageString)) markdownSb.AppendLine($"- **Path**: {stageString}<br>");
        markdownSb.AppendLine($"- **{nameof(IsParallelStage)}**: {IsParallelStage}<br>");
        markdownSb.AppendLine($"- **{nameof(IsFinished)}**: {IsFinished}<br>");
        markdownSb.AppendLine($"- **{nameof(FinishReason)}**: {FinishReason}<br>");
        markdownSb.AppendLine($"- **{nameof(IsFailed)}**: {IsFailed}<br>");
        markdownSb.AppendLine($"- **{nameof(IsSkipped)}**: {IsSkipped}<br>");
        markdownSb.AppendLine($"- **{nameof(DetectedFailureTypes)}**: {DetectedFailureTypes}<br>");
        markdownSb.AppendLine($"- **{nameof(HasFailedChildStages)}**: {HasFailedChildStages}<br>");
        markdownSb.AppendLine($"- **{nameof(HasSkippedChildStages)}**: {HasSkippedChildStages}<br>");
        markdownSb.AppendLine($"#### **{nameof(Output)}**:<br>");
        markdownSb.AppendLine("<details>");
        markdownSb.AppendLine("<summary>Click to expand</summary>");
        markdownSb.AppendLine(Output.Replace("\n", "\n<br>"));
        markdownSb.AppendLine("</details>");
        markdownSb.AppendLine("<br>");
        markdownSb.AppendLine();

        foreach (var childStage in ChildStages)
        {
            childStage.ToMarkdown(stageString, markdownSb);
        }

        return markdownSb.ToString();
    }
}