using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

namespace Tools.ExternalDevServices.Integrations.TestRail;

public class TestRailRestApiClient : IDisposable
{
    public const string ApiPath = "index.php?/api/v2";
    private readonly HttpClient _httpClient;

    public TestRailRestApiClient(string testRailBaseUrl, string testRailUser, string apiKey)
    {
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(testRailBaseUrl);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{testRailUser}:{apiKey}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<TestRailRestApiTypes.TestCase> GetTestCaseAsync(string id)
    {
        var response = await _httpClient.GetAsync($"{ApiPath}/get_case/{id}");
        response.EnsureSuccessStatusCode();

        var responseData = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<TestRailRestApiTypes.TestCase>(responseData) ?? throw new InvalidOperationException($"Failed to get test case: {id}");
    }

    public async Task<TestRailRestApiTypes.TestCaseMetadata[]> GetTestCasesAsync(string projectId, string suiteId, string refs)
    {
        var response = await _httpClient.GetAsync($"{ApiPath}/get_cases/{projectId}&suite_id={suiteId}&refs={refs}");
        response.EnsureSuccessStatusCode();

        var responseData = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<TestRailRestApiTypes.TestCaseMetadata[]>(responseData) ?? throw new InvalidOperationException($"Failed to get test cases. projectId: {projectId}, suiteId: {suiteId}, refs: {refs}");
    }

    public async Task<TestRailRestApiTypes.TestSuite[]> GetSuitesAsync(string projectId)
    {
        var response = await _httpClient.GetAsync($"{ApiPath}/get_suites/{projectId}");
        response.EnsureSuccessStatusCode();

        var responseData = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<TestRailRestApiTypes.TestSuite[]>(responseData) ?? throw new InvalidOperationException($"Failed to get test suites. projectId: {projectId}");
    }
}