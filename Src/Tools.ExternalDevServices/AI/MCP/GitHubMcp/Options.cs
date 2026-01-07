using CommandLine;

namespace Tools.ExternalDevServices.AI.MCP.GitHubMcp;

public class Options
{
    [Option("personal-access-token", Required = true)]
    public string PersonalAccessToken { get; set; } = string.Empty;

    [Option("pull-request-review-version", Default = PullRequestReviewVersion.V2, Required = false)]
    public PullRequestReviewVersion PullRequestReviewVersion { get; set; } = PullRequestReviewVersion.V2;
}