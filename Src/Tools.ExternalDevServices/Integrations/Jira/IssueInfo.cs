using System.Text.RegularExpressions;
using static Tools.ExternalDevServices.Integrations.Jira.JiraRestApiTypes;

namespace Tools.ExternalDevServices.Integrations.Jira;

public partial class IssueInfo
{
    // Source-generated regex: matches absolute URIs with a scheme (e.g., https://, ftp://, etc.)
    [GeneratedRegex(@"\b(?:[a-z][a-z0-9+\-.]*):\/\/[^\s<>()\[\]{}""’']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UrlRegex();

    // Source-generated regex: matches absolute Confluence URLs for the specified base host:port
    [GeneratedRegex(@"\bhttps?:\/\/v-indigo-confluence\.inr\.rd\.hpicorp\.net:6443(?:[\/?#][^\s<>()\[\]{}""’']*)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConfluenceUrlRegex();

    /// <summary>
    /// Jira key of the issue
    /// </summary>
    public string Key { get; set; } = "";

    public string IssueUrl { get; set; } = "";

    /// <summary>
    /// Gets or sets the name of the individual assigned to the issue.
    /// </summary>
    public string Assignee { get; set; } = "";

    /// <summary>
    /// Gets or sets the name of the issue status.
    /// </summary>
    public string Status { get; set; } = "";

    /// <summary>
    /// Gets or sets the name of the issue type (e.g. Task, Bug/Defect, RC etc.).
    /// </summary>
    public string TypeOfRequest { get; set; } = "";

    /// <summary>
    /// Short high-level summary of the issue
    /// </summary>
    public string Summary { get; set; } = "";

    /// <summary>
    /// When not empty, contains description of the issue and how to reproduce
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Short high-level summary of the issue's parent epic
    /// </summary>
    public string ParentEpicSummary { get; set; } = "";

    /// <summary>
    /// When not empty, contains description of the issue's parent epic and how to reproduce
    /// </summary>
    public string ParentEpicDescription { get; set; } = "";

    /// <summary>
    /// Comments for the issue. Can sometimes contain links to Confluence requirements change document 
    /// </summary>
    public Comment[] Comments { get; set; } = [];

    public string IssueLinkedSoftwareRequirementsDocumentInConfluence { get; set; } = "";

    public string[] IssueLinkedSoftwareRequirementsDocumentInConfluenceArray =>
        ExtractUrlsArray(IssueLinkedSoftwareRequirementsDocumentInConfluence);

    /// <summary>
    /// When not empty, provides a link to the Software Requirement Confluence document that is linked to this issue (including containing epic)
    /// </summary>
    public string EpicLinkedSoftwareRequirementsDocumentInConfluence { get; set; } = "";

    public string[] EpicLinkedSoftwareRequirementsDocumentInConfluenceArray =>
        ExtractUrlsArray(EpicLinkedSoftwareRequirementsDocumentInConfluence);

    public string[] AllConfluenceLinksArray =>
        IssueLinkedSoftwareRequirementsDocumentInConfluenceArray
            .Concat(EpicLinkedSoftwareRequirementsDocumentInConfluenceArray)
            .Concat(ConfluenceUrlRegex().Matches(Summary)
                .Concat(ConfluenceUrlRegex().Matches(Description))
                .Concat(ConfluenceUrlRegex().Matches(string.Join("\r\n", Comments.Select(c => c.Body))))
                .Where(m => m.Success)
                .Select(m => m.Value.TrimEnd('.', ',', ';', ':', ')', ']', '}', '"', '\'', '>', '…')))
            .Where(s => Uri.TryCreate(s, UriKind.Absolute, out _))
            .Distinct()
            .ToArray();

    public string[] FixVersions { get; set; } = [];
    public string Priority { get; set; } = "";
    public string Severity { get; set; } = "";

    public static async Task<IssueInfo> GetIssueInformationAsync(JiraRestApiClient jiraRestApiClient, string defectKeyOrId)
    {
        var issue = await jiraRestApiClient.GetIssueAsync(defectKeyOrId);
        var epic = string.IsNullOrEmpty(issue.Fields.EpicKey) ? null : await jiraRestApiClient.GetEpicAsync(issue.Fields.EpicKey);
        
        return ToIssueInfo(issue, epic, $"{jiraRestApiClient.Endpoints.JiraBaseUrl}{issue.BrowserRelativeUrl}");
    }

    public static IssueInfo ToIssueInfo(Issue issue, Epic? parentEpic, string issueUrl) =>
        new()
        {
            Key = issue.Key,
            IssueUrl = issueUrl,
            Assignee = issue.Fields.Assignee.DisplayName,
            Status = issue.Fields.Status.Name,
            TypeOfRequest = issue.Fields.TypeOfRequest?.Value ?? "",
            Summary = issue.Fields.Summary,
            Description = issue.Fields.Description ?? "",
            ParentEpicSummary = parentEpic?.Fields.Summary ?? "",
            ParentEpicDescription = parentEpic?.Fields.Description ?? "",
            IssueLinkedSoftwareRequirementsDocumentInConfluence = issue.Fields.RequirementsUrl ?? "",
            EpicLinkedSoftwareRequirementsDocumentInConfluence =
                parentEpic?.Fields.LinkToSoftwareRequirementsDocumentInConfluence ?? "",
            Comments = issue.Fields.CommentsField?.Comments ?? [],
            FixVersions = issue.Fields.FixVersions?.Select(fv => fv.Name).ToArray() ?? [],
            Priority = issue.Fields.Priority?.Name ?? "",
            Severity = issue.Fields.Severity?.Value ?? ""
        };

    private string[] ExtractUrlsArray(string linkedSoftwareRequirementsDocumentInConfluence)
    {
        if (string.IsNullOrWhiteSpace(linkedSoftwareRequirementsDocumentInConfluence)) return [];

        var matches = UrlRegex().Matches(linkedSoftwareRequirementsDocumentInConfluence);
        if (matches.Count == 0) return [];

        var results = new List<string>(matches.Count);
        foreach (Match m in matches)
        {
            if (!m.Success) continue;

            var candidate = m.Value;

            // Trim common trailing punctuation often attached in prose or lists
            candidate = candidate.TrimEnd('.', ',', ';', ':', ')', ']', '}', '"', '\'', '>', '…');

            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                results.Add(uri.ToString());
            }
        }

        return results.Distinct().ToArray();
    }
}