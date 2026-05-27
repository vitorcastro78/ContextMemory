using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IPgVectorStore
{
    Task<bool> TryLoadFromCacheAsync(string appId, string wikiPath, CancellationToken cancellationToken = default);

    Task UpsertChunksAsync(
        string appId,
        string wikiPath,
        IReadOnlyList<WikiChunkVector> chunks,
        CancellationToken cancellationToken = default);

    Task ReplaceFileChunksAsync(
        string appId,
        string wikiPath,
        string source,
        IReadOnlyList<WikiChunkVector> chunks,
        CancellationToken cancellationToken = default);

    Task DeleteBySourceAsync(string appId, string source, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WikiChunkVector>> SearchAsync(
        string appId,
        float[] query,
        int topK,
        float threshold,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WikiChunkVector>> SearchLearnedAsync(
        string appId,
        float[] query,
        int topK,
        float threshold,
        CancellationToken cancellationToken = default);

    Task<int> GetChunkCountAsync(string appId, CancellationToken cancellationToken = default);
}
