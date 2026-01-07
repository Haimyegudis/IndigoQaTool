using CommandLine;
using Tools.ExternalDevServices.AI.MCP.McpUtils;

namespace Tools.ExternalDevServices.AI.MCP.GitHubMcp;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var result = Parser.Default.ParseArguments<Options>(args);
        if (result is NotParsed<Options>) Environment.Exit(1);

        var options = ValidateArgs(result.Value);
        HostArguments.PersonalAccessToken = options.PersonalAccessToken;
        HostArguments.PullRequestReviewVersion = options.PullRequestReviewVersion;

        await HostUtils.RunStdioAsync();
    }

    private static Options ValidateArgs(Options options)
    {
        if (!string.IsNullOrWhiteSpace(options.PersonalAccessToken)) return options;

        Console.Error.WriteLine("Personal Access Token is a required parameter.");
        Environment.Exit(1);

        return options;
    }
}