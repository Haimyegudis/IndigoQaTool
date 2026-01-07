using Newtonsoft.Json;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Tools.ExternalDevServices.Integrations.GitHub;

public static class CommitTypeExtensions
{
    private static string[] MergeBranchCommitHints { get; } =
    [
        "Merge branch",
        "Merge pull request",
        "Merge remote-tracking branch",
        "merge with develop",
        "Merge tag"
    ];

    public static CommitType ToCommitType(this ApiCommit commit) =>
        MergeBranchCommitHints.Any(p => commit.CommitInfo.Message.Contains(p, StringComparison.OrdinalIgnoreCase))
            ? CommitType.MergeBranchCommit
            : CommitType.UserCommit;

    public static CommitType ToCommitType(this PullRequestCommit commit) =>
        MergeBranchCommitHints.Any(p => commit.Message.Contains(p, StringComparison.OrdinalIgnoreCase))
            ? CommitType.MergeBranchCommit
            : CommitType.UserCommit;
}

public static class CommitPhaseExtensions
{
    public static PullRequestPhase ToCommitPhase(PullRequestDetails pullRequest, PullRequestReview[] reviewComments, PullRequestCommit commit)
    {
        var firstApproval = reviewComments.OrderBy(r => r.SubmittedAt).FirstOrDefault(r => r.IsApproved);
        return commit.Date <= pullRequest.LifeCycle.CreatedAt
            ? PullRequestPhase.BeforePullRequestCreation
            : firstApproval is null || commit.Date <= firstApproval.SubmittedAt
                ? PullRequestPhase.BeforePullRequestApproval
                : PullRequestPhase.AfterPullRequestApproval;
    }
}

public class PullRequestBranchesInfo
{
    public Branch From { get; set; } = new();
    public Branch To { get; set; } = new();
}

public enum CommitType
{
    None,
    UserCommit,
    MergeBranchCommit
}

public enum PullRequestPhase
{
    None,
    BeforePullRequestCreation,
    BeforePullRequestApproval,
    AfterPullRequestApproval
}

public abstract class Comment
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string User { get; set; } = "";
    public string Body { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
}

public class PullRequestComment : Comment
{
    public bool IsStartProcessComment => Body.Trim().Equals("start-process", StringComparison.OrdinalIgnoreCase);
}

public class ReviewComment : Comment
{
    public int PullRequestReviewId { get; set; }
    public int? InReplyToId { get; set; }

    public bool IsApproved => Body.Equals("APPROVED", StringComparison.OrdinalIgnoreCase);

    public static ReviewComment FromApiReviewComment(ApiPullRequestReviewComment apiReviewComment) =>
        new()
        {
            Id = apiReviewComment.Id,
            User = apiReviewComment.User.Login,
            Body = apiReviewComment.Body,
            CreatedAt = apiReviewComment.CreatedAt.ToLocalTime(),
            PullRequestReviewId = apiReviewComment.PullRequestReviewId,
            InReplyToId = apiReviewComment.InReplyToId,
            HtmlUrl = apiReviewComment.HtmlUrl
        };
}

