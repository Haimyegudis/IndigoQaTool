using System.Text;
using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tools.ExternalDevServices.AI.Orchestration.Utils;
using Tools.ExternalDevServices.Integrations.GitHub;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V1;

public partial class GitHubPullRequestCodeReviewAgent
{
    private readonly IChatClient _chatClient;

    public GitHubPullRequestCodeReviewAgent(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<PullRequestFilesCodeReviewResponse> GeneratePullRequestCodeReviewAsync(PullRequestOverviewCommentResponse pullRequestOverviewCommentResponse, 
        DiagnosticsHelper? diagnostics)
    {
        var coreChangesFilesCodeReviews =
            await GeneratePullRequestCodeReviewAsync(pullRequestOverviewCommentResponse, 
                FileCodeReviewSystemPrompt,
                filter: cf => cf.DiffClassificationResponse.DiffChangeType is DiffChangeType.CoreChange,
                diagnostics);
        var behavioralAdaptFilesCodeReviews =
            await GeneratePullRequestCodeReviewAsync(pullRequestOverviewCommentResponse,
                FileCodeReviewSystemPrompt,
                filter: cf => cf.DiffClassificationResponse.DiffChangeType is DiffChangeType.BehavioralAdapt,
                diagnostics);

        return new PullRequestFilesCodeReviewResponse(
            pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse,
            coreChangesFilesCodeReviews.Concat(behavioralAdaptFilesCodeReviews).ToArray());
    }

    private async Task<IReadOnlyCollection<ClassifiedFileCodeReview>> GeneratePullRequestCodeReviewAsync(
        PullRequestOverviewCommentResponse pullRequestOverviewCommentResponse, 
        ChatMessage systemPrompt, 
        Func<ClassifiedFile, bool> filter, 
        DiagnosticsHelper? diagnostics)
    {
        var classifiedFiles =
            pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.ClassifiedFiles
                .Where(filter)
                .ToArray();

        var codeReviews = new List<ClassifiedFileCodeReview>(classifiedFiles.Length);
        foreach (var classifiedFile in classifiedFiles)
        {
            diagnostics?.AddInformation($"Generating code review for {classifiedFile.DiffClassificationResponse.DiffChangeType} file {classifiedFile.File.FileName}...");

            var diffWithLineMarkersSb = new StringBuilder();
            var linesMap = new Dictionary<int, string>();
            var maxAllowedLine = int.MinValue;
            var linesJsonArray = new JArray();
            foreach (var (resolvedLineType, lineIndex, line) in DiffLineResolver.ResolveLines(classifiedFile.Diff))
            {
                if (resolvedLineType is DiffLineResolver.ResolvedLineType.AdditionOrContext && lineIndex.HasValue)
                {
                    linesMap[lineIndex.Value] = line;
                    linesJsonArray.Add(line);
                    maxAllowedLine = Math.Max(maxAllowedLine, lineIndex.Value);
                }
                diffWithLineMarkersSb.AppendLine(
                    $"{(resolvedLineType is DiffLineResolver.ResolvedLineType.AdditionOrContext ? $"[L{lineIndex}] " : "")}{line}");
            }

            var diffWithLineMarkers = diffWithLineMarkersSb.ToString().Trim();
            ChatMessage[] chatMessages =
            [
                systemPrompt,
                new(ChatRole.User,
                    $$"""
                    **Pull Request Title**: {{pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.PullRequest.Title}}
                    **Pull Request Description**: {{pullRequestOverviewCommentResponse.PullRequestFilesClassificationResponse.PullRequest.StripDevopsCommentFromBody()}}
                    **Pull Request Summary**: {{pullRequestOverviewCommentResponse.OverviewForLLMReviewers}}
                    **File**: {{classifiedFile.File.FileName}}
                    
                    **ValidAddedLines**: {{linesJsonArray.ToString(Formatting.None)}}
                    
                    <BEGIN DIFF>
                    {{diffWithLineMarkers}}
                    <END DIFF>
                    
                    Reminder: Anchor only to ValidAddedLines; if none apply, return {"FileComments":"","CodeComments":[]}.
                    """)
            ];

            var codeReview = await GeneratePullRequestCodeReviewAsync(classifiedFile, diffWithLineMarkers, linesMap,
                maxAllowedLine, chatMessages, diagnostics);
            AddContextToCodeReview(codeReview, linesMap);
            
            diagnostics?.WriteContentToSeparateFile(
                $"FileCodeReview_{classifiedFile.File.FileName.Replace('/', '_').Replace('.', '_')}.md",
                $"""
                 **Code review generated for {classifiedFile.DiffClassificationResponse.DiffChangeType} file {classifiedFile.File.FileName}.**

                 **Diff with line markers**:
                 {diffWithLineMarkers}

                 **File comment**:
                 {codeReview.FileComments}

                 **Code comments**:
                 {string.Join("\r\n\r\n", codeReview.CodeComments.Select((c, index) => $"**Comment #{index + 1}\r\n- **Line**: {c.Line}\r\n- **Comment**: {c.Comment}\r\n- **Fix Suggestion**: {c.FixSuggestion}"))}
                 """);
            diagnostics?.AddInformation(
                !codeReview.IsEmpty
                    ? $"Generated code review for {classifiedFile.DiffClassificationResponse.DiffChangeType} file {classifiedFile.File.FileName}:\r\n**File Comment**: {codeReview.FileComments}\r\n**Code Comments**:\r\n{string.Join("\r\n\r\n", codeReview.CodeComments.Select(c => $"**Line**: {c.Line}\r\n**Comment**: {c.Comment}\r\n**Fix Suggestion**: {c.FixSuggestion}"))}"
                    : $"No comments for {classifiedFile.DiffClassificationResponse.DiffChangeType} file {classifiedFile.File.FileName}");
            if (!codeReview.IsEmpty) codeReviews.Add(new ClassifiedFileCodeReview(classifiedFile, codeReview));
        }

        return codeReviews;
    }

    private void AddContextToCodeReview(FileCodeReview codeReview, Dictionary<int, string> linesMap)
    {
        foreach (var codeComment in codeReview.CodeComments)
        {
            var contextSb = new StringBuilder($"[{codeComment.Line}]\t{linesMap[codeComment.Line]}\r\n");
            for (var i = 1; i <= 3; i++)
            {
                if (linesMap.TryGetValue(codeComment.Line - i, out var previousLine))
                {
                    contextSb.Insert(0, $"[{codeComment.Line - i}]\t{previousLine}\r\n");
                }
                else break;
            }
            for (var i = 1; i <= 3; i++)
            {
                if (linesMap.TryGetValue(codeComment.Line + i, out var nextLine))
                {
                    contextSb.AppendLine($"[{codeComment.Line + i}]\t{nextLine}");
                }
                else break;
            }

            codeComment.Context = contextSb.ToString().TrimEnd();
        }
    }

    private async Task<FileCodeReview> GeneratePullRequestCodeReviewAsync(ClassifiedFile classifiedFile, 
        string diffWithLineMarkers,
        IDictionary<int, string> linesMap, 
        int maxAllowedLine, 
        ChatMessage[] chatMessages, 
        DiagnosticsHelper? diagnostics)
    {
        string? lastResponse = null;
        string? lastJson = null;
        for (var i = 0; i < 3; i++)
        {
            try
            {
                lastResponse = "before call";
                lastResponse =
                    (await _chatClient.GetResponseAsync(chatMessages, ChatOptionsUtils.CreateGreedyChatOptions()))
                    .Text;

                var jsonStart = lastResponse.IndexOf('{');
                if (jsonStart == -1) throw new Exception("No JSON response from AI");
                var jsonEnd = lastResponse.LastIndexOf('}');
                if (jsonEnd == -1) throw new Exception("No JSON response from AI");
                if (jsonEnd <= jsonStart) throw new Exception("Invalid JSON response from AI");

                lastJson = lastResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);

                var fileCodeReview = JsonConvert.DeserializeObject<FileCodeReview>(lastJson) ??
                       throw new InvalidOperationException($"Failed to deserialize {nameof(FileCodeReview)} from response {lastResponse}, json: {lastJson}");
                if (fileCodeReview.CodeComments.Length <= 0 ||
                    classifiedFile.FileChangeType is not (FileChangeType.Modified or FileChangeType.Added))
                    return fileCodeReview;
                //Set the line numbers for the code comments
                var invalidLines = fileCodeReview.CodeComments
                    .Where(c => !linesMap.ContainsKey(c.Line) || c.Line <= 0 || c.Line > maxAllowedLine).ToArray();
                if (invalidLines.Length <= 0) return fileCodeReview;
                
                var invalidLinesString = string.Join("\r\n",
                    invalidLines.Select(c =>
                        $"Line: {c.Line}\r\nComment: {c.Comment}\r\nFix Suggestion: {c.FixSuggestion}"));
                var errorMessage = $"Invalid lines in review response: {invalidLinesString}";
                diagnostics?.AddError($"{errorMessage}\r\nInput Diff:\r\n{diffWithLineMarkers}");
                throw new InvalidOperationException(errorMessage);
            }
            catch (Exception ex)
            {
                diagnostics?.AddError($"Failed to generate code review, last response: {lastResponse}, last json: {lastJson}, retry: {i + 1}/3", ex);
            }
        }

        throw new InvalidOperationException("Failed to generate code review after 3 retries");
    }
}