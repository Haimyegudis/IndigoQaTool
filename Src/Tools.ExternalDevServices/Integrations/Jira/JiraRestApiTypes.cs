using Newtonsoft.Json;

namespace Tools.ExternalDevServices.Integrations.Jira;

public class JiraRestApiTypes
{
    public abstract class Response<T>
    {
        public int MaxResults { get; set; }
        public int StartAt { get; set; }

        public abstract T[] Items { get; set; }
        public abstract bool HasMoreItems { get; }
    }

    public class ValuesResponse<T> : Response<T>
    {
        public bool IsLast { get; set; }
        [JsonProperty("Values")]
        public override T[] Items { get; set; } = [];

        public override bool HasMoreItems => !IsLast;
    }

    public class IssuesResponse : Response<Issue>
    {
        public int Total { get; set; }
        [JsonProperty("Issues")]
        public override Issue[] Items { get; set; } = [];

        public override bool HasMoreItems => Total == MaxResults;
    }

    public class Sprint
    {
        public int Id { get; set; }
        public string Self { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public string CompleteDate { get; set; } = string.Empty;
        public string ActivatedDate { get; set; } = string.Empty;
        public int OriginBoardId { get; set; }
        public string Goal { get; set; } = string.Empty;
        public bool Synced { get; set; }
        public bool AutoStartStop { get; set; }
    }

    public class NameAndDescriptionProperty
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";

        public override string ToString() => Name;
    }

    public class TimeTracking
    {
        public string OriginalEstimate { get; set; } = "";
        public string RemainingEstimate { get; set; } = "";
        public string TimeSpent { get; set; } = "";
        public int OriginalEstimateSeconds { get; set; }
        public int RemainingEstimateSeconds { get; set; }
        public int TimeSpentSeconds { get; set; }
    }

    public class User
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string EmailAddress { get; set; } = "";

        public override string ToString() => DisplayName;
    }

    public class Priority
    {
        public string Name { get; set; } = "";

        public override string ToString() => Name;
    }

    public class CustomField
    {
        public string Id { get; set; } = "";
        public string Value { get; set; } = "";

        public override string ToString() => Value;
    }

    public class Comment
    {
        public User Author { get; set; } = null!;
        public string Body { get; set; } = "";
        public string Created { get; set; } = "";
        public string? Updated { get; set; }

        public override string ToString() => $"[{Author}]: {Body}";
    }

    public class CommentsField
    {
        public Comment[] Comments { get; set; } = [];
    }

    public class IssueFields
    {
        public string Summary { get; set; } = "";
        public string? Description { get; set; }
        public NameAndDescriptionProperty IssueType { get; set; } = null!;
        [JsonProperty("customfield_14530")]
        public CustomField? TypeOfRequest { get; set; }
        [JsonProperty("customfield_10006")]
        public string EpicKey { get; set; } = "";

        public TimeTracking? TimeTracking { get; set; } = null!;
        public string[] Labels { get; set; } = [];
        public NameAndDescriptionProperty[]? Versions { get; set; } = null;
        public NameAndDescriptionProperty[]? FixVersions { get; set; } = null;
        public User Reporter { get; set; } = null!;
        [JsonProperty("customfield_46303")]
        public CustomField? ProgramStatus { get; set; }
        public Priority? Priority { get; set; }
        [JsonProperty("customfield_17400")]
        public CustomField? Severity { get; set; }

        public string Created { get; set; } = "";
        public NameAndDescriptionProperty? Resolution { get; set; }
        public string? ResolutionDate { get; set; }

        [JsonProperty("customfield_10501")]
        public CustomField? Program { get; set; }
        [JsonProperty("customfield_46950")]
        public CustomField? Team { get; set; }

        public User Assignee { get; set; } = null!;
        public User Creator { get; set; } = null!;
        public NameAndDescriptionProperty Status { get; set; } = null!;
        [JsonProperty("customfield_47610")]
        public CustomField[]? RejectReason { get; set; }
        [JsonProperty("customfield_28703")]
        public string? Logs { get; set; }
        [JsonProperty("customfield_11408")]
        public CustomField? Reproducibility { get; set; }
        [JsonProperty("comment")]
        public CommentsField? CommentsField { get; set; }
        [JsonProperty("customfield_40407")]
        public string? RequirementsUrl { get; set; }
    }

    public class Issue
    {
        public int Id { get; set; }
        public string Self { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public IssueFields Fields { get; set; } = null!;
        public string BrowserRelativeUrl => $"/browse/{Key}";

        public override string ToString() => $"{Key} ({Fields.IssueType}) {Fields.Summary}";
    }

    public class EpicFields
    {
        public string Summary { get; set; } = "";
        public string? Description { get; set; }
        public string[]? Labels { get; set; }
        [JsonProperty("customfield_48934")]
        public string? LinkToSoftwareRequirementsDocumentInConfluence { get; set; }
        [JsonProperty("customfield_48936")]
        public string? LinkToIntegrationDocumentInConfluence { get; set; }
    }

    public class Epic
    {
        public int Id { get; set; }
        public string Self { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public EpicFields Fields { get; set; } = null!;

        public string BrowserRelativeUrl => $"/browse/{Key}";

        public override string ToString() => $"{Key} {Fields.Summary}";
    }
}