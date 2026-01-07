using Newtonsoft.Json;

namespace Tools.ExternalDevServices.Integrations.GitHub;

public abstract class BaseApiType
{
    public override string ToString() => JsonConvert.SerializeObject(this);
}

public class ApiPullRequestsQueryResponse : BaseApiType
{
    [JsonProperty("items")]
    public ApiPullRequestResponseItem[] Items { get; set; } = [];
}


public class ApiPullRequestResponseItem : BaseApiType
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("repository_url")]
    public string RepositoryUrl { get; set; } = "";

    public string Repo => RepositoryUrl
        .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();

    [JsonProperty("number")]
    public int Number { get; set; }

    [JsonProperty("user")]
    public ApiUser User { get; set; } = new();

    [JsonProperty("title")]
    public string Title { get; set; } = "";

    [JsonProperty("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("closed_at")]
    public DateTime? ClosedAt { get; set; }

    [JsonProperty("pull_request")]
    public ApiPullRequestResponseItemInfo PullRequestInfo { get; set; } = new();
}

public class ApiPullRequestResponseItemInfo : BaseApiType
{
    [JsonProperty("merged_at")]
    public DateTime? ClosedAt { get; set; }
}

public class ApiPullRequestDetails : BaseApiType
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("number")]
    public int Number { get; set; }

    [JsonProperty("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonProperty("user")]
    public ApiUser User { get; set; } = new();

    [JsonProperty("head")]
    public ApiBranch From { get; set; } = new();

    [JsonProperty("base")]
    public ApiBranch To { get; set; } = new();

    [JsonProperty("state")]
    public string State { get; set; } = "";

    [JsonProperty("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("closed_at")]
    public DateTime? ClosedAt { get; set; }

    [JsonProperty("merged_at")]
    public DateTime? MergedAt { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; } = "";

    [JsonProperty("body")]
    public string Body { get; set; } = "";

    [JsonProperty("labels")]
    public ApiLabel[] Labels { get; set; } = [];

    [JsonProperty("milestone")]
    public ApiPullRequestMilestone? Milestone { get; set; }

    [JsonProperty("commits")]
    public int Commits { get; set; }

    [JsonProperty("additions")]
    public int Additions { get; set; }

    [JsonProperty("deletions")]
    public int Deletions { get; set; }

    [JsonProperty("changed_files")]
    public int ChangedFiles { get; set; }

    [JsonProperty("requested_reviewers")]
    public ApiUser[] RequestedReviewers { get; set; } = [];
}

public class ApiUser : BaseApiType
{
    [JsonProperty("login")]
    public string Login { get; set; } = "";

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; } = "";
}

public class ApiContributor : ApiUser
{
    [JsonProperty("contributions")]
    public int Contributions { get; set; }
}

public class ApiLabel : BaseApiType
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";
}

public class ApiPullRequestReview : BaseApiType
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonProperty("body")]
    public string Body { get; set; } = "";

    [JsonProperty("user")]
    public ApiUser User { get; set; } = new();

    [JsonProperty("state")]
    public string State { get; set; } = "";

    [JsonProperty("submitted_at")]
    public DateTime SubmittedAt { get; set; }

    public bool IsApproved => State.Equals("APPROVED", StringComparison.OrdinalIgnoreCase);
}

public abstract class ApiComment : BaseApiType
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("user")]
    public ApiUser User { get; set; } = new();

    [JsonProperty("body")]
    public string Body { get; set; } = "";

    [JsonProperty("html_url")]
    public string HtmlUrl { get; set; } = "";
}

public class ApiPullRequestComment : ApiComment
{
}

public class ApiPullRequestReviewComment : ApiComment
{
    [JsonProperty("pull_request_review_id")]
    public int PullRequestReviewId { get; set; }

    [JsonProperty("in_reply_to_id")]
    public int? InReplyToId { get; set; }

    public bool IsApproved => Body.Equals("APPROVED", StringComparison.OrdinalIgnoreCase);
}

