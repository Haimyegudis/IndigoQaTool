using System.Net.Http.Headers;
using System.Threading.Tasks.Dataflow;
using System.Web;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Tools.ExternalDevServices.Utils;
// ReSharper disable ConditionalAccessQualifierIsNonNullableAccordingToAPIContract

// Added for Config

namespace Tools.ExternalDevServices.Integrations.Confluence;

public class ConfluenceRestApiClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public string ConfluenceUrl { get; }
    
    public ConfluenceRestApiClient(string confluenceUrl, string personalAccessToken)
    {
        ConfluenceUrl = confluenceUrl;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, errors) =>
            {
                // Temporarily ignore SSL errors that occurred on the AI PC until proper fix on the server side
                Console.WriteLine($"SSL errors logged: {errors}");
                return true; // accept any certificate (INSECURE!)
            }
        };
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(confluenceUrl)
        };
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", personalAccessToken);
    }

    public async Task<(ConfluenceDocumentMetadata metadata, string markdown)>
        GetDocumentMetadataAndMarkdownByPageIdAsync(string pageId)
    {
        var query = $"rest/api/content/{pageId}?expand=body.view,metadata.labels";
        return await GetDocumentMetadataAndMarkdownByQueryAsync(query);
    }

    public async Task<( ConfluenceDocumentMetadata metadata, string markdown)> GetDocumentMetadataAndMarkdownByUrlAsync(string url)
    {
        var uri = new Uri(url);
        var queryStringParams = HttpUtility.ParseQueryString(uri.Query);
        var pageId = queryStringParams.Get("pageId");
        if (!string.IsNullOrEmpty(pageId))
        {
            var query = $"rest/api/content/{pageId}?expand=body.view,metadata.labels";
            return await GetDocumentMetadataAndMarkdownByQueryAsync(query);
        }

        var space = queryStringParams.Get("spaceKey");
        var title = queryStringParams.Get("title");

        if (string.IsNullOrEmpty(space) || string.IsNullOrEmpty(title))
        {
            var urlParts = url.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            space ??= urlParts[^2];
            title ??= urlParts[^1].Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .First();
        }

        return await GetDocumentMetadataAndMarkdownByQueryAsync(GetDocumentQueryByTitleAndSpace(title, space));
    }

    public async Task<string> GetDocumentAsMarkdownByUrlAsync(string url)
    {
        var uri = new Uri(url);
        var queryStringParams = HttpUtility.ParseQueryString(uri.Query);
        var pageId = queryStringParams.Get("pageId");
        if (!string.IsNullOrEmpty(pageId))
        {
            return await GetDocumentAsMarkdownByPageIdAsync(pageId, includeMetaTables: false);
        }

        var urlParts = url.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var space = urlParts[^2];
        var title = urlParts[^1].Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .First();

        return await GetDocumentAsMarkdownBySpaceAndTitleAsync(title, space, includeMetaTables: false);
    }

    public async Task<string> GetDocumentHtmlByUrlAsync(string url)
    {
        var uri = new Uri(url);
        var queryStringParams = HttpUtility.ParseQueryString(uri.Query);
        var pageId = queryStringParams.Get("pageId");
        if (!string.IsNullOrEmpty(pageId))
        {
            return await GetDocumentByIdAsync(pageId);
        }

        var urlParts = url.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var space = urlParts[^2];
        var title = urlParts[^1].Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .First();

        return await GetDocumentByTitleAndSpaceAsync(title, space);
    }

    public async Task<string> GetDocumentAsMarkdownByPageIdAsync(string pageId, bool includeMetaTables = true)
    {
        var body = await GetDocumentByIdAsync(pageId);
        return HtmlToTextConverter.ConvertConfluenceHtmlToMarkdown(body, includeMetaTables);
    }

    public async Task<string> GetDocumentAsMarkdownBySpaceAndTitleAsync(string documentTitle, string space, bool includeMetaTables = true)
    {
        var body = await GetDocumentByTitleAndSpaceAsync(documentTitle, space);
        return HtmlToTextConverter.ConvertConfluenceHtmlToMarkdown(body, includeMetaTables);
    }

    public async IAsyncEnumerable<ConfluenceDocumentMetadata> GetConfluenceSpaceDocumentsMetaDatas(string space,
        bool includeLabels,
        Func<ConfluenceDocumentMetadata, bool> filter)
    {
        var start = 0;
        const int limit = 50;
        var more = true;

        //var cql = $"type=page AND space={space} AND title ~ \"SW Requirements\"";
        while (more)
        {
            //var url = $"rest/api/content?cql={Uri.EscapeDataString(cql)}&limit={limit}&start={start}";
            var url = $"rest/api/content?type=page&spaceKey={space}&limit={limit}&start={start}";
            if(includeLabels) url += "&expand=metadata.labels";
            string? response = null;
            for (var retries = 0; retries < 3; retries++)
            {
                try
                {
                    response = await _httpClient.GetStringAsync(url);
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to retrieve metadata for url {url},\r\nerror: {ex.Message}\r\n, waiting for 30 seconds");
                    await Task.Delay(30_000);
                }
            }

            if(response is null)
                throw new InvalidOperationException($"Failed to retrieve metadata after 3 attempts for query: {url}");

            var metadataResponse = JsonConvert.DeserializeObject<ConfluenceDocumentMetadataResponse>(response) ??
                          throw new InvalidOperationException($"Could not parse metadata from {response}");
            if (metadataResponse.Results is null)
            {
                break;
            }
            foreach (var metadata in metadataResponse.Results.Where(filter))
            {
                metadata.Space = space;
                yield return metadata;
            }

            more = metadataResponse.Results.Any();
            start += metadataResponse.Results.Length;
        }
    }

    public async Task<IReadOnlyCollection<(string key, string content)>> GetDocumentMarkdownSegmentsAsync(
        string documentTitle, string space)
    {
        var markdown = await GetDocumentAsMarkdownBySpaceAndTitleAsync(documentTitle, space);
        return SegmentationUtils.ConvertMarkdownToSegments(markdown);
    }

    public async Task<IReadOnlyCollection<(string key, string content)>> GetDocumentSegmentsAsync(string documentTitle, string space)
    {
        var body = await GetDocumentByTitleAndSpaceAsync(documentTitle, space);
        var doc = new HtmlDocument();
        doc.LoadHtml(body);

        var segments = new List<(string key, string content)>();

        var sb = new System.Text.StringBuilder();

        string? h1 = null;
        string? h2 = null;
        string? h3 = null;
        string? currentKey = null;

        var liIndex = 0;

        foreach (var node in doc.DocumentNode.Descendants())
        {
            if (node.Name is "h1" or "h2" or "h3")
            {
                // Flush current chunk
                if (!string.IsNullOrWhiteSpace(currentKey) && sb.Length > 0)
                {
                    segments.Add((currentKey, sb.ToString().Trim()));
                    sb.Clear();
                }

                var title = node.InnerText.Trim();
                switch (node.Name)
                {
                    case "h1":
                        h1 = title;
                        h2 = h3 = null;
                        break;
                    case "h2":
                        h2 = title;
                        h3 = null;
                        break;
                    case "h3":
                        h3 = title;
                        break;
                }

                // Build flattened key
                currentKey = string.Join(" > ",
                    new[] { h1, h2, h3 }.Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            else if (node.Name == "ol")
            {
                liIndex = 0;
            }
            else if (!string.IsNullOrWhiteSpace(currentKey) && node.NodeType is HtmlNodeType.Element && !IsStylingNode(node))
            {
                var text = HtmlEntity.DeEntitize(node.InnerText.Trim())?
                    .Replace('\u00A0', ' ') ?? node.InnerText.Trim().Replace('\u00A0', ' ');
                if (node.Name == "li")
                {
                    liIndex += 1;
                    text = $"{liIndex}. {text}";
                }
                sb.AppendLine(text);
                if (text.StartsWith("2.1.1. Background Digital printing presses are "))
                {

                }
            }
        }

        if (!string.IsNullOrWhiteSpace(currentKey) && sb.Length > 0)
        {
            segments.Add((currentKey, sb.ToString().Trim()));
        }

        return segments;
    }

    public async Task<string> GetDocumentByTitleAndSpaceAsync(string documentTitle, string space)
    {
        var query = GetDocumentQueryByTitleAndSpace(documentTitle, space);
        return await GetDocumentByQueryAsync(query);
    }

    public async Task<string> GetDocumentByIdAsync(string documentID)
    {
        var query = $"rest/api/content/{documentID}?expand=body.view,metadata.labels";
        return await GetDocumentByQueryAsync(query);
    }

    public async Task DownloadDocumentsAsync(string rootPath, string space, bool includeLabels, Func<ConfluenceDocumentMetadata, bool> filter)
    {
        var spacePath = Path.Combine(rootPath, space);
        Directory.CreateDirectory(spacePath);

        var ab = new ActionBlock<ConfluenceDocumentMetadata>(async md =>
        {
            await DownloadDocumentAsync(spacePath, md);
        }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 4 });

        await foreach (var md in GetConfluenceSpaceDocumentsMetaDatas(space, includeLabels, filter))
        {
            await ab.SendAsync(md);
        }
        ab.Complete();
        await ab.Completion;
    }

    public async Task DownloadDocumentsAsync(string path, params string[] uris)
    {
        foreach (var uri in uris)
        {
            var (md, markdown) = await GetDocumentMetadataAndMarkdownByUrlAsync(uri);
            await WriteToDiskAsync(md, await GetDocumentByIdAsync(md.Id), markdown, path);
        }
    }

    public async Task DownloadDocumentAsync(string path, ConfluenceDocumentMetadata md)
    {
        try
        {
            Console.WriteLine($"Fetching Confluence document for '{md.Title}'...");

            var html = await GetDocumentByIdAsync(md.Id);
            var markdown = HtmlToTextConverter.ConvertConfluenceHtmlToMarkdown(html);
            //var plainText = HtmlToTextConverter.ConvertConfluenceHtmlToPlainText(html);

            await WriteToDiskAsync(md, html, markdown, path);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error fetching Confluence document '{md.Title}': {ex.Message}");
            Console.ResetColor();
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private static bool IsStylingNode(HtmlNode node) =>
        node.Name is ("span" or "div" or "u" or "i" or "b" or "strong" or "em" or "small" or "mark" or "sub" or "sup" or "abbr"
            or "cite" or "code");

    private async Task<(ConfluenceDocumentMetadata metadata, string markdown)> GetDocumentMetadataAndMarkdownByQueryAsync(string query)
    {
        var documentResponse = await GetDocumentResponseByQueryAsync(query);

        if (JsonConvert.DeserializeObject<ConfluenceDocumentMetadataResponse>(documentResponse) is { } metadataResponse &&
            metadataResponse.Results?.Length > 0)
        {
            var metadata = metadataResponse.Results.FirstOrDefault() ??
                           JsonConvert.DeserializeObject<ConfluenceDocumentMetadata>(documentResponse) ??
                           throw new InvalidOperationException($"Failed to get document metadata from query: {query}");

            var confluenceDocumentResponse = JsonConvert.DeserializeObject<ConfluenceDocumentResponse>(documentResponse);
            var result = confluenceDocumentResponse?.Results?.FirstOrDefault() ??
                         JsonConvert.DeserializeObject<ConfluenceDocumentResult>(documentResponse);
            var body = result?.Body.View.Value ?? "";

            return (metadata, HtmlToTextConverter.ConvertConfluenceHtmlToMarkdown(body, includeMetaTables: false));
        }

        if (JsonConvert.DeserializeObject<ConfluenceDocumentResult>(documentResponse) is { } confluenceDocumentResult &&
            JsonConvert.DeserializeObject<ConfluenceDocumentMetadata>(documentResponse) is { } confluenceDocumentMetadata &&
            !string.IsNullOrEmpty(confluenceDocumentMetadata.Title) && !string.IsNullOrEmpty(confluenceDocumentResult.Title))
        {
            return (confluenceDocumentMetadata,
                HtmlToTextConverter.ConvertConfluenceHtmlToMarkdown(confluenceDocumentResult.Body?.View.Value ?? "",
                    includeMetaTables: false));
        }

        throw new InvalidOperationException($"Document does not match known schema types, query: {query}");
    }

    private static string GetDocumentQueryByTitleAndSpace(string documentTitle, string space)
    {
        documentTitle = Uri.UnescapeDataString(documentTitle).Replace("+", " ");
        var escapedDocumentTitle = Uri.EscapeDataString(documentTitle);

        space = space.Replace("+", " ");
        var escapedSpace = Uri.EscapeDataString(space);

        return $"rest/api/content?spaceKey={escapedSpace}&title={escapedDocumentTitle}&expand=body.view,metadata.labels";
    }

    private async Task<string> GetDocumentByQueryAsync(string query)
    {
        using var response = await _httpClient.GetAsync(query);
        response.EnsureSuccessStatusCode();
        var jsonResponse = await response.Content.ReadAsStringAsync();

        var confluenceDocumentResponse = JsonConvert.DeserializeObject<ConfluenceDocumentResponse>(jsonResponse);

        var result = confluenceDocumentResponse?.Results?.FirstOrDefault() ??
                     JsonConvert.DeserializeObject<ConfluenceDocumentResult>(jsonResponse);
        var body = result?.Body.View.Value ?? "";
        return body;
    }

    private async Task<string> GetDocumentResponseByQueryAsync(string query)
    {
        using var response = await _httpClient.GetAsync(query);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task WriteToDiskAsync(ConfluenceDocumentMetadata md, string html, string markdown, string path)
    {
        var sanitizedTitle = FileUtils.NormalizeFileName(md.Title);
        var documentPath = Path.Combine(path, sanitizedTitle);
        Directory.CreateDirectory(documentPath);

        await File.WriteAllTextAsync(Path.Combine(documentPath, "_Metadata.json"),
            JsonConvert.SerializeObject(md, Formatting.Indented));
        await File.WriteAllTextAsync(Path.Combine(documentPath, $"{sanitizedTitle}.html"), html);
        await File.WriteAllTextAsync(Path.Combine(documentPath, $"{sanitizedTitle}_Markdown.txt"), markdown);
        //await File.WriteAllTextAsync(Path.Combine(documentPath, $"{sanitizedTitle}_Plain.txt"), plainText);

        Console.WriteLine($"Fetching Confluence document for '{md.Title}' completed.");
    }
}