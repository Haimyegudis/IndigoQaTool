using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Connectors.SqliteVec;
using Newtonsoft.Json;
using OpenAI;
using System.ClientModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using Tools.ExternalDevServices.Integrations.Confluence;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Embeddings.ConfluenceEmbeddingsService;

public class SoftwareRequirementsEmbeddingsService : IDisposable
{
    private readonly string _dbFilePath;
    private const int MaxDocumentsForCollectionSearch = 4096;

    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    private readonly IChatClient _chatClient;

    public SoftwareRequirementsEmbeddingsService(string dbFilePath, EmbeddingGeneratorType embeddingGeneratorType, string embeddingHostUri, string embeddingModelId, IChatClient chatClient)
    {
        _dbFilePath = dbFilePath;
        _embeddingGenerator = embeddingGeneratorType switch
        {
            EmbeddingGeneratorType.Ollama => new OllamaEmbeddingGenerator(embeddingHostUri, embeddingModelId),
            EmbeddingGeneratorType.OpenAI => new OpenAIClient(
                    new ApiKeyCredential("no_key_is_needed"),
                    new OpenAIClientOptions
                    {
                        Endpoint = new Uri(embeddingHostUri.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)? embeddingHostUri : $"{embeddingHostUri}/v1"),
                        NetworkTimeout = TimeSpan.FromHours(1)
                    })
                .GetEmbeddingClient(embeddingModelId)
                .AsIEmbeddingGenerator(),
            _ => throw new ArgumentOutOfRangeException(nameof(embeddingGeneratorType), embeddingGeneratorType,
                "Unsupported embedding generator type")
        };

