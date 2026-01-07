using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Tools.ExternalDevServices.Integrations.GitHub;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V1;

public class PullRequestFilesClassifierAgent
{
    private static readonly ChatMessage SystemPrompt = new(ChatRole.Assistant,
        """
        # SYSTEM PROMPT: Deterministic Diff Classifier
        
        You are a deterministic, high-precision classifier for a single file’s Git unified diff. Your task is to classify the diff into exactly one value of the enum `DiffChangeType` and produce a summary that describes both **what changed** and the **purpose of the change**. The summary must always **begin with a very short description of the file type/role** (e.g., "test file", "service", "utils", "config", "data structure").
        
        ## OUTPUT (STRICT)
        - Return **only one** JSON object, minified, with this exact schema:
          - Key: `DiffChangeType` (case-sensitive)
            - Value: one of `CoreChange`, `BehavioralAdapt`, `StraightforwardAdapt`, `DesignChange`, `ContentChange`, `Cosmetic`
          - Key: `DiffSummary` (case-sensitive)
            - Value: 2–5 short sentences. Start with the file type/role, then describe what changed, then state the purpose or intended effect if it can be directly inferred from the diff.
        - Example (do not include this text): {"DiffChangeType":"CoreChange","DiffSummary":"Service file. Added 'Source' property to JobEntity and updated session creation to compute and persist it. Purpose: propagate job source information across the printing pipeline."}
        - JSON must be minified, UTF-8, double-quoted keys/values, no trailing commas.
        - No prose, no explanations, no code fences, no extra keys.
        
        ## PRIORITY (single value)
        If multiple categories seem to apply, choose the **highest** by this order:
        1. CoreChange
        2. BehavioralAdapt
        3. StraightforwardAdapt
        4. DesignChange
        5. ContentChange
        6. Cosmetic
        
        ## DEFINITIONS (diff-only; no repo-wide context)
        - CoreChange: Introduces/changes logic, control flow, data shape, signatures, or visibility that stands on its own. Includes new helpers, new parameters or out vars, new conditionals/branches, or renaming a method with changed behavior/side effects. If a file both introduces new behavior and adapts to other changes, classify as CoreChange.
        - BehavioralAdapt: Adapts to a prior core change and modifies observable behavior only to comply with it (e.g., computing a trivial mapping, adding a check for a new field). Excludes new helpers, control-flow branches, or side effects (those are CoreChange).
        - StraightforwardAdapt: Mechanical propagation with no behavior change (e.g., thread a new parameter unchanged, update call sites or signatures, reorder args with identical semantics).
        - DesignChange: Purely visual changes (CSS, XAML, layout, styles, colors, static markup). Must not involve bindings, interpolations, or logic-driven changes; otherwise classify as CoreChange or an Adapt.
        - ContentChange: Human-readable content changes (e.g., Markdown, README, docs text).
        - Cosmetic: No behavioral impact and not design/content (e.g., whitespace, indentation, inline comments, TODOs, pure renames, argument reordering with identical meaning).
        
        ## DIFFSUMMARY RULES
        - Write 2–5 short sentences.
        - Always begin with the file type/role (e.g., "controller", "service", "test file", "config", "dto", "utility", "style", "readme").
        - Then describe the main changes in concrete terms, mentioning identifiers (class, method, property, config key) where relevant.
        - Conclude with a short statement of the **purpose** of the change if it can be directly inferred (e.g., "Purpose: support cancellation", "Purpose: ensure null safety", "Purpose: adjust button styling", "Purpose: update documentation").
        - Avoid generic text like “updated logic” or “refactored code.”
        - Do not invent motivations not visible in the diff; only state a purpose if it is reasonably clear from the changes.
        
        ## HEURISTICS (apply in order)
        1. If the diff introduces new helpers, new controlling parameters/out vars, or new conditional branches/side effects, classify as CoreChange.
        2. If the diff adapts to another change and modifies behavior (computes/derives new values, alters conditions/guards/returns) without introducing new helpers or side effects, classify as BehavioralAdapt.
        3. If the diff adapts mechanically (pass-through arg, signature conformity, rename ripple, argument reordering with same meaning), classify as StraightforwardAdapt.
        4. If the diff affects purely visual aspects only (CSS, XAML, styles, layout, colors, static markup) and no bindings or interpolations are changed, classify as DesignChange.
        5. If the diff affects human-readable content (Markdown, README, docs), classify as ContentChange.
        6. If only formatting, indentation, inline comments, TODOs, or trivial renames with no semantic effect, classify as Cosmetic.
        
        ## ASSUMPTIONS
        - Always select the “best fit” category based on available evidence, even if the diff is ambiguous.
        - Let the AI determine file type/role from the diff.
        - If the diff contains no meaningful changes (e.g., only whitespace or formatting), classify as Cosmetic.
        - Assume the project compiles and tests pass.
        - Use only the provided diff; do not invent context outside the diff.
        - Do not restate the input or include file paths.
        
        ## INPUT
        You will receive the pull request metadata and the unified diff (additions/deletions; minimal context).
        
        ## FINAL INSTRUCTION
        - Output exactly one minified JSON object with the `DiffChangeType` and `DiffSummary` keys, and nothing else.
        """);

