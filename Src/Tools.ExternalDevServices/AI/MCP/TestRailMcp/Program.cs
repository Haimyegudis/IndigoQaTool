using CommandLine;
using ModelContextProtocol;
using Tools.ExternalDevServices.AI.MCP.McpUtils;

namespace Tools.ExternalDevServices.AI.MCP.TestRailMcp
{
    internal class Program
    {
        public class Options
        {
            [Option("personal-api-key", Required = true)] public string PersonalApiKey { get; set; } = string.Empty;

            [Option("user", Required = true)] public string User { get; set; } = string.Empty;
            
            [Option("project-id", Required = true)] public string ProjectId { get; set; } = string.Empty;
        }

        private static async Task Main(string[] args)
        {
            var result = Parser.Default.ParseArguments<Options>(args);
            if (result is NotParsed<Options>) throw new McpException($"Failed to parse arguments");

            var options = result.Value;
            HostArguments.TestRailPersonalApiKey = options.PersonalApiKey;
            HostArguments.User = options.User;
            HostArguments.ProjectId = options.ProjectId;

            await HostUtils.RunStdioAsync();
        }
    }
}