public class PullRequestCommit
{
    public string Sha { get; set; } = "";
    public DateTime Date { get; set; }
    public string Author { get; set; } = "";
    public string Committer { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public string Message { get; set; } = "";
    public CommitType CommitType { get; set; }
    public PullRequestPhase Phase { get; set; }
    public int Additions { get; set; }
    public int Deletions { get; set; }
    public CommitFile[] Files { get; set; } = [];
}

public enum CommitFileStatus
{
    Added,
    Modified,
    Deleted,
    Renamed
}

public class CommitFile
{
    public string Sha { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? PreviousFilename { get; set; }
    public CommitFileStatus CommitFileStatus { get; set; }
    public int Additions { get; set; }
    public int Deletions { get; set; }
    public int Changes { get; set; }
    public string DomainMapping { get; set; } = "";
    public string RawUrl { get; set; } = "";
    public string? Patch { get; set; }

    public static CommitFile FromApiCommitFile(ApiCommitFile apiCommitFile) =>
        new()
        {
            Sha = apiCommitFile.Sha,
            FileName = apiCommitFile.Filename,
            PreviousFilename = apiCommitFile.PreviousFilename,
            CommitFileStatus = apiCommitFile.Status switch
            {
                "added" => CommitFileStatus.Added,
                "modified" => CommitFileStatus.Modified,
                "removed" => CommitFileStatus.Deleted,
                "renamed" => CommitFileStatus.Renamed,
                _ => throw new ArgumentOutOfRangeException(nameof(apiCommitFile.Status), apiCommitFile.Status,
                    "Unsupported file status")
            },
            Additions = apiCommitFile.Additions,
            Deletions = apiCommitFile.Deletions,
            Changes = apiCommitFile.Changes,
            RawUrl = apiCommitFile.RawUrl,
            Patch = apiCommitFile.Patch
        };
}

public class PullRequestLifeCycle
{
    public DateTime CreatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? MergedAt { get; set; }
}

public class PullRequestReview
{
    public int Id { get; set; }
    public string HtmlUrl { get; set; } = "";
    public string Body { get; set; } = "";
    public string User { get; set; } = "";

    public string State { get; set; } = "";

    public DateTime SubmittedAt { get; set; }

    public bool IsApproved => State.Equals("APPROVED", StringComparison.OrdinalIgnoreCase);
}

public class PullRequestReviewer
{
    public string User { get; set; } = "";
    public bool Approved { get; set; }
}

public class PullRequestContentInfo
{
    public int CommitsCount { get; set; }
    public int Additions { get; set; }
    public int Deletions { get; set; }
    public int ChangedFiles { get; set; }
}

public class Branch
{
    public string Name { get; set; } = string.Empty;
    public string Sha { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public class PullRequestMergeInfo
{
    public string BranchesCompare { get; set; } = "";
    public string MergeBaseSha { get; set; } = "";
    public JArray MergeBaseFiles { get; set; } = [];
}

public partial class PullRequestDetails
{
    [GeneratedRegex(@"ISW-\d+", RegexOptions.IgnoreCase)]
    private static partial Regex JiraIssuesRegex();

    public int Id { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public string HtmlUrl { get; set; } = "";
    public PullRequestMergeInfo MergeInfo { get; set; } = null!;
    public string User { get; set; } = "";
    public string State { get; set; } = "";
    public string Repo { get; set; } = "";
    public PullRequestBranchesInfo Branches { get; set; } = new();
    public PullRequestCommit[] Commits { get; set; } = [];
    public CommitFile[] PullRequestFiles { get; set; } = [];
    public PullRequestLifeCycle LifeCycle { get; set; } = new();
    public string[] Labels { get; set; } = [];
    public string Milestone { get; set; } = "";
    public PullRequestReviewer[] Reviewers { get; set; } = [];
    public PullRequestReview[] Reviews { get; set; } = [];
    public PullRequestComment[] Comments { get; set; } = [];
    public ReviewComment[] ReviewComments { get; set; } = [];
    public PullRequestContentInfo Content { get; set; } = new();
    public string[] LinkedJiraIssues => JiraIssuesRegex().Matches(Title).Select(m => m.Value).ToArray();

    public int DistinctFilesCount => PullRequestFiles.Length;

    private string? _pullRequestBodyWithoutDevopsText;

    public string PullRequestBodyWithoutDevopsText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_pullRequestBodyWithoutDevopsText))
                return _pullRequestBodyWithoutDevopsText;

            if (string.IsNullOrWhiteSpace(Body))
            {
                _pullRequestBodyWithoutDevopsText = "";
                return _pullRequestBodyWithoutDevopsText;
            }

            var pullRequestBody = Body;
            var devopsTextStartIndex = pullRequestBody.IndexOf("# Please check the relevant checkbox`s",
                StringComparison.OrdinalIgnoreCase);
            if (devopsTextStartIndex >= 0)
            {
                const string devopsEndMarker = "Comment \"start-process\" to start the process";
                var devOpsTextEndIndex =
                    pullRequestBody.IndexOf(devopsEndMarker, StringComparison.OrdinalIgnoreCase);
                if (devOpsTextEndIndex > devopsTextStartIndex)
                {
                    devOpsTextEndIndex += devopsEndMarker.Length;
                    var pullRequestBodyStart =
                        devopsTextStartIndex == 0 ? "" : pullRequestBody[..devopsTextStartIndex];
                    var pullRequestBodyEnd = devOpsTextEndIndex == pullRequestBody.Length
                        ? ""
                        : pullRequestBody[devOpsTextEndIndex..];
                    pullRequestBody = $"{pullRequestBodyStart}\r\n\r\n{pullRequestBodyEnd}";
                }
            }

            _pullRequestBodyWithoutDevopsText = pullRequestBody.Trim();
            return _pullRequestBodyWithoutDevopsText;
        }
    }

