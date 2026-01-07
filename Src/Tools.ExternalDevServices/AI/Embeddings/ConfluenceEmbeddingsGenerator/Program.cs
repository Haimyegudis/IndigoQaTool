using System.ClientModel;
using System.Diagnostics;
using CommandLine;
using Microsoft.Extensions.AI;
using OpenAI;
using Tools.ExternalDevServices.AI.Embeddings.ConfluenceEmbeddingsService;
using Tools.ExternalDevServices.Integrations.Confluence;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Embeddings.ConfluenceEmbeddingsGenerator;

public class Program
{
    public class Options
    {
        [Option("confluence-url", Required = true)]
        public string ConfluenceUrl { get; set; } = string.Empty;

        [Option("personal-access-token", Required = true)]
        public string PersonalAccessToken { get; set; } = string.Empty;

        [Option("embedding-generator-type", Required = false)]
        public EmbeddingGeneratorType EmbeddingGeneratorType { get; set; } = EmbeddingGeneratorType.OpenAI;

        [Option("embedding-host-uri", Required = true)]
        public string EmbeddingHostUri { get; set; } = string.Empty;

        [Option("embedding-model-id", Required = true)]
        public string EmbeddingModelId { get; set; } = string.Empty;

        [Option("chat-host-uri", Required = true)]
        public string ChatHostUri { get; set; } = string.Empty;

        [Option("chat-model-id", Required = true)]
        public string ChatModelId { get; set; } = string.Empty;

        [Option("debug", Required = false)]
        public bool Debug { get; set; } = false;
    }
    
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Generator started");
        
        var result = Parser.Default.ParseArguments<Options>(args);
        if (result is NotParsed<Options>) Environment.Exit(1);

        var options = result.Value;

