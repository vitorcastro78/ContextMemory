using ContextMemory.Core.Contracts;

namespace ContextMemory.Core.Knowledge;

public sealed class WikiVectorSearchAdapter : IWikiVectorSearch
{
    private readonly IPgVectorStore _vectorStore;
    private readonly IEmbeddingEngine _embeddingEngine;

    public WikiVectorSearchAdapter(IPgVectorStore vectorStore, IEmbeddingEngine embeddingEngine)
    {
        _vectorStore = vectorStore;
        _embeddingEngine = embeddingEngine;
    }

    public async Task<IReadOnlyList<WikiVectorHit>> SearchLearnedAsync(
        string appId,
        string queryText,
        int topK,
        float threshold,
        CancellationToken cancellationToken = default)
    {
        if (!_embeddingEngine.IsAvailable || string.IsNullOrWhiteSpace(queryText))
            return [];

        var queryVector = await _embeddingEngine.EmbedAsync(queryText, cancellationToken).ConfigureAwait(false);
        var hits = await _vectorStore
            .SearchLearnedAsync(appId, queryVector, topK, threshold, cancellationToken)
            .ConfigureAwait(false);

        return hits.Select(h => new WikiVectorHit(h.Source, h.Content, h.Similarity)).ToList();
    }
}
