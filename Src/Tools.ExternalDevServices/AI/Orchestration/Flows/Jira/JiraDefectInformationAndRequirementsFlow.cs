using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Newtonsoft.Json;
using Tools.ExternalDevServices.AI.Orchestration.Flows.Confluence;
using Tools.ExternalDevServices.Integrations.Confluence;
using Tools.ExternalDevServices.Integrations.Jira;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Flows.Jira;

public class JiraDefectInformation
{
    public IssueInfo IssueInfo { get; set; } = null!;
    public string? AdditionalInformationFromConfluence { get; set; }
}

public class JiraDefectInformationAndRequirementsFlow
{
    private readonly JiraRestApiClient _jiraRestApiClient;
    private readonly ILogger? _logger;

    private readonly QuerySpecificConfluenceDocumentsFlow _querySpecificConfluenceDocumentsFlow;

    public JiraDefectInformationAndRequirementsFlow(JiraRestApiClient jiraRestApiClient,
        ConfluenceRestApiClient confluenceRestApiClient,
        IChatClient chatClient,
        ILogger? logger)
    {
        _jiraRestApiClient = jiraRestApiClient;
        _logger = logger;

        _querySpecificConfluenceDocumentsFlow = new QuerySpecificConfluenceDocumentsFlow(chatClient, confluenceRestApiClient, logger);
    }

    public async Task<JiraDefectInformation> GetDefectInformationAndRequirementsAsync(string defectKey, CancellationToken ct)
    {
        using var diagnostics = new DiagnosticsHelper(GetType(), _logger);

        try
        {
            var defectInformation =
                await IssueInfo.GetIssueInformationAsync(_jiraRestApiClient, defectKey);
            var defectInformationJson = JsonConvert.SerializeObject(defectInformation);

            diagnostics.AddInformation($"**Defect Information for {defectKey}:**\r\n{defectInformationJson}");

            if (defectInformation.AllConfluenceLinksArray.Length == 0)
            {
                diagnostics.AddInformation($"No Confluence links found for defect {defectKey}.");
                return new JiraDefectInformation
                {
                    IssueInfo = defectInformation,
                    AdditionalInformationFromConfluence = null
                };
            }

            var query = $"""
                         Extract the Confluence requirements that are directly related to the below defect.
                         Include any information that is required for the implementation, e.g. Press Messages, Events, Global Configuration flags etc.
                         If there are no relevant requirements just output "";

                         The defect details are:
                         {defectInformationJson}
                         """;
            diagnostics.AddInformation($"**Query:**\r\n{query}");

            var response =
                await _querySpecificConfluenceDocumentsFlow.QueryConfluenceAsync(query,
                    defectInformation.AllConfluenceLinksArray, ct);
            diagnostics.AddInformation($"**Response:**\r\n{response}");
            return new JiraDefectInformation
            {
                IssueInfo = defectInformation,
                AdditionalInformationFromConfluence = response
            };
        }
        catch (Exception ex)
        {
            diagnostics.AddError($"**Error getting information and requirements for defect {defectKey}:**\r\n{ex}");
            throw new McpException($"Getting information and requirements for defect {defectKey} failed, check diagnostics file for more information.");
        }
        
    }
}