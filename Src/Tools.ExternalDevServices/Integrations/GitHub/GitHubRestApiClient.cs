using DiffPlex.DiffBuilder;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Immutable;
using System.Net.Http.Headers;
using System.Text;
using System.Web;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.Integrations.GitHub;

public class GitHubRestApiClient : IDisposable
{
    private readonly string _personalAccessToken;
    private readonly string _apiBaseUrl;
    private readonly string _owner;
    private const int PerPage = 100;

    private readonly HttpClient _client;

    public GitHubRestApiClient(string personalAccessToken, string apiBaseUrl, string owner)
    {
        _personalAccessToken = personalAccessToken;
        _apiBaseUrl = apiBaseUrl;
        _owner = owner;
        _client = new HttpClient();
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GitHubPRChecker", "1.0"));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", personalAccessToken);
    }

    public async Task<ApiUser> GetAuthenticatedUserAsync()
    {
        // Get the authenticated user's login
        var currentUserJson = await GetApiResponseJsonAsync("user");
        return JsonConvert.DeserializeObject<ApiUser>(currentUserJson) ??
                          throw new InvalidOperationException("Failed to get current user information");
    }
    
    public async Task<IReadOnlyCollection<(int number, string repo)>> GetPullRequestsWaitingForMyReviewAsync()
    {
        var currentUser = await GetAuthenticatedUserAsync();
        var currentUserLogin = currentUser.Login;

        var pullRequests = new List<(int pr, string repo)>();
        var page = 1;

        while (true)
        {
            // Query for open pull requests where the current user is a requested reviewer
            var query = $"is:open is:pr review-requested:{currentUserLogin}";
            var url = $"search/issues?q={Uri.EscapeDataString(query)}&page={page}&per_page={PerPage}";

            var json = await GetApiResponseJsonAsync(url);
            var prs = JsonConvert.DeserializeObject<ApiPullRequestsQueryResponse>(json) ??
                      throw new InvalidOperationException($"Failed to parse pull requests from {url}");

            if (prs.Items.Length == 0)
                break;

            pullRequests.AddRange(prs.Items.Select(pr => (pr.Number, pr.Repo)));

            if (prs.Items.Length < PerPage)
                break;

            page++;
        }

        return pullRequests.Distinct().ToArray();
    }

    public async Task RemoveMeFromPullRequestReviewers(string repo, int pullRequestNumber)
    {
        var currentUser = await GetAuthenticatedUserAsync();
        var currentUserLogin = currentUser.Login;

        await RemoveUserFromPullRequestReviewers(currentUserLogin, repo, pullRequestNumber);

    }

    public async Task RemoveUserFromPullRequestReviewers(string currentUserLogin, string repo, int pullRequestNumber)
    {
        using var stringContent = new StringContent($"{{\"reviewers\":[\"{currentUserLogin}\"]}}", Encoding.UTF8,
            "application/json");
        var request = new HttpRequestMessage(
            HttpMethod.Delete,
            GetAbsoluteUrl($"repos/{_owner}/{repo}/pulls/{pullRequestNumber}/requested_reviewers"))
        {
            Content = stringContent
        };

        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<PullRequestDetails> GetPullRequestDetailsAsync(string repo, int pullRequestNumber)
    {
        var prDetailsJson = await GetApiResponseJsonAsync($"repos/{_owner}/{repo}/pulls/{pullRequestNumber}");
        var apiPullRequestDetails = JsonConvert.DeserializeObject<ApiPullRequestDetails>(prDetailsJson) ??
                        throw new InvalidOperationException($"Failed to parse pull request details for pull request #{pullRequestNumber}");

        var prCommentsJson =
            await GetApiResponseJArrayJsonAsync($"repos/{_owner}/{repo}/issues/{pullRequestNumber}/comments");
        var prReviewCommentsJson =
            await GetApiResponseJArrayJsonAsync($"repos/{_owner}/{repo}/pulls/{pullRequestNumber}/comments");
        var prReviewsJson =
            await GetApiResponseJArrayJsonAsync($"repos/{_owner}/{repo}/pulls/{pullRequestNumber}/reviews");
        var prCommitsJson =
            await GetApiResponseJArrayJsonAsync($"repos/{_owner}/{repo}/pulls/{pullRequestNumber}/commits");
        var commitDetailsJsons = new List<string>();
        var commits = JArray.Parse(prCommitsJson);
        foreach (var commit in commits)
        {
            var commitDetailsJson = await GetApiResponseJsonAsync(commit["url"]?.Value<string>() ??
                                                                  throw new InvalidOperationException(
                                                                      $"Url not found in commit:\r\n{commit}"));
            commitDetailsJsons.Add(commitDetailsJson);
        }

        var apiCommitFilesJson =
            await GetApiResponseJArrayJsonAsync($"repos/{_owner}/{repo}/pulls/{pullRequestNumber}/files");

        var apiCommitFiles = JsonConvert.DeserializeObject<ApiCommitFile[]>(apiCommitFilesJson) ??
                             throw new InvalidOperationException(
                                 $"Failed to deserialize files from response:\r\n{apiCommitFilesJson}");

        var fromSha = apiPullRequestDetails.From.Sha; //head
        var toSha = apiPullRequestDetails.To.Sha; //base
        var branchesCompare =
            await _client.GetStringAsync(
                $"{_apiBaseUrl}/repos/{_owner}/{repo}/compare/{toSha}...{fromSha}");

        var pullRequestDetails = PullRequestDetails.FromApiPullRequestDetails(repo,
            apiPullRequestDetails,
            comments: DeserializeOrThrow<ApiPullRequestComment[]>(prCommentsJson, nameof(prCommentsJson)),
            reviewComments: DeserializeOrThrow<ApiPullRequestReviewComment[]>(prReviewCommentsJson,
                nameof(prReviewCommentsJson)),
            reviews: DeserializeOrThrow<ApiPullRequestReview[]>(prReviewsJson, nameof(prReviewsJson)),
            commits: commitDetailsJsons
                .Select(commitJson => DeserializeOrThrow<ApiCommitDetails>(commitJson, nameof(commitJson))).ToArray(),
            pullRequestFiles: apiCommitFiles,
            branchesCompare: branchesCompare
        );

        return pullRequestDetails;
    }

    public async Task<string> GetFileContentsFromBranchAsync(string repo, string branch, string filePath)
    {
        var url = $"{_apiBaseUrl}/repos/{_owner}/{repo}/contents/{filePath}?ref={branch}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _personalAccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3.raw"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("GitHubPRChecker", "1.0"));
        using var response = await _client.SendAsync(request);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // File does not exist in the specified branch
            return string.Empty;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> GetFileContentsByShaAsync(string repo, string filePath, string sha)
    {
        var url = $"{_apiBaseUrl}/repos/{_owner}/{repo}/contents/{filePath}?ref={sha}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _personalAccessToken);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("GitHubPRChecker", "1.0"));
        using var response = await _client.SendAsync(request);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // File does not exist in the specified branch
            return string.Empty;
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var b64 = JObject.Parse(json)["content"]?.Value<string>() ??
               throw new InvalidOperationException("Failed to get file contents");

        // GitHub inserts newlines every 60 chars in "content"
        b64 = b64.Replace("\n", "").Replace("\r", "");

        var bytes = Convert.FromBase64String(b64);
        return Encoding.UTF8.GetString(bytes);
    }

    public async Task<FileDiffs> GetFileDiffsAsync(string repo, int pullRequest, string file, bool addCsharpContext = false)
    {
        var pullRequestDetails = await GetPullRequestDetailsAsync(repo, pullRequest);
        return await GetFileDiffsAsync(pullRequestDetails, file, addCsharpContext);
    }

    public async Task<FileDiffs> GetFileDiffsAsync(PullRequestDetails pullRequestDetails, 
        string file,
        bool addCsharpContext = true,
        int contextSizeBefore = 3,
        int contextSizeAfter = 3,
        string addedLineMarker = "+", 
        string deletedLineMarker = "-", 
        string unchangedLineMarker = " ")
    {
        var fromSha = pullRequestDetails.Branches.From.Sha; //head
        var entry = pullRequestDetails.MergeInfo.MergeBaseFiles.FirstOrDefault(f =>
            string.Equals((string?)f["filename"], file, StringComparison.OrdinalIgnoreCase) ||
            string.Equals((string?)f["previous_filename"], file, StringComparison.OrdinalIgnoreCase));

        var isRenamed = string.Equals((string?)entry?["status"], "renamed", StringComparison.OrdinalIgnoreCase);

        // Use previous_filename for the merge-base (source) if renamed
        var basePath = isRenamed ? (string?)entry?["previous_filename"] ?? file : file;
        // Always use the current path for the head
        var headPath = isRenamed ? (string?)entry?["filename"] ?? file : file;

        var refFile = await GetFileContentsByShaAsync(repo: pullRequestDetails.Repo, filePath: basePath,
            sha: pullRequestDetails.MergeInfo.MergeBaseSha);
        var changesFile = await GetFileContentsByShaAsync(repo: pullRequestDetails.Repo, filePath: headPath, sha: fromSha);
        if (string.IsNullOrEmpty(refFile) && string.IsNullOrEmpty(changesFile))
        {
            //File does not exist in the given path, probably moved to a new path in a later commit
            return new FileDiffs(file, FileChangeType.Deleted, "", []);
        }
        var diffPanel = InlineDiffBuilder.Diff(refFile, changesFile);
        var fullDiff = diffPanel.ToSingleDiffBlock(addedLineMarker, deletedLineMarker, unchangedLineMarker).Diff;

        if (string.IsNullOrEmpty(refFile) && !string.IsNullOrEmpty(changesFile))
        {
            return new FileDiffs(file, FileChangeType.Added, changesFile, [fullDiff]);
        }

        if (!string.IsNullOrEmpty(refFile) && string.IsNullOrEmpty(changesFile))
        {
            return new FileDiffs(file, FileChangeType.Deleted, fullDiff, [fullDiff]);
        }

        IReadOnlyDictionary<int, string> declarationsMap;
        if (addCsharpContext && file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            declarationsMap = RoslynUtils.GetCSharpFileLineNumbersToDeclarationStringsMap(changesFile);
        }
        else declarationsMap = ImmutableDictionary<int, string>.Empty;

        var diffs = 
            diffPanel.ToDiffBlocks(declarationsMap, contextSizeBefore, contextSizeAfter, addedLineMarker, deletedLineMarker, unchangedLineMarker)
            .Where(block => block.IsModificationBlock)
            .Select(block => $"{block.DiffDescriptor}\r\n{block.Diff}")
            .ToArray();

        return new FileDiffs(file, FileChangeType.Modified, fullDiff, diffs);
    }

    public async Task<(int id, string url)> AddCommentToPullRequestAsync(string repo, int pullRequestNumber, string comment)
    {
        using var stringContent = new StringContent(JsonConvert.SerializeObject(new { body = comment }));
        using var response = await _client.PostAsync(
            $"{_apiBaseUrl}/repos/{_owner}/{repo}/issues/{pullRequestNumber}/comments",
            stringContent);
        var json = await response.Content.ReadAsStringAsync();
        var apiComment = JsonConvert.DeserializeObject<ApiPullRequestComment>(json) ??
                      throw new InvalidOperationException($"Failed to parse response from comment post: {json}");
        return (apiComment.Id, apiComment.HtmlUrl);
    }

    public async Task UpdateComment(string repo, int commentId, string comment)
    {
        using var stringContent = new StringContent(JsonConvert.SerializeObject(new { body = comment }));
        using var _ = await _client.PatchAsync(
            $"{_apiBaseUrl}/repos/{_owner}/{repo}/issues/comments/{commentId}",
            stringContent);
    }

    public async Task<(int id, string url)> AddReviewToPullRequestAsync(
        string repo,
        int pullRequestNumber,
        string comment)
    {
        var payload = new
        {
            body = comment,
            // This makes it behave like clicking "Comment" in the UI (not approve / request changes)
            // and submits the review immediately.
            // See: "Create a review for a pull request"
            @event = "COMMENT"
        };

        using var stringContent =
            new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync(
            $"{_apiBaseUrl}/repos/{_owner}/{repo}/pulls/{pullRequestNumber}/reviews",
            stringContent);

        var json = await response.Content.ReadAsStringAsync();

        var apiReview = JsonConvert.DeserializeObject<ApiPullRequestReview>(json)
                        ?? throw new InvalidOperationException($"Failed to parse response from review post: {json}");

        return (apiReview.Id, apiReview.HtmlUrl);
    }

    public async Task UpdateReviewAsync(
        string repo,
        int pullRequestNumber,
        int reviewId,
        string comment)
    {
        var payload = new { body = comment };

        using var stringContent =
            new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
        using var _ = await _client.PutAsync(
            $"{_apiBaseUrl}/repos/{_owner}/{repo}/pulls/{pullRequestNumber}/reviews/{reviewId}",
            stringContent);
    }


    public async Task<ReviewComment> AddFileCodeReviewCommentAsync(
        PullRequestDetails pr,
        string filePath,
        int lineNumber1Based,
        string commentBody)
    {
        if (!FileAppearsInAnyUserCommit(pr.Commits, filePath))
            throw new InvalidOperationException(
                $"Cannot place an inline review comment: '{filePath}' was not changed by user commits in this PR.");

        // Try the PR head first
        var headAttemptApiPullRequestReviewComment = await TryPostingToPullRequestHeadAsync(pr, filePath, lineNumber1Based, commentBody);
        if (headAttemptApiPullRequestReviewComment is not null)
            return ReviewComment.FromApiReviewComment(headAttemptApiPullRequestReviewComment);

        // Try the most recent user commits that touched the file, newest → oldest
        var commitAttemptApiPullRequestReviewComment =
            await TryPostingToMostRecentTouchingUserCommitAsync(pr, filePath, lineNumber1Based, commentBody);
        if (commitAttemptApiPullRequestReviewComment is not null)
            return ReviewComment.FromApiReviewComment(commitAttemptApiPullRequestReviewComment);

        throw new InvalidOperationException(
            $"Unable to place an inline review comment for '{filePath}' at line {lineNumber1Based}: not part of any eligible diff hunk in this PR.");
    }

    public async Task<T> GetApiResponseAsync<T>(string url)
    {
        var json = await GetApiResponseJsonAsync(url);
        return JsonConvert.DeserializeObject<T>(json) ??
               throw new InvalidOperationException($"Failed to parse response from {url}");
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<ApiPullRequestReviewComment?> TryPostingToPullRequestHeadAsync(
        PullRequestDetails pr,
        string filePath,
        int lineNumber1Based,
        string commentBody)
    {
        var headSha = pr.Branches.From.Sha;
        var payload = BuildInlineReviewCommentPayload(headSha, filePath, lineNumber1Based, commentBody);

        var res = await PostReviewCommentAsync(pr.Repo, pr.Number, payload).ConfigureAwait(false);
        if (res.Success) return res.Comment;

        if (res.StatusCode == 422) return null;

        throw new HttpRequestException($"POST review comment against PR head failed. HTTP {res.StatusCode}: {res.ErrorBody}");
    }

    private async Task<ApiPullRequestReviewComment?> TryPostingToMostRecentTouchingUserCommitAsync(
        PullRequestDetails pr,
        string filePath,
        int lineNumber1Based,
        string commentBody)
    {
        foreach (var sha in EnumerateUserCommitShasThatTouchedFileNewestToOldest(pr, filePath))
        {
            var payload = BuildInlineReviewCommentPayload(sha, filePath, lineNumber1Based, commentBody);
            var res = await PostReviewCommentAsync(pr.Repo, pr.Number, payload).ConfigureAwait(false);

            if (res.Success) return res.Comment;

            if (res.StatusCode != 422)
                throw new HttpRequestException($"POST review comment against commit {sha} failed. HTTP {res.StatusCode}: {res.ErrorBody}");

            // else: 422 → try next older qualifying commit
        }

        return null;
    }

    private static IEnumerable<string> EnumerateUserCommitShasThatTouchedFileNewestToOldest(
        PullRequestDetails pr,
        string filePath) =>
        pr.Commits.Where(c => FileListedInCommit(c.Files, filePath))
            .OrderByDescending(c => c.Date)
            .Select(c => c.Sha);

    private sealed class PostResult
    {
        public bool Success { get; init; }
        public int StatusCode { get; init; }
        public string ErrorBody { get; init; } = string.Empty;
        public ApiPullRequestReviewComment? Comment { get; init; }
    }

    private async Task<PostResult> PostReviewCommentAsync(string repo, int prNumber, object payload)
    {
        var url = GetAbsoluteUrl($"repos/{_owner}/{repo}/pulls/{prNumber}/comments");

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var stringContent = new StringContent(JsonConvert.SerializeObject(payload),
            Encoding.UTF8, "application/json");
        req.Content = stringContent;

        using var res = await _client.SendAsync(req).ConfigureAwait(false);
        var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (res.IsSuccessStatusCode)
            return new PostResult
            {
                Success = true,
                StatusCode = (int)res.StatusCode,
                Comment = JsonConvert.DeserializeObject<ApiPullRequestReviewComment>(content)
            };

        return new PostResult
        {
            Success = false,
            StatusCode = (int)res.StatusCode,
            ErrorBody = content
        };
    }

    private object BuildInlineReviewCommentPayload(string commitSha, string filePath, int lineNumber1Based, string body) =>
        new
        {
            body,
            commit_id = commitSha,
            path = filePath,
            line = lineNumber1Based,  // 1-based
            side = "RIGHT"
        };

    private static bool FileAppearsInAnyUserCommit(IEnumerable<PullRequestCommit> commits, string filePath) =>
        commits.Where(c => c.CommitType == CommitType.UserCommit)
            .Any(c => FileListedInCommit(c.Files, filePath));

    private static bool FileListedInCommit(CommitFile[] files, string filePath) =>
        files.Any(f => f.FileName.Equals(filePath, StringComparison.OrdinalIgnoreCase));

    private async Task<string> GetApiResponseJsonAsync(string url)
    {
        using var response = await _client.GetAsync(GetAbsoluteUrl(url));
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to fetch response from {url}: {response.StatusCode}");
        }
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> GetApiResponseJArrayJsonAsync(string url)
    {
        var uri = new Uri(GetAbsoluteUrl(url));
        var hasQueryParameters = HttpUtility.ParseQueryString(uri.Query).Count > 0;

        var jArray = new JArray();
        var page = 0;
        while (true)
        {
            page += 1;
            var pageUrl = hasQueryParameters
                ? $"{url}&page={page}&per_page={PerPage}"
                : $"{url}?page={page}&per_page={PerPage}";
            var json = await GetApiResponseJsonAsync(pageUrl);
            var jArrayPage = JArray.Parse(json);
            if (jArrayPage.Count == 0) break;
            jArray.Merge(jArrayPage);
        }

        return jArray.ToString(Formatting.Indented);
    }

    private string GetAbsoluteUrl(string url) =>
        url.StartsWith(_apiBaseUrl, StringComparison.InvariantCultureIgnoreCase)
            ? url
            : $"{_apiBaseUrl}/{url.TrimStart('/')}";
    private static T DeserializeOrThrow<T>(string fieldJson, string fieldName) =>
        JsonConvert.DeserializeObject<T>(fieldJson ??
                                         throw new InvalidOperationException(
                                             $"Pull Request field {fieldName} is null")) ??
        throw new InvalidOperationException(
            $"Failed parsing {typeof(T).Name} from json: {fieldJson}");
}