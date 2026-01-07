using CommandLine;
using Tools.ExternalDevServices.AI.MCP.McpUtils;

namespace Tools.ExternalDevServices.AI.MCP.JiraMCP;

internal class Program
{
    public class Options
    {
        [Option("jira-personal-access-token", Required = true)]
        public string JiraPersonalAccessToken { get; set; } = string.Empty;

        [Option("confluence-personal-access-token", Required = true)]
        public string ConfluencePersonalAccessToken { get; set; } = string.Empty;

        [Option("user", Required = true)]
        public string User { get; set; } = string.Empty;
    }

    private static async Task Main(string[] args)
    {
        var result = Parser.Default.ParseArguments<Options>(args);
        if (result is NotParsed<Options>) Environment.Exit(1);

        var options = result.Value;
        HostArguments.JiraPersonalAccessToken = options.JiraPersonalAccessToken;
        HostArguments.ConfluencePersonalAccessToken = options.ConfluencePersonalAccessToken;
        HostArguments.User = options.User;

        await HostUtils.RunStdioAsync();
    }
}