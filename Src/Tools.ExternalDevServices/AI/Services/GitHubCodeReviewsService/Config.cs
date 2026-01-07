using Newtonsoft.Json;

namespace Tools.ExternalDevServices.AI.Services.GitHubCodeReviewsService;

public class GitHubOrganization
{
    public string Url { get; set; } = "";
    public string Owner { get; set; } = "";
}

public enum LlmHostType
{
    LMStudio,
    Ollama
}

public class Model
{
    public LlmHostType HostType { get; set; }
    public string HostUrl { get; set; } = "";
    public string ModelId { get; set; } = "";
}

public class PullRequestMinInfo
{
    public int Number { get; set; }
    public string Repo { get; set; } = "";
}

public class Config
{
    public int PollingIntervalInSeconds { get; set; } = 30;
    public string GitHubPersonalAccessToken { get; set; } = "";
    public bool AddPlaceholderComment { get; set; }
    public bool DiagnosticsConsoleEnabled { get; set; }
    public bool DebugModeEnabled { get; set; }
    public PullRequestMinInfo[] DebugPullRequests { get; set; } = [];
    public GitHubOrganization Organization { get; set; } = null!;
    public Model Model { get; set; } = null!;
    public string OutputFolder { get; set; } = "";

    public static async Task<Config> LoadAsync(string configFile)
    {
        if (!File.Exists(configFile))
        {
            throw new FileNotFoundException($"Configuration file '{configFile}' not found.");
        }

        var config = JsonConvert.DeserializeObject<Config>(await File.ReadAllTextAsync(configFile));
        return config ?? throw new InvalidOperationException($"Failed to load configuration from '{configFile}'.");
    }
}