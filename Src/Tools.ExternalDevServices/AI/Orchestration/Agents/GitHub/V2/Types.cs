using Newtonsoft.Json.Converters;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Tools.ExternalDevServices.Integrations.GitHub;

namespace Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V2;

[Flags]
public enum DiffClassificationType
{
    [Description("No classification applied. Used only as a placeholder.")]
    None = 0,

    [Description($"""
                  APPLIES IF ALL RULES ARE MET:
                  
                  COMPONENT TYPE CHECK:
                  1. The file being changed is a **BEHAVIORAL** component (e.g., Service, Controller, Manager, Provider). 
                  2. It is NOT a passive data container (DTO, Entity, Model).

                  RULESET:
                  1. The diff modifies, adds, or removes **METHOD SIGNATURES** or **CONSTRUCTORS**.
                  2. The modification will NECESSARILY change how external callers invoke the code (compilation break or binary incompatibility).
                  3. Examples: Changing method names, parameter types/counts, return types, or access modifiers (public/private).
                  4. This does **NOT** apply to property changes in DTOs/Models (see {nameof(DataStructure)}).
                  5. Changes to mock setups or test files should NOT be classified as {nameof(ApiChange)}.
                  """)]
    ApiChange = 1 << 0,

    [Description($"""
                  APPLIES IF ALL RULES ARE MET:
                  
                  LOGIC & FLOW:
                  1. The diff modifies the **CODE PATH** or **DECISION TREES** (if/else, loops, calculations) within a method.
                  2. The output value or side effect of a method changes, but the *signature* remains the same.
                  
                  VS DATA STRUCTURE:
                  1. This applies to **RUNTIME VALUES** (e.g., changing a timeout from 5s to 10s, changing a string format).
                  2. It does NOT apply to Schema/Type definitions. If a field changes from `int` to `double`, that is {nameof(DataStructure)} or {nameof(ApiChange)}, not Behavior.

                  ADAPTATION:
                  1. The change is strictly updating a call-site to match an external dependency change (e.g., adding a new required parameter to a function call).
                  """)]
    BehaviorChange = 1 << 1,

    [Description($"""
                  APPLIES IF ALL RULES ARE MET:

                  COMPONENT TYPE CHECK:
                  1. The file being changed is a **PASSIVE DATA CONTAINER** (e.g., DTO, Database Entity, ViewModel, Configuration Class).
                  2. The file contains primarily properties/fields and NO business logic methods.

                  RULESET:
                  1. The diff changes the **SCHEMA** or **SHAPE** of the data.
                  2. Examples: Adding/removing properties, changing property types (int to string), modifying serialization attributes (JsonProperty).
                  3. The change impacts how the object is Serialized, Deserialized, or stored in a database.
                  4. NOTE: If a class has *both* complex logic and data properties, default to {nameof(ApiChange)} or {nameof(BehaviorChange)} rather than {nameof(DataStructure)}.
                  """)]
    DataStructure = 1 << 2,

    [Description("""
                  APPLIES IF ALL RULES ARE MET:
                  VALIDATION:
                  1. The diff adds or modifies guard clauses, assertions, or precondition checks.
                  2. The code specifically validates input parameters or data integrity before processing.

                  AND/OR ERROR HANDLING:
                  1. The diff modifies or adds 'catch' blocks, retry policies, or fallback logic.
                  2. The code is specifically related to recovering from, ignoring, or logging exceptions/failures.
                  """)]
    ValidationOrErrorHandling = 1 << 3,

    [Description("""
                  APPLIES IF ALL RULES ARE MET:
                  1. The diff modifies the contents of a config files (JSON/XML/YAML/PROPS/CONFIG).
                  """)]
    Configuration = 1 << 4,

    [Description("""
                  APPLIES IF ALL RULES ARE MET:
                  REFACORING:
                  1. The diff moves, renames, extracts, or reorganizes existing code without altering logic.
                  
                  OR CLEANUP:
                  1. The diff removes code, variables, usings, or comments with no logic replacement.
                  
                  OR COSMETIC:
                  1. Whitespace, indentation, or formatting changes only.
                  """)]
    MinorChange = 1 << 5,

    [Description($"""
                  APPLIES IF ALL RULES ARE MET:
                  1. The file being modified is a Test file, Mock, Spec, or Test Data builder.
                  2. The code changes logic, assertions, mock setups, method inputs or method calls within the test context.
                  3. This classification SUPERSEDES all classifications for all test files.
                  4. NO ROOM FOR JUDGMENT - ALL TEST FILE MUST BE CLASSIFIED AS {nameof(TestChange)}.
                  """)]
    TestChange = 1 << 6,

