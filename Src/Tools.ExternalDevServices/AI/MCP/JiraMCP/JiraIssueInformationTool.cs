using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Tools.ExternalDevServices.Integrations.Jira;

namespace Tools.ExternalDevServices.AI.MCP.JiraMCP;

[McpServerToolType]
public static class JiraIssueInformationTool
{
    public const string JiraUrl = "https://hp-jira.external.hp.com";
    public const string JiraApiVersion = "2";
    
    [McpServerTool, Description(
         "Gets the Jira issue information including relevant requirements for the issue with the specified key/id")]
    public static async Task<IssueInfo> GetJiraIssueInformation(McpServer mcpServer, string issueKeyOrId)
    {
        Validate(out var jiraPersonalAccessToken,
            out var user);

        using var jiraRestApiClient = new JiraRestApiClient(JiraUrl, JiraApiVersion, user, jiraPersonalAccessToken);
        return await IssueInfo.GetIssueInformationAsync(jiraRestApiClient, issueKeyOrId);
    }

#pragma warning disable IDE0051
    private static void ValidateForSampling(McpServer mcpServer,
#pragma warning restore IDE0051
        out string jiraPersonalAccessToken,
        out string user,
        out IChatClient samplingChatClient)
    {
        Validate(out jiraPersonalAccessToken, out user);

        if (mcpServer.ClientCapabilities?.Sampling is null)
        {
            throw new McpException("Sampling is not supported.");
        }

        samplingChatClient = mcpServer.AsSamplingChatClient();
        if (samplingChatClient is null)
        {
            throw new McpException("Sampling is not supported.");
        }
    }

    private static void Validate(out string jiraPersonalAccessToken, out string user)
    {
        jiraPersonalAccessToken = "";
        user = "";

        if (string.IsNullOrWhiteSpace(HostArguments.JiraPersonalAccessToken))
        {
            throw new InvalidOperationException("PersonalAccessToken is not set.");
        }
        jiraPersonalAccessToken = HostArguments.JiraPersonalAccessToken;

        if (string.IsNullOrWhiteSpace(HostArguments.User))
        {
            throw new InvalidOperationException("User is not set.");
        }
        user = HostArguments.User;
    }
}