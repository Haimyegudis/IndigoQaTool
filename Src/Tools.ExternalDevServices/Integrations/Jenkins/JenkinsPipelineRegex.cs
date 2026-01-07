using System.Text.RegularExpressions;

namespace Tools.ExternalDevServices.Integrations.Jenkins;

public static partial class JenkinsPipelineRegex
{
    [GeneratedRegex(@"^(?<root>https?://[^/]+)(?:/[^/]+)*/job/(?<pipeline>[^/]+)/(?<build>\d+)(?:/.*)?$")]
    public static partial Regex JenkinsConsoleUrlRegex();

    [GeneratedRegex(@"^(?<root>https?://[^/]+)/blue/organizations/jenkins/(?<pipeline>[^/]+)/detail/[^/]+/(?<build>\d+)/pipeline/?")]
    public static partial Regex JenkinsUrlRegex();

    [GeneratedRegex(@"\[(?<iso>\d{4}-\d{2}-\d{2}T[^\]]+)\]")]
    public static partial Regex IsoDateRegex();

    [GeneratedRegex(@"(?<h>\d{2}):(?<m>\d{2}):(?<s>\d{2})")]
    public static partial Regex HhMmSsTimeRegex();

    [GeneratedRegex(@"^\s*\[Pipeline]\s*\{\s*\((?<name>[^)]+)\)\s*$")]
    public static partial Regex StageOpenRegex();

    [GeneratedRegex(@"^\s*\[Pipeline]\s*\}\s*$")]
    public static partial Regex StageCloseRegex();

    // Matches: Stage "Parallel Tests" skipped due to earlier failure(s)
    [GeneratedRegex(@"\s*(?<skipped>Stage\s+""(?<name>[^""]+)""\s+skipped due to [^\r\n]+)(?=\r?\n|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    public static partial Regex TextStageSkippedRegex();

    // Matches lines like: "   Failed in branch Run Dockerize" (leading spaces allowed)
    [GeneratedRegex(@"\.*Failed in branch\s+(?<name>.+)")]
    public static partial Regex FailedInBranchRegex();

    [GeneratedRegex(@"^.*[\r\n]?Finished:\s*(?<status>[A-Z]+)$", RegexOptions.Singleline)]
    public static partial Regex PipelineFinishStatusRegex();

    [GeneratedRegex(@"^pipeline-node-(?<NodeId>\d+)$")]
    public static partial Regex PipelineNodeRegex();

    [GeneratedRegex("^BUILD_TAG=(?<buildTag>.+)$")]
    public static partial Regex BuildTagRegex();

    [GeneratedRegex(@"^Failed!\s*-\s*Failed:\s*(?<Failed>\d+),\s*Passed:\s*(?<Passed>\d+),\s*Skipped:\s*(?<Skipped>\d+).*$")]
    public static partial Regex DotNetTestsRunFailureRegex();

    [GeneratedRegex(@"^Done Building Project "".*"" \(.+\) -- FAILED\.$")]
    public static partial Regex DotNetBuildFailedRegex();

    public static bool IsStageOpenText(string innerText) => StageOpenRegex().IsMatch(innerText);

    public static bool IsStageCloseText(string innerText) => StageCloseRegex().IsMatch(innerText);
}