using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text;

namespace Tools.ExternalDevServices.Integrations.Jira;

public class JiraRestApiClient : IDisposable
{
    public Endpoints Endpoints { get; }
    private readonly HttpClient _httpClient;

    public JiraRestApiClient(string jiraBaseUrl, string apiVersion, string jiraUser, string personalAccessToken)
    {
        Endpoints = new Endpoints(jiraBaseUrl, apiVersion);
        
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(Endpoints.JiraBaseUrl);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{jiraUser}:{personalAccessToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
    }

    public async Task<string> GetMeAsync()
    {
        using var response = await _httpClient.GetAsync(Endpoints.MySelf);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<IReadOnlyCollection<JiraRestApiTypes.Issue>> GetUsersDefectsAsync(IReadOnlyCollection<string> users)
    {
        if (users.Count == 0) return [];

        var jql =
            $"issuetype = Incident AND Status not in (Closed, Canceled, Rejected, Verification, Delivered) AND assignee in ({string.Join(',', users.Select(user => $"'{user}'"))})";
        var searchUrl = $"{Endpoints.SearchRestApi}/search?jql={jql}";

        return await GetSearchResponseAsync<JiraRestApiTypes.Issue, JiraRestApiTypes.IssuesResponse>(searchUrl);
    }

    public async Task<JiraRestApiTypes.Issue> GetIssueAsync(string issueKeyOrId)
    {
        var url = $"{Endpoints.Issue}/{issueKeyOrId}";
        return await GetAsync<JiraRestApiTypes.Issue>(url);
    }

    public async Task<JiraRestApiTypes.Epic> GetEpicAsync(string epicKeyOrId)
    {
        var url = $"{Endpoints.Issue}/{epicKeyOrId}";
        return await GetAsync<JiraRestApiTypes.Epic>(url);
    }

    public async Task<IReadOnlyCollection<JiraRestApiTypes.Issue>> GetUsersActiveSprintIssues(string boardId, IReadOnlyCollection<string> users) =>
        await GetSprintIssues(users,
            async () => (await GetActiveSprint(boardId))?.Id ??
                        throw new InvalidOperationException("Failed to get active sprint"));

    public async Task<IReadOnlyCollection<JiraRestApiTypes.Issue>> GetUsersFutureSprintIssues(string boardId, IReadOnlyCollection<string> users) =>
        await GetSprintIssues(users,
            async () => (await GetFutureSprint(boardId))?.Id ??
                        throw new InvalidOperationException("Failed to get future sprint"));

    public async Task<JiraRestApiTypes.Sprint?> GetActiveSprint(string boardId) =>
        (await GetSprints(boardId, state: "active")).FirstOrDefault();

    public async Task<JiraRestApiTypes.Sprint?> GetFutureSprint(string boardId) =>
        (await GetSprints(boardId, state: "future")).FirstOrDefault();

    public async Task<JiraRestApiTypes.Sprint?> GetSprintByName(string boardId, string name) =>
        (await GetSprints(boardId, state: "")).FirstOrDefault(s => s.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

    public async Task<IReadOnlyCollection<JiraRestApiTypes.Sprint>> GetSprints(string boardId, string? state)
    {
        var searchUrl = $"{Endpoints.BoardsRestApi}/{boardId}/sprint";
        if (!string.IsNullOrEmpty(state)) searchUrl += $"?state={state}";

        return await GetSearchResponseAsync<JiraRestApiTypes.Sprint, JiraRestApiTypes.ValuesResponse<JiraRestApiTypes.Sprint>>(searchUrl);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<T> GetAsync<T>(string url)
    {
        using var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var jsonResponse = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<T>(jsonResponse) ??
               throw new InvalidOperationException($"Failed to deserialize response from {url}");
    }

    private async Task<IReadOnlyCollection<T>> GetSearchResponseAsync<T, TResponse>(string searchUrl)
        where TResponse : JiraRestApiTypes.Response<T>
    {
        var items = new List<T>();
        var startAt = 0;

        while (true)
        {
            using var response = await _httpClient.GetAsync($"{searchUrl}{(searchUrl.Contains("?") ? "&" : "?")}{nameof(startAt)}={startAt}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonConvert.DeserializeObject<TResponse>(content) ??
                              throw new InvalidOperationException($"Failed to deserialize response from query {searchUrl}");
            items.AddRange(apiResponse.Items);

            if (!apiResponse.HasMoreItems || apiResponse.Items.Length == 0) break;
            startAt += apiResponse.Items.Length;
        }

        return items;
    }

    private async Task<IReadOnlyCollection<JiraRestApiTypes.Issue>> GetSprintIssues(IReadOnlyCollection<string> users, Func<Task<int>> sprintIdFunc)
    {
        if (users.Count == 0) return [];

        var jql =
            $"Sprint in ({await sprintIdFunc()}) AND assignee in ({string.Join(',', users.Select(user => $"'{user}'"))}) order by assignee, Status";
        var searchUrl = $"{Endpoints.SearchRestApi}/search?jql={Uri.EscapeDataString(jql)}";

        return await GetSearchResponseAsync<JiraRestApiTypes.Issue, JiraRestApiTypes.IssuesResponse>(searchUrl);
    }
}