    private readonly GitHubRestApiClient _gitHubRestApiClient;
    private readonly IChatClient _chatClient;
    private readonly DiagnosticsHelper? _diagnosticsHelper;

    public PullRequestFilesClassifierAgent(GitHubRestApiClient gitHubRestApiClient, 
        IChatClient chatClient, 
        DiagnosticsHelper? diagnosticsHelper = null)
    {
        _gitHubRestApiClient = gitHubRestApiClient;
        _chatClient = chatClient;
        _diagnosticsHelper = diagnosticsHelper;
    }

    public async Task<PullRequestFilesClassificationResponse> ClassifyPullRequestFilesAsync(string repo, int pullRequestNumber, int? maxContextSizeInTokens)
    {
        var pullRequestDetails = await _gitHubRestApiClient.GetPullRequestDetailsAsync(repo, pullRequestNumber);
        return await ClassifyPullRequestFilesAsync(pullRequestDetails, maxContextSizeInTokens);
    }

    public async Task<PullRequestFilesClassificationResponse> ClassifyPullRequestFilesAsync(PullRequestDetails pullRequestDetails, int? maxContextSizeInTokens)
    {
        var commitFiles = pullRequestDetails.PullRequestFiles;
        
        _diagnosticsHelper?.AddInformation($"Getting diffs for {commitFiles.Length} files from GitHub Rest Api...");
        var diffs = new ConcurrentBag<(string fileName, FileDiffs fileDiffs)>();
        await Parallel.ForEachAsync(commitFiles, async (commitFile, _) =>
        {
            var fileDiffs = await _gitHubRestApiClient.GetFileDiffsAsync(pullRequestDetails, commitFile.FileName,
                addCsharpContext: true, contextSizeBefore: 3, contextSizeAfter: 3);
            diffs.Add((commitFile.FileName, fileDiffs));
        });
        var diffsMap = diffs.ToDictionary(diffTuple => diffTuple.fileName, diffTuple => diffTuple.fileDiffs);

        _diagnosticsHelper?.AddInformation($"Classifying {commitFiles.Length} files diffs using AI...");

        var sw = Stopwatch.StartNew();
        var classifiedFiles = new List<ClassifiedFile>();
        for (var index = 0; index < commitFiles.Length; index++)
        {
            var commitFile = commitFiles[index];
            _diagnosticsHelper?.AddInformation($"Processing file {commitFile.FileName} ({index + 1}/{commitFiles.Length})...");
            var fileDiffs = diffsMap[commitFile.FileName];

            switch (fileDiffs.ChangeType)
            {
               case FileChangeType.Modified or FileChangeType.Added:
                    var classifiedFile = await ClassifyFileDiffAsync(pullRequestDetails, commitFile, fileDiffs);
                    classifiedFiles.Add(classifiedFile);
                    _diagnosticsHelper?.AddInformation(
                        $"Classified {fileDiffs.Diffs.Count} diffs for file {commitFile.FileName}\r\nRelevant: {classifiedFile.IsRelevantForReview}\r\n{nameof(classifiedFile.DiffClassificationResponse.DiffChangeType)}:\r\n{classifiedFile.DiffClassificationResponse.DiffChangeType}\r\n{nameof(classifiedFile.DiffClassificationResponse.DiffSummary)}:\r\n{classifiedFile.DiffClassificationResponse.DiffSummary}");
                    break;
                case FileChangeType.Deleted:
                    _diagnosticsHelper?.AddInformation($"File {fileDiffs.FileName} was deleted");
                    classifiedFiles.Add(new ClassifiedFile(commitFile, FileChangeType.Deleted, Diff: "",
                        new DiffClassificationResponse
                            { DiffChangeType = DiffChangeType.Deletion, DiffSummary = "File was deleted" }));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fileDiffs.ChangeType), fileDiffs.ChangeType,
                        $"Unsupported {nameof(FileChangeType)}");
            }
        }
        sw.Stop();
        _diagnosticsHelper?.AddInformation($"Classified {commitFiles.Length} files diffs using AI, duration: {sw.Elapsed}");
        
        return new PullRequestFilesClassificationResponse(pullRequestDetails, classifiedFiles);
    }

    private async Task<ClassifiedFile> ClassifyFileDiffAsync(PullRequestDetails pullRequestDetails, CommitFile commitFile, FileDiffs fileDiffs)
    {
        var combinedDiffs = string.Join("\r\n\r\n", fileDiffs.Diffs);
        var prDescription = pullRequestDetails.StripDevopsCommentFromBody();
        var prMetadata =
            $"**Pull Request Title**: {pullRequestDetails.Title}\r\n{(string.IsNullOrWhiteSpace(prDescription) ? "" : $"\r\n**Pull Request Description**: {prDescription}")}";
        var fileMetadata =
            $"**File**: {fileDiffs.FileName} ({(fileDiffs.ChangeType is FileChangeType.Added ? "File was added to the pull request and should be reviewed as a new file" : "File was modified in the pull request and review should focus on changes to the file")})";
        var diff = $"{prMetadata}\r\n{fileMetadata}\r\n---\r\n**Unified Diff**:\r\n{combinedDiffs}";
        var userMessage = new ChatMessage(ChatRole.User, diff);

        /*var used = chatHistory.Sum(cm => EstTokensByBytes(cm.Text));
        if (!TryAppend(used, 30_000, userMessage.Text))
        {
            chatHistory.RemoveRange(1, chatHistory.Count - 1); //Keep only the system prompt
        }*/
        ChatMessage[] chatHistory = [SystemPrompt, userMessage];

        for (var i = 0; i < 3; i++) // Retry up to 3 times
        {
            var response = (await _chatClient.GetResponseAsync(chatHistory,
                new ChatOptions { TopP = 0, Temperature = 0 })).Text;
            string? json = null;
            try
            {
                var jsonStart = response.IndexOf('{');
                if (jsonStart == -1) throw new Exception("No JSON response from AI");
                var jsonEnd = response.LastIndexOf('}');
                if (jsonEnd == -1) throw new Exception("No JSON response from AI");
                if (jsonEnd <= jsonStart) throw new Exception("Invalid JSON response from AI");

                json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var diffClassificationResponse =
                    JsonConvert.DeserializeObject<DiffClassificationResponse>(json) ??
                    throw new InvalidOperationException(
                        $"Error deserializing response from AI, response: {response}, json: {json}, Retry: {i + 1}");

                return new ClassifiedFile(commitFile, fileDiffs.ChangeType, combinedDiffs, diffClassificationResponse);
            }
            catch
            {
                _diagnosticsHelper?.AddError($"Error deserializing response from AI, response: {response}, json: {json}, Retry: {i + 1}");
            }
        }

        throw new InvalidOperationException("Error deserializing response from AI after max retires");
    }

    /*private static int EstTokensByBytes(string s, double safety = 1.3)
    {
        var bytes = System.Text.Encoding.UTF8.GetByteCount(s);
        return (int)Math.Ceiling((bytes / 3.0) * safety);
    }

    private static bool TryAppend(int used, int budgetIn, string msg)
    {
        var need = EstTokensByBytes(msg);
        return used + need <= budgetIn;
    }*/
}