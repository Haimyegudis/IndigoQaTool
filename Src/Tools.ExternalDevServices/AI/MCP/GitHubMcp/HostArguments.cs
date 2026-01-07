using static Tools.ExternalDevServices.AI.MCP.GitHubMcp.Program;

namespace Tools.ExternalDevServices.AI.MCP.GitHubMcp;

internal static class HostArguments
{
    public const string ApiBaseUrl = "https://github.azc.ext.hp.com/api/v3";
    public const string Owner = "Indigo-RnD";
    public static string? PersonalAccessToken { get; set; }
    public static PullRequestReviewVersion PullRequestReviewVersion { get; set; } = PullRequestReviewVersion.V2;
}