    public PullRequestCommit[] GetAllCommitsForFile(CommitFile file) =>
        Commits.Where(commit => commit.Files.Any(f =>
            f.FileName.Equals(file.FileName, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(file.PreviousFilename) &&
             f.FileName.Equals(file.PreviousFilename, StringComparison.OrdinalIgnoreCase)))).ToArray();

    public string StripDevopsCommentFromBody()
    {
        if (string.IsNullOrWhiteSpace(Body)) return "";
        
        var bodySpan = Body.AsSpan();
        var devOpsCommentIndex =
            bodySpan.IndexOf("# Please check the relevant checkbox`s", StringComparison.OrdinalIgnoreCase);
        return devOpsCommentIndex < 0
            ? Body
            : bodySpan[..devOpsCommentIndex].Trim().ToString();
    }

    public override string ToString() => JsonConvert.SerializeObject(this);

    public DateTime? FirstStartProcessAfterLastCommitDate =>
        Comments.FirstOrDefault(c => c.IsStartProcessComment && c.CreatedAt > Commits.LastOrDefault()?.Date)?.CreatedAt;

    public static PullRequestDetails FromApiPullRequestDetails(string repo,
        ApiPullRequestDetails apiPullRequestDetails,
        ApiPullRequestComment[] comments,
        ApiPullRequestReviewComment[] reviewComments,
        ApiPullRequestReview[] reviews,
        ApiCommitDetails[] commits,
        ApiCommitFile[] pullRequestFiles,
        string branchesCompare)
    {
        var approvedBy = reviews.Where(r => r.IsApproved).Select(r => r.User.Login).ToHashSet();

        var mergeBaseSha = JObject.Parse(branchesCompare)["merge_base_commit"]?["sha"]?.Value<string>() ??
                           throw new InvalidOperationException("Failed to get merge base sha");

        var compareJson = JObject.Parse(branchesCompare);
        var mergeFilesArray = (JArray?)compareJson["files"] ?? [];

        var pullRequestDetails = new PullRequestDetails
        {
            Id = apiPullRequestDetails.Id,
            Number = apiPullRequestDetails.Number,
            Title = apiPullRequestDetails.Title,
            Body = apiPullRequestDetails.Body,
            HtmlUrl = apiPullRequestDetails.HtmlUrl,
            User = apiPullRequestDetails.User.Login,
            State = apiPullRequestDetails.State,
            Repo = repo,
            MergeInfo = new PullRequestMergeInfo
            {
                BranchesCompare = branchesCompare,
                MergeBaseSha = mergeBaseSha,
                MergeBaseFiles = mergeFilesArray
            },
            Commits = commits.OrderBy(c => c.CommitInfo.Author.Date)
                .Select(commit => new PullRequestCommit
                {
                    Sha = commit.Sha,
                    Message = commit.CommitInfo.Message,
                    Author = commit.CommitInfo.Author.User,
                    Committer = commit.Committer?.Login ?? "",
                    HtmlUrl = commit.HtmlUrl,
                    Date = commit.CommitInfo.Author.Date.ToLocalTime(),
                    CommitType = CommitType.None, // Should be filled after saving to DB so it can be dynamic without having to recreate DB
                    Phase = PullRequestPhase.None, // Should be filled after saving to DB so it can be dynamic without having to recreate DB
                    Additions = commit.Stats.Additions,
                    Deletions = commit.Stats.Deletions,
                    Files = commit.Files.Select(CommitFile.FromApiCommitFile).ToArray()
                }).ToArray(),
            PullRequestFiles = pullRequestFiles.Select(CommitFile.FromApiCommitFile).ToArray(),
            Branches = new PullRequestBranchesInfo
            {
                From = new Branch
                {
                    Name = apiPullRequestDetails.From.Name,
                    Sha = apiPullRequestDetails.From.Sha,
                    Label = apiPullRequestDetails.From.Label
                },
                To = new Branch
                {
                    Name = apiPullRequestDetails.To.Name,
                    Sha = apiPullRequestDetails.To.Sha,
                    Label = apiPullRequestDetails.To.Label
                }
            },
            LifeCycle = new PullRequestLifeCycle
            {
                CreatedAt = apiPullRequestDetails.CreatedAt.ToLocalTime(),
                ClosedAt = apiPullRequestDetails.ClosedAt?.ToLocalTime(),
                ApprovedAt = reviews.OrderBy(r => r.SubmittedAt).FirstOrDefault(review => review.IsApproved)
                    ?.SubmittedAt.ToLocalTime(),
                MergedAt = apiPullRequestDetails.MergedAt?.ToLocalTime()
            },
            Labels = apiPullRequestDetails.Labels.Select(label => label.Name).ToArray(),
            Milestone = apiPullRequestDetails.Milestone?.Title ?? "",
            Reviewers = apiPullRequestDetails.RequestedReviewers.Select(r => new PullRequestReviewer
                    { User = r.Login, Approved = approvedBy.Contains(r.Login) })
                .Concat(reviews.Where(r => r.User.Login != apiPullRequestDetails.User.Login).Select(r =>
                    new PullRequestReviewer { User = r.User.Login, Approved = approvedBy.Contains(r.User.Login) }))
                .DistinctBy(r => r.User)
                .OrderBy(r => (r.Approved ? 0 : 1, r.User)).ToArray(),
            Reviews = reviews.Where(r => r.User.Login != apiPullRequestDetails.User.Login)
                .OrderBy(r => r.SubmittedAt)
                .Select(review =>
                    new PullRequestReview
                    {
                        Id = review.Id,
                        HtmlUrl = review.HtmlUrl,
                        Body = review.Body,
                        User = review.User.Login,
                        State = review.State,
                        SubmittedAt = review.SubmittedAt.ToLocalTime()
                    }).OrderBy(r => r.SubmittedAt).ToArray(),
            Comments = comments.OrderBy(c => c.CreatedAt).Select(comment => new PullRequestComment
            {
                Id = comment.Id,
                User = comment.User.Login,
                Body = comment.Body,
                CreatedAt = comment.CreatedAt.ToLocalTime(),
                HtmlUrl = comment.HtmlUrl
            }).ToArray(),
            ReviewComments = reviewComments.OrderBy(c => c.CreatedAt).Select(comment => new ReviewComment
            {
                Id = comment.Id,
                User = comment.User.Login,
                Body = comment.Body,
                CreatedAt = comment.CreatedAt.ToLocalTime(),
                PullRequestReviewId = comment.PullRequestReviewId,
                InReplyToId = comment.InReplyToId,
                HtmlUrl = comment.HtmlUrl
            }).ToArray(),
            Content = new PullRequestContentInfo
            {
                Additions = apiPullRequestDetails.Additions,
                Deletions = apiPullRequestDetails.Deletions,
                CommitsCount = apiPullRequestDetails.Commits,
                ChangedFiles = apiPullRequestDetails.ChangedFiles
            }
        };

        foreach (var pullRequestCommit in pullRequestDetails.Commits)
        {
            pullRequestCommit.CommitType = pullRequestCommit.ToCommitType();
            pullRequestCommit.Phase =
                CommitPhaseExtensions.ToCommitPhase(pullRequestDetails, pullRequestDetails.Reviews, pullRequestCommit);
        }

        return pullRequestDetails;
    }
}

public enum FileChangeType
{
    Added,
    Modified,
    Deleted
}

public record FileDiffs(string FileName, FileChangeType ChangeType, string FullDiff, IReadOnlyCollection<string> Diffs);