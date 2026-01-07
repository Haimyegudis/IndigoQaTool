namespace Tools.ExternalDevServices.Integrations.Jira;

public class Endpoints
{
    public string JiraBaseUrl { get; }
    public string ApiVersion { get; }

    public Endpoints(string jiraBaseUrl, string apiVersion)
    {
        ApiVersion = apiVersion;
        JiraBaseUrl = jiraBaseUrl.TrimEnd('/');
    }

    public string BoardsRestApi => "/rest/agile/1.0/board";
    public string SearchRestApi => $"/rest/api/{ApiVersion}";
    public string MySelf => $"{SearchRestApi}/myself";
    public string Issue => $"{SearchRestApi}/issue";
}