    [Description($"""
                  APPLIES IF ALL RULES ARE MET:
                  1. The file being modified is a visual style definition file (e.g. css, xaml, scss, less, sass, uss).
                  2. This classification SUPERSEDES all other classifications for style files.
                  3. NO ROOM FOR JUDGMENT - ALL STYLE FILES MUST BE CLASSIFIED AS {nameof(Style)}.
                  """)]
    Style = 1 << 7,

    [Description($"""
                  APPLIES IF ALL RULES ARE MET:
                  1. The file being modified is a non-code resource asset.
                  2. Examples include: Text files (.txt, .md, .rst), Images (.png, .jpg, .ico, .svg), Binaries, or Localization (.resx).
                  3. The file is NOT a configuration file (see {nameof(Configuration)}) and NOT a source code file.
                  4. This classification SUPERSEDES all other classifications for resource files.
                  5. NO ROOM FOR JUDGMENT - ALL RESOURCE FILES MUST BE CLASSIFIED AS {nameof(Resource)}.
                  """)]
    Resource = 1 << 8,

    [Description("Placeholder for max value.")]
    MaxValue = int.MaxValue
}

public class DiffClassification
{
    /// <summary>
    /// Classification of the changes made in the diff
    /// </summary>
    [Description("Classification of the changes made in the diff")]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public DiffClassificationType DiffClassificationType { get; set; }

    /// <summary>
    /// Justification for the assigned classification type
    /// </summary>
    [Description("Justification for the assigned classification type")]
    public string ClassificationJustifications { get; set; } = "";

    public DiffClassificationType WithoutClassificationsAbove(DiffClassificationType maxClassificationType)
    {
        if (maxClassificationType == DiffClassificationType.MaxValue) return DiffClassificationType;

        var values = Enum.GetValues<DiffClassificationType>()
            .OrderBy(x => x);
        var classificationsToRemove =
            values.Where(value => value > maxClassificationType && value < DiffClassificationType.MaxValue);

        var result = DiffClassificationType;
        foreach (var value in classificationsToRemove)
        {
            result &= ~value;
        }
        return result;
    }

    public DiffClassificationType GetMinDiffClassificationType()
    {
        var values = Enum.GetValues<DiffClassificationType>()
            .Where(classification => classification > DiffClassificationType.None).OrderBy(x => x);
        foreach (var value in values)
        {
            if ((DiffClassificationType & value) == value)
                return value;
        }

        return DiffClassificationType;
    }
}

public class DiffClassificationResponse
{
    [Description("Classifications of the changes made in the diff")]
    public DiffClassification[] DiffClassifications { get; set; } = [];

    [Description("Summary of the changes made in the diff")]
    public string DiffSummary { get; set; } = "";
}

public class FileDiffsClassification
{
    [System.Text.Json.Serialization.JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public FileDiffs FileDiffs { get; set; } = null!;

    /// <summary>
    /// The name of the file that was changed
    /// </summary>
    [Description("The name of the file that was changed")]
    public string FileName => FileDiffs.FileName;

    /// <summary>
    /// Summary of the changes made in the file diffs
    /// </summary>
    [Description("Summary of the changes made in the file diffs")]
    public string DiffSummary { get; set; } = "";

    /// <summary>
    /// Classifications of the changes made in the file diffs
    /// </summary>
    [Description("Classifications of the changes made in the file diffs")]
    public DiffClassification[] DiffClassifications { get; set; } = [];

    public static FileDiffsClassification FromResponse(FileDiffs fileDiffs,
        DiffClassificationResponse diffClassificationResponse) =>
        new()
        {
            FileDiffs = fileDiffs,
            DiffSummary = diffClassificationResponse.DiffSummary,
            DiffClassifications = diffClassificationResponse.DiffClassifications
                .OrderBy(x => x.GetMinDiffClassificationType()).ToArray()
        };
}

public class PullRequestFilesClassifications
{
    public PullRequestDetails PullRequest { get; set; } = null!;
    
    public string[] DeletedFiles { get; set; } = [];

    public FileDiffsClassification[] AddedFiles { get; set; } = [];

    public FileDiffsClassification[] ModifiedFiles { get; set; } = [];

    [Newtonsoft.Json.JsonIgnore]
    public DiffClassificationType MinDiffClassificationType =>
        AddedFiles.Length == 0 && ModifiedFiles.Length == 0
            ? DiffClassificationType.None
            : AddedFiles.Concat(ModifiedFiles).Min(file =>
                file.DiffClassifications.Min(classification => classification.GetMinDiffClassificationType()));

