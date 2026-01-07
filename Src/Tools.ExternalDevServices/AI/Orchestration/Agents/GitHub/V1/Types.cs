using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Tools.ExternalDevServices.Integrations.GitHub;

namespace Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V1;

[Flags]
public enum DiffChangeType
{
    None = 0,
    CoreChange = 1 << 0,
    BehavioralAdapt = 1 << 1,
    StraightforwardAdapt = 1 << 2,
    DesignChange = 1 << 3,
    ContentChange = 1 << 4,
    Cosmetic = 1 << 5,
    Deletion = 1 << 6,
    Any = ~0
}

public class DiffClassificationResponse
{
    [JsonConverter(typeof(StringEnumConverter))]
    public DiffChangeType DiffChangeType { get; set; }
    public string DiffSummary { get; set; } = "";

    public override string ToString() => JsonConvert.SerializeObject(this, Formatting.None);
}

public record ClassifiedFile(CommitFile File, FileChangeType FileChangeType, string Diff, DiffClassificationResponse DiffClassificationResponse)
{
    public bool IsRelevantForReview => DiffClassificationResponse.DiffChangeType.HasFlag(DiffChangeType.CoreChange) ||
                                       DiffClassificationResponse.DiffChangeType.HasFlag(DiffChangeType.BehavioralAdapt);

    public string ToClassificationString(bool includeClassificationDetails, DiffChangeType includeDiffLevel)
    {
        var classificationDetails = includeClassificationDetails
            ? $"\r\n**{nameof(DiffClassificationResponse.DiffChangeType)}**: {DiffClassificationResponse.DiffChangeType}\r\n**{nameof(DiffClassificationResponse.DiffSummary)}**: {DiffClassificationResponse.DiffSummary}"
            : "";
        var diffDetails =
            includeDiffLevel == DiffChangeType.Any || DiffClassificationResponse.DiffChangeType <= includeDiffLevel
                ? $"\r\n---**Diff**:\r\n{Diff}"
                : "";
        return $"**File**: {File.FileName}{classificationDetails}{diffDetails}";
    }

    public static string ToClassificationString(IReadOnlyCollection<ClassifiedFile> classifiedFiles, bool includeClassificationDetails, DiffChangeType includeDiffLevel)
    {
        return string.Join("\r\n\r\n---",
            classifiedFiles.OrderBy(cf => cf.DiffClassificationResponse.DiffChangeType)
                .Select(f => f.ToClassificationString(includeClassificationDetails, includeDiffLevel)));
    }
}

public record PullRequestFilesClassificationResponse(PullRequestDetails PullRequest, IReadOnlyCollection<ClassifiedFile> ClassifiedFiles);

// ReSharper disable once InconsistentNaming
public record PullRequestOverviewCommentResponse(
    PullRequestFilesClassificationResponse PullRequestFilesClassificationResponse,
    string OverviewCommentForHumanReviewers,
    string OverviewForLLMReviewers);

public class CodeComment
{
    [JsonIgnore]
    public string Context { get; set; } = "";
    public int Line { get; set; }
    public string Comment { get; set; } = "";
    public string FixSuggestion { get; set; } = "";
}

public class FileCodeReview
{
    public string FileComments { get; set; } = "";
    public CodeComment[] CodeComments { get; set; } = [];

    public bool IsEmpty => string.IsNullOrWhiteSpace(FileComments) && CodeComments.Length == 0;
}

public record ClassifiedFileCodeReview(ClassifiedFile ClassifiedFile, FileCodeReview FileCodeReview);

public record PullRequestFilesCodeReviewResponse(
    PullRequestFilesClassificationResponse PullRequestFilesClassificationResponse,
    IReadOnlyCollection<ClassifiedFileCodeReview> ClassifiedFilesCodeReview);

public static class PullRequestReviewMarkdownUtils
{
    public static string GetPullRequestReviewMarkdown(
        PullRequestOverviewCommentResponse pullRequestOverviewCommentResponse,
        PullRequestFilesCodeReviewResponse pullRequestFilesCodeReviewResponse) =>
        $"""
         # {pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.PullRequest.Title} #{pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.PullRequest.Number}<br>
         **Created**: {pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.PullRequest.LifeCycle.CreatedAt:yyyy MMM dd HH:mm}<br>
         **Author**: {pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.PullRequest.User}<br>
         **Description**: {pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.PullRequest.StripDevopsCommentFromBody()}<br>

         ## Files Classifications
         <details>
         <summary>Click to expand</summary>
         
         {string.Join("\r\n\r\n", pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.ClassifiedFiles.OrderBy(c => c.DiffClassificationResponse.DiffChangeType).Select(ToClassifiedFileMarkdown))}
         
         </details><br>

         {pullRequestOverviewCommentResponse.OverviewCommentForHumanReviewers}

         ## Summary for LLM Reviewers
         <details>
         <summary>Click to expand</summary>
         
         {pullRequestOverviewCommentResponse.OverviewForLLMReviewers}
         
         </details>

         ## Files Code Reviews
         {ToCodeReviewComments(pullRequestFilesCodeReviewResponse)}
         """;

    private static string ToCodeReviewComments(PullRequestFilesCodeReviewResponse pullRequestFilesCodeReviewResponse)
    {
        var relevantFiles = pullRequestFilesCodeReviewResponse.ClassifiedFilesCodeReview
            .Where(c => !c.FileCodeReview.IsEmpty).ToArray();
        return relevantFiles.Length == 0
            ? "No comments"
            : string.Join("\r\n\r\n", relevantFiles.Select(ToClassifiedFileCodeReviewMarkdown));
    }

    private static string ToClassifiedFileMarkdown(ClassifiedFile classifiedFile) =>
        $"""
         ### {classifiedFile.File.FileName}
         **File Change Type**: {classifiedFile.FileChangeType}<br>
         **Classification**: {classifiedFile.DiffClassificationResponse.DiffChangeType}<br>
         **Summary**: {classifiedFile.DiffClassificationResponse.DiffSummary}<br>
         **Diff**:<br>
         <details>
         <summary>Click to expand</summary>
         
         ```diff
         {classifiedFile.Diff}
         ```
         
         </details>
         """;

    private static string ToClassifiedFileCodeReviewMarkdown(ClassifiedFileCodeReview classifiedFileCodeReview) =>
        $"""
         ### {classifiedFileCodeReview.ClassifiedFile.File.FileName}<br>
         **File Change Type**: {classifiedFileCodeReview.ClassifiedFile.FileChangeType}<br>
         **Classification**: {classifiedFileCodeReview.ClassifiedFile.DiffClassificationResponse.DiffChangeType}<br>
         **Summary**: {classifiedFileCodeReview.ClassifiedFile.DiffClassificationResponse.DiffSummary}<br>
         **Diff**:<br>
         <details>
         <summary>Click to expand</summary>
         
         ```diff
         {classifiedFileCodeReview.ClassifiedFile.Diff}
         ```
         
         </details>
         
         **File Comments**:<br>
         {classifiedFileCodeReview.FileCodeReview.FileComments}
         **Code Comments**:<br>
         {string.Join("\r\n\r\n", classifiedFileCodeReview.FileCodeReview.CodeComments.Select((c, index) => $"**Comment #{index + 1}**\r\n- **Line**: {c.Line}\r\n- **Comment**: {c.Comment}\r\n- **Context**:\r\n```diff\r\n{c.Context}\r\n```\r\n- **Fix Suggestion**: {c.FixSuggestion}"))}
         """;
}