        //Initialize embeddings cache
        var path = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "EmbeddingsDb");
        Directory.CreateDirectory(path);
        var dbPath = Path.Combine(path, "EmbeddingsDb.db");

        var chatClient = new OpenAIClient(new ApiKeyCredential("no_key_is_needed"),
                new OpenAIClientOptions { Endpoint = new Uri(options.ChatHostUri) })
            .GetChatClient(options.ChatModelId)
            .AsIChatClient();

        using var softwareRequirementsEmbeddingsService =
            new SoftwareRequirementsEmbeddingsService(dbPath, options.EmbeddingGeneratorType, options.EmbeddingHostUri,
                options.EmbeddingModelId, chatClient);

        Console.WriteLine("Verifying access to Embedding generator...");
        await softwareRequirementsEmbeddingsService.EnsureEmbeddingsGeneratorEndpointAsync();
        Console.WriteLine($"Embedding generator {options.EmbeddingGeneratorType} is running at {options.EmbeddingHostUri} with model {options.EmbeddingModelId}.");

        Console.WriteLine("Verifying access to Chat client by sending 'Hi'...");
        var response = await softwareRequirementsEmbeddingsService.EnsureChatClientEndpointAsync("Hi");
        Console.WriteLine($"Chat client is running at {options.ChatHostUri} with model {options.ChatModelId}. Response: {response}");

        //Initialize Confluence client
        using var confluenceRestApiClient = new ConfluenceRestApiClient(options.ConfluenceUrl, options.PersonalAccessToken);
        var confluenceDocumentsRootPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "ConfluenceDocuments");
        Directory.CreateDirectory(confluenceDocumentsRootPath);

        if (options.Debug)
        {
            await DebugMethod(softwareRequirementsEmbeddingsService, confluenceRestApiClient);
            Environment.Exit(0);
        }

        var sw = Stopwatch.StartNew();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Deleting Embeddings DB");
        await softwareRequirementsEmbeddingsService.DeleteAsync();
        Console.ResetColor();

        #region SW Requirements

        //Build embeddings for SWSE documents
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Downloading SWSE Requirements documents...");
        Console.ResetColor();
        await confluenceRestApiClient.DownloadDocumentsAsync(confluenceDocumentsRootPath, "SWSE", includeLabels: true,
            static metadata => metadata.Title.EndsWith("Requirements", StringComparison.OrdinalIgnoreCase) && IsSupportedSeries(metadata));
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Downloading Requirements documents done.");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Caching SWSE Requirements documents...");
        Console.ResetColor();
        await softwareRequirementsEmbeddingsService.CacheSwseRequirementsDocumentsFolderAsync(
            confluenceDocumentsRootPath, "SWSE");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Caching SWSE Requirements documents done.");
        Console.ResetColor();

        #endregion

        #region Software Design

        //Build embeddings for SD documents
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Downloading SD Software Design documents...");
        Console.ResetColor();
        await confluenceRestApiClient.DownloadDocumentsAsync(confluenceDocumentsRootPath, "SD", includeLabels: true,
            static metadata =>
                (metadata.Title.EndsWith("SW Design", StringComparison.OrdinalIgnoreCase) ||
                 metadata.Title.EndsWith("design document", StringComparison.OrdinalIgnoreCase)) &&
                IsSupportedSeries(metadata));

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Caching SD Software Design documents...");
        Console.ResetColor();
        await softwareRequirementsEmbeddingsService.CacheSdDesignDocumentsFolderAsync(
            confluenceDocumentsRootPath, "SD");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Caching SD Software Design documents done.");
        Console.ResetColor();

        #endregion

        #region OCB Flags

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Downloading S6 'Global Configurations (OCB Flags)' documents...");
        Console.ResetColor();
        await confluenceRestApiClient.DownloadDocumentsAsync(Path.Combine(confluenceDocumentsRootPath, "PSWA"),
            "https://v-indigo-confluence.inr.rd.hpicorp.net:6443/display/PSWA/Global+Configurations+%28OCB+Flags%29+-+Developer+Guidelines",
            "https://v-indigo-confluence.inr.rd.hpicorp.net:6443/display/PSWA/Global+Configurations+%28OCB+Flags%29+-+Modifying+existing+flag+-+Important+Notes",
            "https://v-indigo-confluence.inr.rd.hpicorp.net:6443/pages/viewpage.action?pageId=569031527");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Downloading S6 'Global Configurations (OCB Flags)' documents done.");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Caching S6 'Global Configurations (OCB Flags)' documents...");
        Console.ResetColor();
        await softwareRequirementsEmbeddingsService.CacheSpaceDocumentsFolderAsync(
            confluenceDocumentsRootPath, "PSWA", static (md, markdown) =>
            {
                //Special processing for 'Global Configurations (OCB Flags)' - Q&A document
                if (!md.Title.Equals("Global Configurations (OCB Flags) - Q&A", StringComparison.OrdinalIgnoreCase)) return [];

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Adding additions segments for S6 'Global Configurations (OCB Flags) - Q&A' document");
                Console.ResetColor();
                var questionsSegment = SegmentationUtils.ConvertMarkdownToSegments(markdown)
                    .FirstOrDefault(s => s.key.EndsWith("Questions", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(questionsSegment.content))
                {
                    return questionsSegment.content.Split('|',
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(q => q.Trim().EndsWith('?'))
                        .Select(q => q.Trim());
                }

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to get questions segment for S6 'Global Configurations (OCB Flags) - Q&A' document");
                Console.ResetColor();
                return [];

            },  embeddingPrefixes: "S6");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Caching S6 'Global Configurations (OCB Flags)' documents done.");
        Console.ResetColor();

        #endregion

        sw.Stop();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Rebuilding documents embeddings completed after {sw.Elapsed}. Press Enter to exit...");
        Console.ResetColor();
        Console.ReadLine();
    }

    private static bool IsSupportedSeries(ConfluenceDocumentMetadata metadata) =>
        SeriesHelper.HasSupportedSeries(metadata.Title) || (metadata.Attributes?.Results is not null &&
                                                            metadata.Attributes.Results.Labels.Any(label =>
                                                                SeriesHelper.HasSupportedSeries(label.Name)));

    private static async Task DebugMethod(SoftwareRequirementsEmbeddingsService softwareRequirementsEmbeddingsService,
        // ReSharper disable once UnusedParameter.Local
        ConfluenceRestApiClient confluenceRestApiClient)
    {
        Console.WriteLine("Entered debug mode");
        
       /* var confluenceDocumentsRootPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "ConfluenceDocumentsForDebug");
        Directory.CreateDirectory(confluenceDocumentsRootPath);

        await confluenceRestApiClient.DownloadDocumentsAsync(confluenceDocumentsRootPath,
            "https://v-indigo-confluence.inr.rd.hpicorp.net:6443/pages/viewpage.action?pageId=431259915");
        await softwareRequirementsEmbeddingsService.CacheSwseRequirementsDocumentsFolderAsync(confluenceDocumentsRootPath);

        Console.WriteLine("Embedding ended");
        Console.WriteLine();*/

        while (true)
        {
            Console.WriteLine("USER:");
            var prompt = Console.ReadLine() ?? "";

            if(string.IsNullOrEmpty(prompt) || prompt.ToLower() == "exit")
                break;

            var series = SeriesHelper.GetSeriesOccurrences(prompt).ToArray();
            if (series.Length == 0)
            {
                Console.WriteLine("Prompt does not contain a series");
                continue;
            }

            foreach (var seriesInstance in series)
            {
                prompt = prompt.Replace(seriesInstance, "").Trim();
            }

            prompt = prompt.Trim();

            foreach (var seriesInstance in series)
            {
                var response =
                    await softwareRequirementsEmbeddingsService.FindBestDocumentsForPromptBySimilarityThreshold(
                        seriesInstance, prompt, 0.65, null);
                Console.WriteLine($"SYSTEM [{seriesInstance}]:");
                foreach (var group in response.GroupBy(p => p.DocumentEmbeddings.DocumentTitle)
                             .OrderByDescending(g => g.Max(p => p.Similarity)))
                {
                    Console.WriteLine($"{group.Key} ({group.Max(p => p.Similarity)})");
                }

                Console.WriteLine();
            }
        }
    }
}