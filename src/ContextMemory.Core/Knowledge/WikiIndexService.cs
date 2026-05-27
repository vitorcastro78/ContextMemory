using System.Collections.Concurrent;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.Knowledge;

public sealed class WikiIndexService : IWikiIndexService
{
    private readonly WikiLoader _wikiLoader;
    private readonly IPgVectorStore _vectorStore;
    private readonly IEmbeddingEngine _embeddingEngine;
    private readonly ILogger<WikiIndexService> _logger;
    private readonly ConcurrentDictionary<string, byte> _indexedApps = new();

    public WikiIndexService(
        WikiLoader wikiLoader,
        IPgVectorStore vectorStore,
        IEmbeddingEngine embeddingEngine,
        ILogger<WikiIndexService> logger)
    {
        _wikiLoader = wikiLoader;
        _vectorStore = vectorStore;
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
            var source = relativeFilePath.Replace('\\', '/');
            await _vectorStore.DeleteBySourceAsync(appId, source, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_embeddingEngine.IsAvailable)
            return;

        var chunks = _wikiLoader.LoadFile(wikiPath, fullPath);
        var sourcePath = relativeFilePath.Replace('\\', '/');
        var entries = await BuildChunkVectorsAsync(appId, chunks, cancellationToken).ConfigureAwait(false);
        await _vectorStore
            .ReplaceFileChunksAsync(appId, wikiPath, sourcePath, entries, cancellationToken)
            .ConfigureAwait(false);

        _indexedApps[appId] = 0;
        _logger.LogInformation("Re-indexed wiki file {Source} for {AppId} ({Count} chunks)", sourcePath, appId, entries.Count);
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

        var count = await _vectorStore.GetChunkCountAsync(appId, cancellationToken).ConfigureAwait(false);
        if (count == 0)
            return [];

        var queryVector = await _embeddingEngine.EmbedAsync(query, cancellationToken).ConfigureAwait(false);
        var hits = await _vectorStore
            .SearchAsync(appId, queryVector, topK, threshold, cancellationToken)
            .ConfigureAwait(false);

        return hits
            .Select(h => new WikiChunk(h.Content, h.Source, h.HeaderPath))
            .ToList();
    }

    private async Task ReindexAllAsync(string appId, string wikiPath, CancellationToken cancellationToken)
    {
        var allChunks = new List<WikiChunk>();
        foreach (var file in Directory.EnumerateFiles(wikiPath, "*.md", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            allChunks.AddRange(_wikiLoader.LoadFile(wikiPath, file));
        }

        var entries = await BuildChunkVectorsAsync(appId, allChunks, cancellationToken).ConfigureAwait(false);
        await _vectorStore.UpsertChunksAsync(appId, wikiPath, entries, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Full wiki index built for {AppId} ({Count} chunks)", appId, entries.Count);
    }

    private async Task<IReadOnlyList<WikiChunkVector>> BuildChunkVectorsAsync(
        string appId,
        IReadOnlyList<WikiChunk> chunks,
        CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
            return [];

        var texts = chunks.Select(c => c.Content).ToList();
        var vectors = await _embeddingEngine.EmbedBatchAsync(texts, cancellationToken).ConfigureAwait(false);
        var result = new List<WikiChunkVector>(chunks.Count);

        for (var i = 0; i < chunks.Count; i++)
        {
            result.Add(new WikiChunkVector
            {
                Source = chunks[i].Source,
                HeaderPath = chunks[i].HeaderPath,
                Content = chunks[i].Content,
                Vector = vectors[i],
                IsLearned = chunks[i].Source.Contains("learned/", StringComparison.OrdinalIgnoreCase)
            });
        }

        return result;
    }
}
