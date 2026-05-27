using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Knowledge;

/// <summary>Bridges legacy in-memory VectorStore to IPgVectorStore for File persistence mode.</summary>
public sealed class FileVectorStoreAdapter : IPgVectorStore
{
    private readonly VectorStore _vectorStore;

    public FileVectorStoreAdapter(VectorStore vectorStore) => _vectorStore = vectorStore;

    public Task<bool> TryLoadFromCacheAsync(string appId, string wikiPath, CancellationToken cancellationToken = default) =>
        _vectorStore.TryLoadFromCacheAsync(appId, wikiPath, cancellationToken);

    public async Task UpsertChunksAsync(
        string appId,
        string wikiPath,
        IReadOnlyList<WikiChunkVector> chunks,
        CancellationToken cancellationToken = default)
    {
        var entries = chunks.Select(c => new VectorEntry
        {
            AppId = appId,
            Text = c.Content,
            Source = c.Source,
            Vector = c.Vector
        }).ToList();

        await _vectorStore.ReplaceAllEntriesAsync(appId, entries, wikiPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReplaceFileChunksAsync(
        string appId,
        string wikiPath,
        string source,
        IReadOnlyList<WikiChunkVector> chunks,
        CancellationToken cancellationToken = default)
    {
        var entries = chunks.Select(c => new VectorEntry
        {
            AppId = appId,
            Text = c.Content,
            Source = c.Source,
            Vector = c.Vector
        }).ToList();

        await _vectorStore.ReplaceFileEntriesAsync(appId, source, entries, wikiPath, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task DeleteBySourceAsync(string appId, string source, CancellationToken cancellationToken = default) =>
        ReplaceFileChunksAsync(appId, string.Empty, source, [], cancellationToken);

    public Task<IReadOnlyList<WikiChunkVector>> SearchAsync(
        string appId,
        float[] query,
        int topK,
        float threshold,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SearchInternal(appId, query, topK, threshold, learnedOnly: false));

    public Task<IReadOnlyList<WikiChunkVector>> SearchLearnedAsync(
        string appId,
        float[] query,
        int topK,
        float threshold,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SearchInternal(appId, query, topK, threshold, learnedOnly: true));

    public Task<int> GetChunkCountAsync(string appId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_vectorStore.GetEntries(appId).Count);

    private IReadOnlyList<WikiChunkVector> SearchInternal(
        string appId,
        float[] query,
        int topK,
        float threshold,
        bool learnedOnly)
    {
        var entries = _vectorStore.GetEntries(appId);
        if (learnedOnly)
            entries = entries.Where(e => e.Source.Contains("learned/", StringComparison.OrdinalIgnoreCase)).ToList();

        var scored = entries
            .Select(e => (Entry: e, Score: SimilaritySearch.CosineSimilarity(query, e.Vector)))
            .Where(x => x.Score >= threshold)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

        return scored.Select(x => new WikiChunkVector
        {
            Source = x.Entry.Source,
            HeaderPath = string.Empty,
            Content = x.Entry.Text,
            Vector = x.Entry.Vector,
            IsLearned = x.Entry.Source.Contains("learned/", StringComparison.OrdinalIgnoreCase),
            Similarity = x.Score
        }).ToList();
    }
}
