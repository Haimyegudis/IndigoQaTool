using System.Reflection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tools.ExternalDevServices.AI.MCP.McpUtils;

public static class HostUtils
{
    /// <summary>
    /// Starts the MCP server with a stdio transport and default configuration.
    /// </summary>
    /// <returns></returns>
    public static async Task RunStdioAsync()
    {
        // Set the current directory to the base directory so that the MCP server can find its resources when running from VSCode etc.
        Environment.CurrentDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

        var builder = Host.CreateEmptyApplicationBuilder(settings: null);

        builder.Logging.AddConsole(consoleLogOptions =>
        {
            // Configure all logs to go to stderr
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Information;
        });

        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(toolAssembly: Assembly.GetEntryAssembly(), serializerOptions: new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
            {
                Converters = { new JsonStringEnumConverter() },
                RespectNullableAnnotations = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never
            })
            .WithPromptsFromAssembly(promptAssembly: Assembly.GetEntryAssembly())
            .WithResourcesFromAssembly(resourceAssembly: Assembly.GetEntryAssembly());

        await builder.Build().RunAsync();
    }
}