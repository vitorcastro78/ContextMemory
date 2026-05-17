using System.Collections.Concurrent;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Knowledge;

public sealed class WikiIndexService : IWikiIndexService
{
    private readonly WikiLoader _wikiLoader;
    private readonly VectorStore _vectorStore;
    private readonly SimilaritySearch _similaritySearch;
    private readonly IEmbeddingEngine _embeddingEngine;
    private readonly ILogger<WikiIndexService> _logger;
    private readonly ConcurrentDictionary<string, byte> _indexedApps = new();

    public WikiIndexService(
        WikiLoader wikiLoader,
        VectorStore vectorStore,
        SimilaritySearch similaritySearch,
        IEmbeddingEngine embeddingEngine,
        ILogger<WikiIndexService> logger)
    {
        _wikiLoader = wikiLoader;
        _vectorStore = vectorStore;
        _similaritySearch = similaritySearch;
        _embeddingEngine = embeddingEngine;
        _logger = logger;
    }

    public async Task EnsureIndexedAsync(string appId, string wikiPath, CancellationToken cancellationToken = default)
    {
        wikiPath = Path.GetFullPath(wikiPath);
        if (!Directory.Exists(wikiPath))
        {
            _logger.LogWarning("Wiki path not found for {AppId}: {WikiPath}", appId, wikiPath);
            return;
        }

        if (_indexedApps.ContainsKey(appId))
            return;

        if (await _vectorStore.TryLoadFromCacheAsync(appId, wikiPath, cancellationToken).ConfigureAwait(false))
        {
            _indexedApps[appId] = 0;
            return;
        }

        if (!_embeddingEngine.IsAvailable)
        {
            _logger.LogWarning("Embedding engine unavailable — wiki for {AppId} not indexed", appId);
            return;
        }

        await ReindexAllAsync(appId, wikiPath, cancellationToken).ConfigureAwait(false);
        _indexedApps[appId] = 0;
    }

    public async Task ReindexFileAsync(
        string appId,
        string wikiPath,
        string relativeFilePath,
        CancellationToken cancellationToken = default)
    {
        wikiPath = Path.GetFullPath(wikiPath);
        var fullPath = Path.Combine(wikiPath, relativeFilePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            await RemoveFileEntriesAsync(appId, relativeFilePath.Replace('\\', '/'), wikiPath, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!_embeddingEngine.IsAvailable)
            return;

        var chunks = _wikiLoader.LoadFile(wikiPath, fullPath);
        var entries = await BuildEntriesAsync(appId, chunks, cancellationToken).ConfigureAwait(false);
        var source = relativeFilePath.Replace('\\', '/');
        await _vectorStore
            .ReplaceFileEntriesAsync(appId, source, entries, wikiPath, cancellationToken)
            .ConfigureAwait(false);

        _indexedApps[appId] = 0;
        _logger.LogInformation("Re-indexed wiki file {Source} for {AppId} ({Count} chunks)", source, appId, entries.Count);
    }

    public async Task<IReadOnlyList<WikiChunk>> SearchAsync(
        string appId,
        string query,
        int topK,
        float threshold,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || !_embeddingEngine.IsAvailable)
            return [];

        var entries = _vectorStore.GetEntries(appId);
        if (entries.Count == 0)
            return [];

        var queryVector = await _embeddingEngine.EmbedAsync(query, cancellationToken).ConfigureAwait(false);
        return _similaritySearch.Search(entries, queryVector, topK, threshold);
    }

    private async Task ReindexAllAsync(string appId, string wikiPath, CancellationToken cancellationToken)
    {
        var allChunks = new List<WikiChunk>();
        foreach (var file in Directory.EnumerateFiles(wikiPath, "*.md", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            allChunks.AddRange(_wikiLoader.LoadFile(wikiPath, file));
        }

        var entries = await BuildEntriesAsync(appId, allChunks, cancellationToken).ConfigureAwait(false);
        await _vectorStore.ReplaceAllEntriesAsync(appId, entries, wikiPath, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Full wiki index built for {AppId} ({Count} chunks)", appId, entries.Count);
    }

    private async Task RemoveFileEntriesAsync(
        string appId,
        string source,
        string wikiPath,
        CancellationToken cancellationToken)
    {
        await _vectorStore.ReplaceFileEntriesAsync(appId, source, [], wikiPath, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<VectorEntry>> BuildEntriesAsync(
        string appId,
        IReadOnlyList<WikiChunk> chunks,
        CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
            return [];

        var texts = chunks.Select(c => c.Content).ToList();
        var vectors = await _embeddingEngine.EmbedBatchAsync(texts, cancellationToken).ConfigureAwait(false);
        var entries = new List<VectorEntry>(chunks.Count);

        for (var i = 0; i < chunks.Count; i++)
        {
            entries.Add(new VectorEntry
            {
                AppId = appId,
                Text = chunks[i].Content,
                Source = chunks[i].Source,
                Vector = vectors[i]
            });
        }

        return entries;
    }
}
