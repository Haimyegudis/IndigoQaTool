using CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Tools.ExternalDevServices.AI.MCP.McpUtils;

namespace Tools.ExternalDevServices.AI.MCP.ConfluenceMCP;

internal class Program
{
    public class Options
    {
        [Option("personal-access-token", Required = true)]
        public string PersonalAccessToken { get; set; } = string.Empty;

        [Option("series", Required = true)]
        public string Series { get; set; } = string.Empty;
    }

    private static async Task Main(string[] args)
    {
        var result = Parser.Default.ParseArguments<Options>(args);
        if (result is NotParsed<Options>) Environment.Exit(1);

        var options = ValidateArgs(result.Value);
        HostArguments.PersonalAccessToken = options.PersonalAccessToken;
        HostArguments.Series = options.Series;

        await HostUtils.RunStdioAsync();
    }

    private static Options ValidateArgs(Options options)
    {
        if (string.IsNullOrWhiteSpace(options.PersonalAccessToken))
        {
            Console.Error.WriteLine("Personal Access Token is a required parameter.");
            Environment.Exit(1);
        }
        if (string.IsNullOrWhiteSpace(options.Series))
        {
            Console.Error.WriteLine("Series is a required parameter.");
            Environment.Exit(1);
        }

        return options;
    }
}