public class ApiBranch : BaseApiType
{
    [JsonProperty("ref")]
    public string Name { get; set; } = "";
    public string Sha { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public class ApiPullRequestMilestone : BaseApiType
{
    [JsonProperty("title")]
    public string Title { get; set; } = "";
}

public class ApiUserCommit
{
    [JsonProperty("name")]
    public string User { get; set; } = "";

    [JsonProperty("date")]
    public DateTime Date { get; set; }
}

public class ApiCommitInfo
{
    [JsonProperty("author")]
    public ApiUserCommit Author { get; set; } = new();

    [JsonProperty("committer")]
    public ApiUserCommit Committer { get; set; } = new();

    [JsonProperty("message")]
    public string Message { get; set; } = "";
}

public class ApiCommit
{
    [JsonProperty("sha")]
    public string Sha { get; set; } = "";

    [JsonProperty("node_id")]
    public string NodeId { get; set; } = "";

    [JsonProperty("url")]
    public string Url { get; set; } = "";

    [JsonProperty("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonProperty("commit")]
    public ApiCommitInfo CommitInfo { get; set; } = new();

    [JsonProperty("author")]
    public ApiUser Author { get; set; } = new();

    [JsonProperty("committer")]
    public ApiUser? Committer { get; set; }
}

public class ApiCommitStats
{
    [JsonProperty("total")]
    public int Total { get; set; }

    [JsonProperty("additions")]
    public int Additions { get; set; }

    [JsonProperty("deletions")]
    public int Deletions { get; set; }
}

public class ApiCommitFile
{
    [JsonProperty("sha")]
    public string Sha { get; set; } = "";

    [JsonProperty("filename")]
    public string Filename { get; set; } = "";

    [JsonProperty("previous_filename")]
    public string? PreviousFilename { get; set; }

    [JsonProperty("blob_url")]
    public string BlobUrl { get; set; } = "";

    [JsonProperty("status")]
    public string Status { get; set; } = "";

    [JsonProperty("additions")]
    public int Additions { get; set; }

    [JsonProperty("deletions")]
    public int Deletions { get; set; }

    [JsonProperty("changes")]
    public int Changes { get; set; }

    [JsonProperty("patch")]
    public string? Patch { get; set; }

    [JsonProperty("raw_url")]
    public string RawUrl { get; set; } = "";
}

public class ApiCommitDetails : ApiCommit
{
    [JsonProperty("stats")]
    public ApiCommitStats Stats { get; set; } = new();

    [JsonProperty("files")]
    public ApiCommitFile[] Files { get; set; } = [];
}

public class ApiRateLimit : BaseApiType
{
    [JsonProperty("resources")]
    public ApiRateLimitResources Resources { get; set; } = new();

    [JsonProperty("rate")]
    public ApiRate Rate { get; set; } = new();
}

public class ApiRateLimitResources : BaseApiType
{
    [JsonProperty("core")]
    public ApiRate Core { get; set; } = new();

    [JsonProperty("search")]
    public ApiRate Search { get; set; } = new();

    [JsonProperty("graphql")]
    public ApiRate Graphql { get; set; } = new();

    [JsonProperty("integration_manifest")]
    public ApiRate IntegrationManifest { get; set; } = new();

    [JsonProperty("source_import")]
    public ApiRate SourceImport { get; set; } = new();

    [JsonProperty("code_scanning_upload")]
    public ApiRate CodeScanningUpload { get; set; } = new();

    [JsonProperty("actions_runner_registration")]
    public ApiRate ActionsRunnerRegistration { get; set; } = new();

    [JsonProperty("scim")]
    public ApiRate Scim { get; set; } = new();

    [JsonProperty("dependency_snapshots")]
    public ApiRate DependencySnapshots { get; set; } = new();

    [JsonProperty("code_search")]
    public ApiRate CodeSearch { get; set; } = new();
}

public class ApiRate : BaseApiType
{
    [JsonProperty("limit")]
    public int Limit { get; set; }

    [JsonProperty("used")]
    public int Used { get; set; }

    [JsonProperty("remaining")]
    public int Remaining { get; set; }

    [JsonProperty("reset")]
    public int Reset { get; set; }

    public DateTime ResetDateTime => DateTimeOffset.FromUnixTimeSeconds(Reset).DateTime;
    public TimeSpan TimeToReset => ResetDateTime - DateTime.Now;
}