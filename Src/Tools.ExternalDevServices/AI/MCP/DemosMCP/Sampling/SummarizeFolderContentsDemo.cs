using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace Tools.ExternalDevServices.AI.MCP.DemosMCP.Sampling;

[McpServerToolType]
public static class SummarizeFolderContentsDemo
{
    [McpServerTool(Name = "summarize_files_contents"), Description("Summarizes files contents.")]
    public static async Task<string> SummarizeFilesContentsAsync(McpServer mcpServer, string parentFolder, CancellationToken ct)
    {
        var samplingChatClient = mcpServer.AsSamplingChatClient();

        var filesSummaries = new List<string>();
        foreach (var file in Directory.GetFiles(parentFolder))
        {
            var fileSummaryResponse = await samplingChatClient.GetResponseAsync(new ChatMessage(ChatRole.User,
                $"""
                 Provide a brief summary of the following file content, capturing its purpose and intent:

                 {await File.ReadAllTextAsync(file, ct)}
                 """), cancellationToken: ct);
            filesSummaries.Add($"**{Path.GetFileName(file)}**: {fileSummaryResponse.Text}");
        }

        var folderSummaryResponse = await samplingChatClient.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System,
                """
                You are a documentation expert, excelling in capturing the essence and purpose of folder when given its contents.
                When given summaries of files in a folder, you provide a detailed summary of the folder purpose and intent.
                """),
            new ChatMessage(ChatRole.User,
                $"""
                 Folder: {parentFolder}
                 Number of files: {filesSummaries.Count}
                 Files summaries:
                 {string.Join("\n", filesSummaries)}
                 """)
        ], cancellationToken: ct);

        return folderSummaryResponse.Text;
    }
}