    public int CountFilesByClassification(DiffClassificationType maxClassificationType)
    {
        return AddedFiles
                   .Count(file => file.DiffClassifications.Any(classification =>
                       classification.WithoutClassificationsAbove(maxClassificationType) >
                       DiffClassificationType.None)) +
               ModifiedFiles
                   .Count(file => file.DiffClassifications.Any(classification =>
                       classification.WithoutClassificationsAbove(maxClassificationType) >
                       DiffClassificationType.None));
    }

    public IReadOnlyCollection<FileDiffsClassification> GetFilesByClassification(DiffClassificationType maxClassificationType)
    {
        return AddedFiles
            .Where(file => file.DiffClassifications.Any(classification =>
                classification.WithoutClassificationsAbove(maxClassificationType) >
                DiffClassificationType.None)).Concat(
                ModifiedFiles
                    .Where(file => file.DiffClassifications.Any(classification =>
                        classification.WithoutClassificationsAbove(maxClassificationType) >
                        DiffClassificationType.None)))
            .OrderBy(file =>
                file.DiffClassifications.Min(classification => classification.GetMinDiffClassificationType()))
            .ToArray();
    }

    public string ToJson() => ToJson(Enum.GetValues<DiffClassificationType>().Max());

    public string ToJson(DiffClassificationType maxClassificationType) =>
        JsonConvert.SerializeObject(new
        {
            PullRequest = $"{PullRequest.Title} #{PullRequest.Number}",
            Body = PullRequest.Body ?? "",
            AddedFiles = AddedFiles
                .Where(file => file.DiffClassifications.Any(classification =>
                    classification.WithoutClassificationsAbove(maxClassificationType) > DiffClassificationType.None))
                .OrderBy(file =>
                    file.DiffClassifications.Min(classification => classification.GetMinDiffClassificationType()))
                .ToArray(),
            ModifiedFiles = ModifiedFiles
                .Where(file => file.DiffClassifications.Any(classification =>
                    classification.WithoutClassificationsAbove(maxClassificationType) > DiffClassificationType.None))
                .OrderBy(file =>
                    file.DiffClassifications.Min(classification => classification.GetMinDiffClassificationType()))
                .ToArray(),
            DeletedFiles
        }, Formatting.Indented);
}

public class PullRequestOverviewResponse
{
    /// <summary>
    /// A brief 1-4 sentences high level summary of the purpose and essence of the pull request.
    /// </summary>
    [Description("A brief 1-4 sentences high level summary of the purpose and essence of the pull request. The high level summary should aim to capture the task or issue the pull request aims to resolve, without detailing how it does it.")]
    public string Summary { get; set; } = "";

    /// <summary>
    /// Detailed descriptions of the distinct/independent core/important changes and additions to the pull request that lead to achieving its purpose.
    /// Should not include minor changes or changes that are not core to the pull request's purpose or changes and additions to tests.
    /// If there are no core/important changes or additions, this should be an empty array.
    /// </summary>
    [Description("""
                 Detailed descriptions of the distinct/independent core/important changes and additions to the pull request that lead to achieving its purpose.
                 Should not include minor changes or changes that are not core to the pull request's purpose or changes and additions to tests.
                 If there are no core/important changes or additions, this should be an empty array.
                 """)]
    public string[] KeyChangesAndAdditions { get; set; } = [];
}

public class PullRequestOverview
{
    public PullRequestOverviewResponse PullRequestOverviewResponse { get; set; } = null!;

    public PullRequestFilesClassifications FilesClassifications { get; set; } = null!;
}

public class CodeReviewComment
{
    /// <summary>
    /// Textual only explanation of why the commented code is incorrect or not good enough, and how it should be changed.
    /// Should not contain citations from the diff; Should use backticks for identifiers, e.g. `methodName` or `SomeType`.
    /// </summary>
    [Description("""
                 Textual only explanation of why the commented code is incorrect or not good enough, why the pull request cannot be merged before fixing the issue, and how it should be changed.
                 Should not contain citations from the diff; Should use backticks for identifiers, e.g. `methodName` or `SomeType`.
                 """)]
    public string ReviewComment { get; set; } = "";

    /// <summary>
    /// Minimal code snippet from the original unified diff that the comment is about, with ±1–2 lines of context; Markdown code block format with language if obvious; no surrounding text
    /// </summary>
    [Description("Minimal code snippet from the original unified diff that the comment is about, with ±1–2 lines of context; Markdown code block format with language if obvious (e.g. ```csharp...```); no surrounding text")]
    public string CommentedCodeMarkdownBlock { get; set; } = "";