        _chatClient = chatClient;
    }

    public void Dispose()
    {
         _embeddingGenerator.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task EnsureEmbeddingsGeneratorEndpointAsync() => 
        await _embeddingGenerator.GenerateAsync("Is Generator Running on configured endpoint?");

    public async Task<string> EnsureChatClientEndpointAsync(string prompt) =>
        (await _chatClient.GetResponseAsync(prompt)).Text;

    public async Task DeleteAsync() =>
        await Task.Run(() =>
        {
            if (!File.Exists(_dbFilePath)) return;
            File.Delete(_dbFilePath);
        });

    public async Task<IReadOnlyCollection<DocumentEmbeddingsResponse>>
        FindBestDocumentsForPromptBySimilarityThreshold(string embeddingsPrefix, string prompt, double similarityThreshold, ILogger? logger)
    {
        const int maxDocuments = 10;

        using var collection = await GetCollectionAsync(embeddingsPrefix);

        var sw = Stopwatch.StartNew();
        var promptEmbedding = (await _embeddingGenerator.GenerateAsync(prompt)).Vector.ToArray();
        sw.Stop();
        logger?.LogInformation($"Generating prompt embedding completed after {sw.Elapsed}.");

        sw.Restart();
        var result = new List<DocumentEmbeddingsResponse>();
        var foundDocuments = new HashSet<string>();
        await foreach (var documentEmbedding in collection.SearchAsync(promptEmbedding, MaxDocumentsForCollectionSearch))
        {
            if (!foundDocuments.Add(documentEmbedding.Record.DocumentId))
                continue;

            if (documentEmbedding.Score is null || 1.0 - documentEmbedding.Score.Value < similarityThreshold)
                break;

            result.Add(new DocumentEmbeddingsResponse(documentEmbedding.Record, 1.0 - documentEmbedding.Score.Value));

            if (result.Count >= maxDocuments)
                break;
        }
        sw.Stop();
        logger?.LogInformation($"Searching for best documents completed after {sw.Elapsed}.");

        return result;
    }

    public async Task<IReadOnlyCollection<DocumentEmbeddingsResponse>> 
        FindBestDocumentsForPromptWithLimit(string embeddingsPrefix, string prompt, int maxDocuments)
    {
        if (maxDocuments < 1)
            throw new ArgumentException("maxDocuments must be at least 1");

        using var collection = await GetCollectionAsync(embeddingsPrefix);

        var promptEmbedding = (await _embeddingGenerator.GenerateAsync(prompt)).Vector.ToArray();
        var result = new List<DocumentEmbeddingsResponse>();
        var foundDocuments = new HashSet<string>();
        await foreach (var documentEmbedding in collection.SearchAsync(promptEmbedding, MaxDocumentsForCollectionSearch))
        {
            if (!foundDocuments.Add(documentEmbedding.Record.DocumentId))
                continue;

            result.Add(new DocumentEmbeddingsResponse(documentEmbedding.Record, 1.0 - documentEmbedding.Score ?? 0));

            if (result.Count >= maxDocuments)
                break;
        }

        return result;
    }

    public async Task CacheSpaceDocumentsFolderAsync(string documentsRootFolder, string spaceFolderName,
        Func<ConfluenceDocumentMetadata, string, IEnumerable<string>>? markdownToEmbeddingsTextsFunc,
        params string[] embeddingPrefixes)
    {
        var segmentsForEmbeddingsBag =
            new ConcurrentBag<(string embeddingPrefix, string documentRootFolder, string documentMarkdownFilePath, string documentId, string documentTitle, string sample)>();
        var spaceFolderPath = Path.Combine(documentsRootFolder, spaceFolderName);
        var files = Directory.GetFiles(spaceFolderPath, "*_Markdown.txt", SearchOption.AllDirectories);

        var sw = Stopwatch.StartNew();
        Console.WriteLine($"Generating embeddings samples for {files.Length} files...");

        await Parallel.ForEachAsync(files, async (file, ct) =>
        {
            await foreach (var fileSegmentForEmbedding in GetSamplesForEmbeddingsAsync(file, markdownToEmbeddingsTextsFunc, embeddingPrefixes).WithCancellation(ct))
            {
                segmentsForEmbeddingsBag.Add(fileSegmentForEmbedding);
            }
        });

        sw.Stop();
        Console.WriteLine($"Generating {segmentsForEmbeddingsBag.Count} embeddings samples completed after {sw.Elapsed}.");

        if (segmentsForEmbeddingsBag.IsEmpty)
        {
            Console.WriteLine("No segments available for embeddings. Skipping generation.");
            return;
        }

        sw.Restart();
        Console.WriteLine("Checking duplicates...");

        await EmbedAsync(documentsRootFolder, segmentsForEmbeddingsBag);
    }

    public async Task CacheSwseRequirementsDocumentsFolderAsync(string documentsRootFolder, string spaceFolderName, params string[] embeddingPrefixes)
    {
        var segmentsForEmbeddingsBag =
            new ConcurrentBag<(string embeddingsPrefix, string documentFolder, string documentMarkdownFilePath, string documentId, string documentTitle, string sample)>();
        var spaceFolderPath = Path.Combine(documentsRootFolder, spaceFolderName);
        var files = Directory.GetFiles(spaceFolderPath, "*_Markdown.txt", SearchOption.AllDirectories);

        var sw = Stopwatch.StartNew();
        Console.WriteLine($"Generating embeddings samples for {files.Length} files...");

        await Parallel.ForEachAsync(files, async (file, ct) =>
        {
            await foreach (var fileSegmentForEmbedding in GetSoftwareRequirementsFileSamplesForEmbeddingsAsync(
                               file, embeddingPrefixes).WithCancellation(ct))
            {
                segmentsForEmbeddingsBag.Add(fileSegmentForEmbedding);
            }
        });

        sw.Stop();
        Console.WriteLine($"Generating {segmentsForEmbeddingsBag.Count} embeddings samples completed after {sw.Elapsed}.");

        if (segmentsForEmbeddingsBag.IsEmpty)
        {
            Console.WriteLine("No segments available for embeddings. Skipping generation.");
            return;
        }

        sw.Restart();
        Console.WriteLine("Checking duplicates...");

        await EmbedAsync(documentsRootFolder, segmentsForEmbeddingsBag);
    }

    public async Task CacheSdDesignDocumentsFolderAsync(string documentsRootFolder, string spaceFolderName, params string[] embeddingPrefixes)
    {
        var segmentsForEmbeddingsBag =
            new ConcurrentBag<(string embeddingsPrefix, string documentFolder, string documentMarkdownFilePath, string documentId, string documentTitle, string sample)>();
        var spaceFolderPath = Path.Combine(documentsRootFolder, spaceFolderName);
        var files = Directory.GetFiles(spaceFolderPath, "*_Markdown.txt", SearchOption.AllDirectories);

        var sw = Stopwatch.StartNew();
        Console.WriteLine($"Generating embeddings samples for {files.Length} files...");

        await Parallel.ForEachAsync(files, async (file, ct) =>
        {
            await foreach (var fileSegmentForEmbedding in GetSoftwareDesignFileSamplesForEmbeddingsAsync(
                               file, embeddingPrefixes).WithCancellation(ct))
            {
                segmentsForEmbeddingsBag.Add(fileSegmentForEmbedding);
            }
        });

        sw.Stop();
        Console.WriteLine($"Generating {segmentsForEmbeddingsBag.Count} embeddings samples completed after {sw.Elapsed}.");

        if (segmentsForEmbeddingsBag.IsEmpty)
        {
            Console.WriteLine("No segments available for embeddings. Skipping generation.");
            return;
        }

        sw.Restart();
        Console.WriteLine("Checking duplicates...");

        await EmbedAsync(documentsRootFolder, segmentsForEmbeddingsBag);
    }

    private async Task EmbedAsync(string documentsRootFolder,
        ConcurrentBag<(string embeddingPrefix, string documentRootFolder, string documentMarkdownFilePath, string
            documentId, string documentTitle, string sample)> segmentsForEmbeddingsBag)
    {
        var segmentsGrouping = segmentsForEmbeddingsBag.GroupBy(s => (s.embeddingPrefix, s.sample)).Where(g => g.Count() > 1).ToArray();

        if (segmentsGrouping.Length == 0)
        {
            Console.WriteLine("No duplicates found");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{segmentsGrouping.Length} Duplicates found:");
            Console.ResetColor();
            Console.WriteLine(
                $"{string.Join("\r\n", segmentsGrouping.Select((g, i) => $"{i + 1}. {g.Key}"))}");
            Console.WriteLine();
        }

        var sw = Stopwatch.StartNew();
        Console.WriteLine(
            $"Generating embeddings for {segmentsForEmbeddingsBag.Count} chunks, max sample size: {segmentsForEmbeddingsBag.Max(t => t.sample.Length)}...");

        var segmentsForEmbeddingsArray = segmentsForEmbeddingsBag.ToArray();

        // Batch processing to avoid API limits and memory issues
        const int batchSize = 1024; // Adjust based on your API limits
        var embeddings = new List<Embedding<float>>(segmentsForEmbeddingsArray.Length);

        var totalBatches = (int)Math.Ceiling((double)segmentsForEmbeddingsArray.Length / batchSize);
        var currentBatch = 1;
        for (var batchStart = 0; batchStart < segmentsForEmbeddingsArray.Length; batchStart += batchSize)
        {
            var batchEnd = Math.Min(batchStart + batchSize, segmentsForEmbeddingsArray.Length);
            var batch = segmentsForEmbeddingsArray[batchStart..batchEnd];

            Console.WriteLine($"Processing batch {currentBatch}/{totalBatches} ({batchEnd - batchStart} items)...");

            var batchSw = Stopwatch.StartNew();
            var batchEmbeddings = await _embeddingGenerator
                .GenerateAsync(batch.Select(s => s.sample));
            batchSw.Stop();

            Console.WriteLine($"Processing batch {currentBatch}/{totalBatches} ({batchEnd - batchStart} items) completed after {batchSw.Elapsed}");

            embeddings.AddRange(batchEmbeddings);
            currentBatch++;
        }

        for (var i = 0; i < embeddings.Count; i++)
        {
            var embedding = embeddings[i];
            if (embedding.Vector.Length != 1024)
            {
                Console.WriteLine($"Warning: Invalid embedding dimension {embedding.Vector.Length} for sample: {segmentsForEmbeddingsArray[i].sample[..Math.Min(50, segmentsForEmbeddingsArray[i].sample.Length)]}");
            }

            // Check for zero vectors (indicates embedding failure)
            if (embedding.Vector.ToArray().All(v => Math.Abs(v) < 1e-6f))
            {
                Console.WriteLine($"Warning: Zero vector generated for sample: {segmentsForEmbeddingsArray[i].sample[..Math.Min(50, segmentsForEmbeddingsArray[i].sample.Length)]}");
            }
        }

        sw.Stop();
        Console.WriteLine($"Generating embeddings for {segmentsForEmbeddingsArray.Length} chunks completed after {sw.Elapsed}.");

        var documents = embeddings.Select((e, i) => (segmentsForEmbeddingsArray[i].embeddingPrefix,
                documentEmbeddings: new DocumentEmbeddings
                {
                    Sampling = segmentsForEmbeddingsArray[i].sample,
                    Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                    DocumentFolder = Path.GetRelativePath(documentsRootFolder,
                        segmentsForEmbeddingsArray[i].documentRootFolder),
                    DocumentMarkdownFilePath = Path.GetRelativePath(documentsRootFolder,
                        segmentsForEmbeddingsArray[i].documentMarkdownFilePath),
                    DocumentId = segmentsForEmbeddingsArray[i].documentId,
                    DocumentTitle = segmentsForEmbeddingsArray[i].documentTitle,
                    SamplingEmbedding = e.Vector
                })).GroupBy(pair => pair.embeddingPrefix)
            .Select(group => (key: group.Key, embeddings: group.ToArray()));

        sw.Restart();
        Console.WriteLine($"Upserting {segmentsForEmbeddingsArray.Length} documents...");

        const int upsertBatchSize = 1000; // Adjust based on SQLite performance
        foreach (var byEmbeddingPrefixGrouping in documents)
        {
            Console.WriteLine($"Upserting {byEmbeddingPrefixGrouping.embeddings.Length} {byEmbeddingPrefixGrouping.key} documents...");
            using var collection = await GetCollectionAsync(byEmbeddingPrefixGrouping.key);

            var embeddingsToUpsert = byEmbeddingPrefixGrouping.embeddings.Select(p => p.documentEmbeddings).ToArray();
            totalBatches = (int)Math.Ceiling((double)embeddingsToUpsert.Length / batchSize);
            currentBatch = 1;
            for (var batchStart = 0; batchStart < embeddingsToUpsert.Length; batchStart += upsertBatchSize)
            {
                var batchEnd = Math.Min(batchStart + upsertBatchSize, embeddingsToUpsert.Length);
                var batch = embeddingsToUpsert[batchStart..batchEnd];

                Console.WriteLine($"Upserting batch {currentBatch}/{totalBatches} ({batch.Length} items)...");
                var batchSw = Stopwatch.StartNew();
                await collection.UpsertAsync(batch);
                batchSw.Stop();
                Console.WriteLine($"Upserting batch {currentBatch}/{totalBatches} ({batch.Length} items) completed after {batchSw.Elapsed}");
                currentBatch++;
            }
        }

        sw.Stop();
        Console.WriteLine($"Upserting {segmentsForEmbeddingsArray.Length} documents completed after {sw.Elapsed}.");
    }

    private async
        IAsyncEnumerable<(string embeddingPrefix, string documentRootFolder, string documentMarkdownFilePath, string documentId, string documentTitle, string
            sample)> GetSamplesForEmbeddingsAsync(string file,
            Func<ConfluenceDocumentMetadata, string, IEnumerable<string>>? markdownToEmbeddingsTextsFunc,
            string[] embeddingPrefixes)
    {
        var parentFolder = Path.GetDirectoryName(file)!;
        if (Directory.GetFiles(parentFolder).Length == 0) yield break;

        var metadataPath = Path.Combine(parentFolder, "_Metadata.json");
        if (!File.Exists(metadataPath)) yield break;

        // ReSharper disable once MethodSupportsCancellation
        var metadata =
            JsonConvert.DeserializeObject<ConfluenceDocumentMetadata>(
                await File.ReadAllTextAsync(metadataPath)) ??
            throw new InvalidOperationException($"Failed to deserialize metadata file in {parentFolder}");
        var title = metadata.Title.Replace("- SW Requirements", "", StringComparison.OrdinalIgnoreCase).Trim();

        foreach (var series in SeriesHelper.GetSeriesOccurrences(title)
                     .Concat(embeddingPrefixes.Select(p => p.ToUpper()))
                     .Concat(GetSeriesFromLabels(metadata))
                     .Distinct())
        {
            using var collection = await GetCollectionAsync(series);

            //If this is a new entry, we need to add the title as a sampling
            if (await collection.GetAsync(title) is null)
            {
                yield return (series, parentFolder, file, metadata.Id, metadata.Title, title);
            }

            var markdown = await File.ReadAllTextAsync(file);
            var markdownSegments = SegmentationUtils.ConvertMarkdownToSegments(markdown);

            foreach (var segment in markdownSegments)
            {
                yield return (series, parentFolder, file, metadata.Id, metadata.Title, $"{metadata.Title} {segment.key}");
            }

            if (markdownToEmbeddingsTextsFunc is null) yield break;

            foreach (var sample in markdownToEmbeddingsTextsFunc(metadata, markdown))
            {
                yield return (series, parentFolder, file, metadata.Id, metadata.Title, $"{metadata.Title} {sample}");
            }
        }
    }

    private async
        IAsyncEnumerable<(string embeddingsPrefix, string documentFolder, string documentMarkdownFilePath, string documentId, string documentTitle, string sample)> GetSoftwareRequirementsFileSamplesForEmbeddingsAsync(string file, string[] embeddingPrefixes)
    {
        var documentFolder = Path.GetDirectoryName(file)!;
        if (Directory.GetFiles(documentFolder).Length == 0) yield break;

        var metadataPath = Path.Combine(documentFolder, "_Metadata.json");
        if (!File.Exists(metadataPath)) yield break;

        // ReSharper disable once MethodSupportsCancellation
        var metadata =
            JsonConvert.DeserializeObject<ConfluenceDocumentMetadata>(
                await File.ReadAllTextAsync(metadataPath)) ??
            throw new InvalidOperationException($"Failed to deserialize metadata file in {documentFolder}");
        var title = metadata.Title
            .Replace("- SW Requirements", "", StringComparison.OrdinalIgnoreCase)
            .Replace("- Software Requirements", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        foreach (var series in SeriesHelper.GetSeriesOccurrences(title)
                     .Concat(GetSeriesFromLabels(metadata))
                     .Concat(embeddingPrefixes.Select(p => p.ToUpper()))
                     .Distinct())
        {
            using var collection = await GetCollectionAsync(series);

            //If this is a new entry, we need to add the title as a sampling
            if (await collection.GetAsync(title) is null)
            {
                yield return (series, documentFolder, file, metadata.Id, metadata.Title, title);
            }

            var markdownSegments = SegmentationUtils.ConvertMarkdownToSegments(await File.ReadAllTextAsync(file));

            //Background & Motivation segments
            await foreach (var samplingSegment in ProcessSegmentsAsync(collection, series, markdownSegments
                                   .Where(s => (s.key.Contains("Background & Motivation", StringComparison.OrdinalIgnoreCase) ||
                                                s.key.Contains("Background and Motivation", StringComparison.OrdinalIgnoreCase)) &&
                                               !string.IsNullOrEmpty(s.content.Trim()))
                                   .Select(keyAndContentPair => $"{title} {keyAndContentPair.key}: {keyAndContentPair.content.Trim()}")
                               /*.Concat(
                           [
                               $"Summarize {title}",
                               $"Explain {title}",
                               $"Overview {title}"
                           ])*/))
            {
                yield return samplingSegment;
            }

            var backgroundAndMotivation = string.Join("\r\n", markdownSegments.Where(s =>
                    (s.key.Contains("Background & Motivation", StringComparison.OrdinalIgnoreCase) ||
                     s.key.Contains("Background and Motivation", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(s.content)).Select(s => $"{s.key}:\r\n{s.content.Trim()}"));
            var llmSamplesForBackgroundSection = await GenerateBackgroundSamplesAsync(documentFolder, file, title, backgroundAndMotivation);
            foreach (var llmSample in llmSamplesForBackgroundSection.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                Console.WriteLine($"LLM Sample for Background segment of {title}: {llmSample}");
                yield return (series, documentFolder, file, metadata.Id, metadata.Title, llmSample);
            }

            // Requirements segments
            await foreach (var samplingSegment in ProcessSegmentsAsync(collection, series, markdownSegments
                               .Where(s => s.key.Contains("Requirements", StringComparison.OrdinalIgnoreCase) &&
                                           !string.IsNullOrEmpty(s.content.Trim()))
                               .SelectMany(keyAndContentPair => new[]
                               {
                                   /*$"Explain {keyAndContentPair.key} for {title}",
                               $"Summarize {keyAndContentPair.key} for {title}",
                               $"What are {keyAndContentPair.key} requirements for {title}"*/
                                   $"{title} - Software requirements - {keyAndContentPair.key}"
                               })
                               .Concat(
                               [
                                   /*$"Overview the software requirements for {title}",
                               $"Explain the software requirements for {title}",
                               $"Summarize the software requirements for {title}",*/
                                   $"Software requirements for {title}"
                               ])))
            {
                yield return samplingSegment;
            }

            // Scenario Analysis / User Story segments
            await foreach (var samplingSegment in ProcessSegmentsAsync(collection, series, markdownSegments
                               .Where(s => s.key.Contains("Scenario Analysis / User Story", StringComparison.OrdinalIgnoreCase) &&
                                           !string.IsNullOrEmpty(s.content.Trim()))
                               .SelectMany(keyAndContentPair => new[]
                               {
                                   /*$"Explain {keyAndContentPair.key} for {title}",
                               $"Summarize {keyAndContentPair.key} for {title}",
                               $"What are {keyAndContentPair.key} for {title}"*/
                                   $"{title} - User stories / Scenarios - {keyAndContentPair.key}",
                               })
                               .Concat(
                               [
                                   /*$"Overview the user stories / scenarios for {title}",
                               $"Explain the user stories / scenarios for {title}",
                               $"Summarize the user stories / scenarios for {title}",*/
                                   $"User stories / scenarios for {title}"
                               ])))
            {
                yield return samplingSegment;
            }
        }

        yield break;

        async IAsyncEnumerable<(string embeddingsPrefix, string documentFolder, string documentMarkdownFilePath, string documentId, string documentTitle, string sample)> ProcessSegmentsAsync(SqliteCollection<string, DocumentEmbeddings> collection, string series, IEnumerable<string> samplings)
        {
            /*var chunks = samplings.SelectMany(sampling =>
                sampling.Length < 2000
                    ? [sampling]
                    : Chunk(sampling, maxChars: 2000 - metadata.Title.Length).Select(s => $"{metadata.Title} - {s}"));*/

            var samplingsHashSet = samplings.ToHashSet();
            await foreach (var documentEmbedding in
                           collection.GetAsync(samplingsHashSet))
            {
                samplingsHashSet.Remove(documentEmbedding.Sampling);
            }
            foreach (var sampling in samplingsHashSet)
            {
                yield return (series, documentFolder, file, metadata.Id, metadata.Title, sampling);
            }
        }
    }

    private async Task<string[]> GenerateBackgroundSamplesAsync(string documentFolder, string contentFilePath, string title, string backgroundAndMotivation)
    {
        var samplesFilePath = Path.Combine(documentFolder,
            Path.GetFileNameWithoutExtension(contentFilePath).Replace("_Markdown", "") + "_samples.txt");
        if (File.Exists(samplesFilePath))
            return await File.ReadAllLinesAsync(samplesFilePath);
        
        var response = await _chatClient.GetResponseAsync(new ChatMessage(ChatRole.User,
            $"""
              Rephrase the following input into a short, focused text of 1–2 sentences that addresses only the specific feature titled '{title}'.
              
              Requirements:
              - Include only content that is directly and uniquely relevant to the '{title}' feature.
              - Remove all general system context, domain background, or broad motivations that are not specific to this feature.
              - Do not introduce new information or expand beyond what appears in the input.
              - The result must be optimized for semantic embeddings, prioritizing relevance and meaning over human-friendly phrasing.
              - The output should read as a concise, self-contained description of the feature’s background and motivation.
              
              Input:
              {backgroundAndMotivation}
              """));
        var sample = $"{title} - {response.Text.Trim()}";
        await File.WriteAllTextAsync(samplesFilePath, $"{sample}");
        return [sample];
    }

    private async
        IAsyncEnumerable<(string embeddingsPrefix, string documentFolder, string documentMarkdownFilePath, string documentId, string documentTitle, string sample)> GetSoftwareDesignFileSamplesForEmbeddingsAsync(string file, string[] embeddingPrefixes)
    {
        var documentFolder = Path.GetDirectoryName(file)!;
        if (Directory.GetFiles(documentFolder).Length == 0) yield break;

        var metadataPath = Path.Combine(documentFolder, "_Metadata.json");
        if (!File.Exists(metadataPath)) yield break;

        // ReSharper disable once MethodSupportsCancellation
        var metadata =
            JsonConvert.DeserializeObject<ConfluenceDocumentMetadata>(
                await File.ReadAllTextAsync(metadataPath)) ??
            throw new InvalidOperationException($"Failed to deserialize metadata file in {documentFolder}");
        var title = metadata.Title.Replace("Design Document", "", StringComparison.OrdinalIgnoreCase)
            .Replace("SW Design", "", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('-', ' ', '.');

        foreach (var series in SeriesHelper.GetSeriesOccurrences(title)
                     .Concat(GetSeriesFromLabels(metadata))
                     .Concat(embeddingPrefixes.Select(p => p.ToUpper()))
                     .Distinct())
        {
            using var collection = await GetCollectionAsync(series);

            //If this is a new entry, we need to add the title as a sampling
            if (await collection.GetAsync(title) is null)
            {
                yield return (series, documentFolder, file, metadata.Id, metadata.Title, title);
            }

            var markdownSegments = SegmentationUtils.ConvertMarkdownToSegments(await File.ReadAllTextAsync(file));

            //Background - Header and content
            await foreach (var samplingSegment in ProcessSegmentsAsync(collection, series, markdownSegments
                                   .Where(s => (s.key.Contains("Introduction > Background", StringComparison.OrdinalIgnoreCase)) &&
                                               !string.IsNullOrEmpty(s.content.Trim()))
                                   .Select(keyAndContentPair => $"{title} {keyAndContentPair.key}: {keyAndContentPair.content.Trim()}")))
            {
                yield return samplingSegment;
            }

            // Other segments - just headers
            await foreach (var samplingSegment in ProcessSegmentsAsync(collection, series, markdownSegments
                               .Select(keyAndContentPair => $"{title} - {keyAndContentPair.key}")))
            {
                yield return samplingSegment;
            }
        }

        yield break;

        async IAsyncEnumerable<(string embeddingsPrefix, string documentFolder, string documentMarkdownFilePath, string documentId, string documentTitle, string sample)> ProcessSegmentsAsync(SqliteCollection<string, DocumentEmbeddings> collection, string series, IEnumerable<string> samplings)
        {
            var samplingsHashSet = samplings.ToHashSet();
            await foreach (var documentEmbedding in
                           collection.GetAsync(samplingsHashSet))
            {
                samplingsHashSet.Remove(documentEmbedding.Sampling);
            }
            foreach (var sampling in samplingsHashSet)
            {
                yield return (series, documentFolder, file, metadata.Id, metadata.Title, sampling);
            }
        }
    }

    // ReSharper disable once UnusedMember.Local
    // ReSharper disable UnusedParameter.Local
    private static IEnumerable<string> Chunk(string text, int maxChars, int overlap = 200)
    {
        /*for (var start = 0; start < text.Length; start += maxChars - overlap)
        {
            var end = Math.Min(start + maxChars, text.Length);
            yield return text[start..end];
            if (end == text.Length) break;
        }*/
        yield return text;
    }

    private async Task<SqliteCollection<string, DocumentEmbeddings>> GetCollectionAsync(string collectionName)
    {
        var collection = new SqliteCollection<string, DocumentEmbeddings>($"Data Source={_dbFilePath}",
            $"{collectionName}-{nameof(DocumentEmbeddings)}");
        await collection.EnsureCollectionExistsAsync();
        
        return collection;
    }

    private static IEnumerable<string> GetSeriesFromLabels(ConfluenceDocumentMetadata metadata) =>
        metadata.Attributes?.Results is null
            ? []
            : metadata.Attributes.Results.Labels.SelectMany(label => SeriesHelper.GetSeriesOccurrences(label.Name));
}