    /// <summary>
    /// Minimal corrected code with ±1–2 lines of context; Markdown code block format with language if obvious; no surrounding text
    /// </summary>
    [Description("Minimal corrected code with ±1–2 lines of context; Markdown code block format with language if obvious (e.g. ```csharp...```); no surrounding text")]
    public string FixSuggestionMarkdownBlock { get; set; } = "";
}

public enum RiskType
{
    /// <summary>
    /// The visible changes are likely to introduce serious negative impact (such as data corruption, crashes, clearly incorrect behavior, or breaking critical flows) if merged without further fixes
    /// </summary>
    [Description("The visible changes are likely to introduce serious negative impact (such as data corruption, crashes, clearly incorrect behavior, or breaking critical flows) if merged without further fixes")]
    High,

    /// <summary>
    /// The visible changes are somewhat complex, fragile, or easy to misuse and might lead to incorrect behavior or maintenance problems in some scenarios, but no obviously catastrophic failure is visible
    /// </summary>
    [Description("The visible changes are somewhat complex, fragile, or easy to misuse and might lead to incorrect behavior or maintenance problems in some scenarios, but no obviously catastrophic failure is visible")]
    Medium,

    /// <summary>
    /// The visible changes are straightforward and clearly aligned with the PR’s goal; nothing in this file stands out as clearly dangerous, although bugs are still possible
    /// </summary>
    [Description("The visible changes are straightforward and clearly aligned with the PR’s goal; nothing in this file stands out as clearly dangerous, although bugs are still possible")]
    Low,

    /// <summary>
    /// The visible changes are trivial or obviously correct; no risk of introducing bugs or breaking anything
    /// </summary>
    [Description("The visible changes are trivial or obviously correct; no risk of introducing bugs or breaking anything")]
    None
}

public class FileCodeReviewResponse
{
    /// <summary>
    /// A review of the file in the context of the pull request - the file’s role in the pull request and how its important changes/additions contribute to the pull request’s purpose
    /// </summary>
    [Description("A review of the file in the context of the pull request - the file’s role, in the system in general and in the pull request specifically, and how its important changes/additions contribute to the pull request’s purpose")]
    public string FileReview { get; set; } = "";

    /// <summary>
    /// Risk estimation for the file
    /// </summary>
    [Description("Risk estimation for the file")]
    public RiskType RiskType { get; set; } = RiskType.None;

    /// <summary>
    /// Justification for the risk estimation
    /// </summary>
    [Description("Justification for the risk estimation")]
    public string RiskJustification { get; set; } = "";

    /// <summary>
    /// List of areas to focus on when reviewing the file
    /// </summary>
    [Description("List of areas to focus on when reviewing the file")]
    public string[] FocusOn { get; set; } = [];
}

public class FileCodeReview
{
    public FileDiffsClassification File { get; set; } = null!;
    public FileCodeReviewResponse FileCodeReviewResponse { get; set; } = null!;

    public bool HasBlockingMergeIssues => FileCodeReviewResponse.RiskType is RiskType.High;

    public string ToCodeReviewMarkdown()
    {
        var markdown =
            $"""
             ## {ToEmojiPrefix(FileCodeReviewResponse.RiskType)}{File.FileName} ({File.FileDiffs.ChangeType})
             **Classification:** {string.Join(", ", File.DiffClassifications.OrderBy(dc => dc.GetMinDiffClassificationType()).Select(dc => $"{dc.GetMinDiffClassificationType().ToString()} ({(int)dc.GetMinDiffClassificationType()})").Distinct())}
             {FileCodeReviewResponse.FileReview}
             """;

        if (FileCodeReviewResponse.RiskType is RiskType.High or RiskType.Medium)
        {
            markdown += $"\r\n\r\n### Needs Attention ({FileCodeReviewResponse.RiskType}):\r\n{FileCodeReviewResponse.RiskJustification}";
        }

        markdown +=
            $"\r\n\r\n### Focus On:\r\n{string.Join("\r\n", FileCodeReviewResponse.FocusOn.Select(f => $"- {f}"))}";

        return markdown;
    }

    private static string ToEmojiPrefix(RiskType riskType) =>
        riskType switch
        {
            RiskType.High => "📌 ",
            RiskType.Medium => "📍 ",
            _ => ""
        };
}

public class GitHubAddOrUpdatePullRequestOverviewCommentResponse
{
    public int? CommentId { get; set; }
    public string? CommentHtmlUrl { get; set; }
    public string Comment { get; set; } = "";
    public PullRequestOverview PullRequestOverview { get; set; } = null!;
    public IReadOnlyCollection<FileCodeReview> FileCodeReviews { get; set; } = [];
    public string ReviewPlan { get; set; } = "";
}